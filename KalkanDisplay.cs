using System;
using System.Collections.Generic;

namespace ResetExplode;

/// <summary>
/// Sessão com o visor Kalkan: abre a coleção HID certa, faz o handshake e envia
/// quadros de temperatura/ventoinha/carga.
///
/// O fluxo é o do app oficial (handshake com semente → quadro embaralhado a cada
/// ciclo), com duas adaptações deliberadas:
///   • a leitura de conferência depois de cada escrita usa 150 ms, e não 2 s
///     (o app oficial bloqueia até 2 s por quadro e segura o visor se ele calar);
///   • a ausência do marcador 0xE3 é tratada como falha de handshake — o app
///     oficial fecha o device e continua como se fosse só.
/// </summary>
public sealed class KalkanDisplay : IDisposable
{
    private readonly object _trava = new();
    private KalkanHid? _hid;
    private KalkanFrameBuilder? _quadros;

    /// <summary>Tempo de espera do handshake (o do app oficial).</summary>
    public const int TimeoutHandshakeMs = 2000;

    /// <summary>Tempo de espera da leitura de conferência depois de cada escrita.</summary>
    public const int TimeoutReadBackMs = 150;

    /// <summary>Tempo de espera da escrita no visor.</summary>
    public const int TimeoutEscritaMs = 1000;

    public bool Conectado { get { lock (_trava) return _hid != null; } }

    /// <summary>Semente do handshake em uso (0 enquanto desconectado).</summary>
    public byte Semente { get { lock (_trava) return _quadros?.Seed ?? 0; } }

    /// <summary>Índice do próximo quadro (o app oficial mantém o contador entre aberturas).</summary>
    public byte Indice { get { lock (_trava) return _quadros?.NextIndex ?? 0; } }

    /// <summary>Último erro registrado (null quando tudo certo).</summary>
    public string? Erro { get; private set; }

    /// <summary>Caminho da interface HID em uso.</summary>
    public string Caminho { get; private set; } = "";

    /// <summary>Coleção USB em uso (COL01, COL02…).</summary>
    public string Colecao { get; private set; } = "";

    /// <summary>Quantos quadros foram enviados desde a última conexão.</summary>
    public int QuadrosEnviados { get; private set; }

    /// <summary>Quantas respostas do visor chegaram na leitura de conferência.</summary>
    public int LeiturasRecebidas { get; private set; }

    /// <summary>Última resposta bruta do visor, em hexadecimal.</summary>
    public string UltimaLeituraHex { get; private set; } = "";

    /// <summary>Detalhes de como a conexão foi feita (para o log da aba).</summary>
    public string DetalheConexao { get; private set; } = "";

    /// <summary>
    /// Procura o visor, abre a coleção certa e faz o handshake.
    /// Devolve false (e preenche <see cref="Erro"/>) se não houver visor ou se ele não responder.
    /// </summary>
    public bool Conectar()
    {
        lock (_trava)
        {
            Desconectar();
            Erro = null;
            DetalheConexao = "";
            QuadrosEnviados = 0;
            LeiturasRecebidas = 0;
            UltimaLeituraHex = "";

            List<KalkanHid> candidatos;
            try
            {
                candidatos = KalkanHid.EnumerarCandidatos();
            }
            catch (Exception ex)
            {
                Erro = "falha ao enumerar dispositivos HID: " + ex.Message;
                return false;
            }

            if (candidatos.Count == 0)
            {
                Erro = "nenhum dispositivo HID do visor Kalkan (VID 0416 · PID 1100) encontrado";
                return false;
            }

            var tentativas = new System.Text.StringBuilder();
            foreach (KalkanHid candidato in candidatos)
            {
                string rotulo = candidato.Colecao.Length > 0 ? candidato.Colecao : "coleção desconhecida";
                byte[] pedido = KalkanProtocol.BuildSeedRequest();
                if (!candidato.Escrever(pedido, TimeoutEscritaMs, out string erroEscrita))
                {
                    tentativas.Append(rotulo + ": " + erroEscrita + "; ");
                    candidato.Dispose();
                    continue;
                }

                int lidos = candidato.Ler(TimeoutHandshakeMs, out string erroLeitura);
                if (lidos <= 0)
                {
                    tentativas.Append(rotulo + ": " + (erroLeitura.Length > 0 ? erroLeitura : "sem resposta") + "; ");
                    candidato.Dispose();
                    continue;
                }

                if (!KalkanProtocol.TryParseSeed(candidato.UltimaLeitura, out byte seed))
                {
                    tentativas.Append(rotulo + ": resposta sem marcador 0xE3 (" + candidato.UltimaLeituraHex + "); ");
                    candidato.Dispose();
                    continue;
                }

                _hid = candidato;
                _quadros = new KalkanFrameBuilder(seed);
                Caminho = candidato.Caminho;
                Colecao = rotulo;
                DetalheConexao = rotulo + " · semente 0x" + seed.ToString("X2")
                    + (candidato.PaginaDeUso != 0 ? " · uso 0x" + candidato.PaginaDeUso.ToString("X2") : "")
                    + " · relatório " + (candidato.TamanhoRelatorioSaida > 0 ? candidato.TamanhoRelatorioSaida.ToString() : "64") + " bytes";
                Erro = null;
                return true;
            }

            Erro = "o visor não respondeu ao handshake (" + tentativas.ToString().TrimEnd(' ', ';') + ")";
            return false;
        }
    }

    /// <summary>
    /// Envia um quadro com temperatura (°C), rotação da ventoinha (RPM) e carga
    /// da CPU (%). A leitura de conferência logo depois é opcional e curta.
    /// </summary>
    public bool Enviar(byte temperatura, ushort fan, byte cargaCpu)
    {
        lock (_trava)
        {
            if (_hid == null || _quadros == null)
            {
                Erro = "visor não conectado";
                return false;
            }

            byte[] quadro = _quadros.Next(temperatura, fan, cargaCpu);
            if (!_hid.Escrever(quadro, TimeoutEscritaMs, out string erro))
            {
                Erro = erro;
                Desconectar();
                return false;
            }

            QuadrosEnviados++;

            // Leitura de conferência: o visor devolve o quadro recebido.
            // Se calar, tudo bem: o quadro já foi enviado.
            int lidos = _hid.Ler(TimeoutReadBackMs, out string erroLeitura);
            if (lidos > 0)
            {
                LeiturasRecebidas++;
                UltimaLeituraHex = _hid.UltimaLeituraHex;
            }
            else if (lidos < 0 && erroLeitura.Length > 0)
            {
                Erro = erroLeitura;
            }

            Erro = null;
            return true;
        }
    }

    /// <summary>Fecha a sessão com o visor.</summary>
    public void Desconectar()
    {
        lock (_trava)
        {
            _hid?.Dispose();
            _hid = null;
            _quadros = null;
            Caminho = "";
            Colecao = "";
        }
    }

    public void Dispose() => Desconectar();
}
