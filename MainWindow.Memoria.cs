using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace TranalSentinel;

/// <summary>
/// Página TRANAL MEMÓRIA: mostra o que cada módulo de memória tem dentro.
///
/// <para>
/// Cada módulo carrega um SPD (Serial Presence Detect) de 512 bytes: o
/// "cartão de visita" que diz fabricante, capacidade, organização e os tempos
/// (timings) do padrão JEDEC e dos perfis XMP/EXPO. É a mesma informação que
/// o CPU-Z mostra na aba SPD.
///
/// </para>
/// <para>
/// A leitura é somente leitura: o app conversa com o barramento SMBus pelo
/// driver PawnIO e não altera o conteúdo gravado no módulo. A única escrita
/// é a troca de página do hub SPD, que é estado volátil do próprio hub.
///
/// </para>
/// </summary>
public partial class MainWindow
{
    private bool _memoriaInit, _memoriaBusy;

    private void InitMemoria()
    {
        if (_memoriaInit) return;
        _memoriaInit = true;

        MemoriaResumo.ItemsSource = null;
        MemoriaModulos.ItemsSource = null;
        MemoriaStatus.Text =
            "Clique em LER MÓDULOS para ler o SPD de cada módulo pelo SMBus. Nada do que está gravado na memória é alterado.";
    }

    private async void BtnMemoriaScan_Click(object sender, RoutedEventArgs e)
    {
        if (_memoriaBusy) return;

        _memoriaBusy = true;
        BtnMemoriaScan.IsEnabled = false;
        MemoriaStatus.Text = "Abrindo o SMBus do processador e lendo o SPD dos módulos…";

        try
        {
            // A leitura do SMBus é lenta (centenas de transações): fora da thread da interface.
            MemoriaColetaResultado resultado = await Task.Run(() => MemoriaColeta.Coletar(forcar: true));
            MostrarMemoria(resultado);
        }
        catch (Exception ex)
        {
            MemoriaResumo.ItemsSource = null;
            MemoriaModulos.ItemsSource = null;
            MemoriaStatus.Text = "Falhou na leitura da memória: " + ex.Message;
        }
        finally
        {
            _memoriaBusy = false;
            BtnMemoriaScan.IsEnabled = true;
        }
    }

    private void MostrarMemoria(MemoriaColetaResultado resultado)
    {
        if (!resultado.Disponivel)
        {
            MemoriaResumo.ItemsSource = null;
            MemoriaModulos.ItemsSource = null;
            MemoriaStatus.Text = resultado.Erro ?? "Não foi possível ler os módulos de memória.";
            return;
        }

        var resumo = new List<InfoNode>();

        resumo.Add(new InfoNode { Header = "Módulos encontrados", Value = resultado.Modulos.Count.ToString() });
        resumo.Add(new InfoNode
        {
            Header = "Capacidade total",
            Value = MemoriaTexto.Bytes(resultado.CapacidadeTotalBytes)
        });
        resumo.Add(new InfoNode
        {
            Header = "Barramento",
            Value = resultado.Barramento == SmbusModelo.AmdFch
                ? "AMD FCH (SMBus do processador)"
                : "Intel ICH/PCH (SMBus do processador)"
        });

        // Quem manda é o módulo mais fraco (o que limita o clock do sistema):
        // a decisão mora em MemoriaRegras, e o TRANAL SAÚDE consome a mesma.
        SpdModulo? primeiro = MemoriaRegras.ModuloQueManda(resultado.Modulos.Select(m => m.Modulo));
        if (primeiro != null)
        {
            resumo.Add(new InfoNode
            {
                Header = "Padrão JEDEC",
                Value = primeiro.Jedec.VelocidadeMts > 0
                    ? $"{primeiro.Jedec.VelocidadeMts} MT/s · {primeiro.Jedec.ResumoCiclos}"
                    : "—"
            });

            SpdPerfil? perfil = primeiro.MelhorPerfil;
            resumo.Add(new InfoNode
            {
                Header = "Perfil do módulo",
                Value = perfil != null
                    ? $"{perfil.Nome} · {perfil.Timing.VelocidadeMts} MT/s · {perfil.Timing.ResumoCiclos} · {MemoriaTexto.Voltagem(perfil.Voltagem)}"
                    : "—"
            });
        }

        if (resultado.VelocidadeConfigMts is int configurada && primeiro != null)
        {
            ModoMemoria modo = MemoriaRegras.Classificar(configurada, primeiro);
            resumo.Add(new InfoNode
            {
                Header = "Configurado na BIOS",
                Value = $"{configurada} MT/s · {MemoriaRegras.Descrever(modo, configurada, primeiro)}"
            });
        }
        else if (resultado.VelocidadeMaximaMts is int maxima)
        {
            resumo.Add(new InfoNode { Header = "Velocidade máxima informada", Value = $"{maxima} MT/s" });
        }

        MemoriaResumo.ItemsSource = resumo;
        MemoriaModulos.ItemsSource = resultado.Modulos.Select(CriarModuloVm).ToList();
        MemoriaStatus.Text = $"Leitura concluída às {DateTime.Now:HH:mm:ss}.";
    }

    private static MemoriaModuloVm CriarModuloVm(MemoriaSpd modulo)
    {
        SpdModulo spd = modulo.Modulo;
        SpdPerfil? perfil = spd.MelhorPerfil;

        var vm = new MemoriaModuloVm
        {
            Titulo = $"MÓDULO {LetraSlot(modulo.EnderecoSmbus)} · SMBus 0x{modulo.EnderecoSmbus:X2}",
            Identidade = $"{spd.Fabricante} · {spd.PartNumber} · {spd.Gigabytes:0.#} GB",
            Detalhe = $"{spd.TipoModulo} · {spd.Ranks} rank{(spd.Ranks > 1 ? "s" : "")} · {spd.LarguraBits} bits ({spd.LarguraChip})" +
                      (spd.Serial.Length > 0 ? $" · SN {spd.Serial}" : "") +
                      (spd.FabricadoEm.Length > 0 ? $" · {spd.FabricadoEm}" : ""),
            Padrao = $"Padrão JEDEC: {spd.Jedec.VelocidadeMts} MT/s · {spd.Jedec.ResumoCiclos} · tCK {MemoriaTexto.Ps(spd.Jedec.TckPs)}",
            Perfil = perfil != null
                ? $"{perfil.Nome}: {perfil.Timing.VelocidadeMts} MT/s · {perfil.Timing.ResumoCiclos} · {MemoriaTexto.Voltagem(perfil.Voltagem)}" +
                  (perfil.Habilitado ? " · habilitado" : " · desabilitado")
                : "Sem perfil XMP/EXPO gravado no módulo."
        };

        SpdTiming tempos = perfil?.Timing ?? spd.Jedec;
        vm.Tempos.Add(new MemoriaLinhaVm { Nome = "tAA (latência CAS)", Valor = MemoriaTexto.Ns(tempos.TaaNs) });
        vm.Tempos.Add(new MemoriaLinhaVm { Nome = "tRCD", Valor = MemoriaTexto.Ns(tempos.TrcdNs) });
        vm.Tempos.Add(new MemoriaLinhaVm { Nome = "tRP", Valor = MemoriaTexto.Ns(tempos.TrpNs) });
        vm.Tempos.Add(new MemoriaLinhaVm { Nome = "tRAS", Valor = MemoriaTexto.Ns(tempos.TrasNs) });
        vm.Tempos.Add(new MemoriaLinhaVm { Nome = "tRC", Valor = MemoriaTexto.Ns(tempos.TrcNs) });
        vm.Tempos.Add(new MemoriaLinhaVm { Nome = "tRFC1", Valor = MemoriaTexto.Ns(tempos.Trfc1Ns) });
        vm.Tempos.Add(new MemoriaLinhaVm { Nome = "tRFC2", Valor = MemoriaTexto.Ns(tempos.Trfc2Ns) });
        vm.Tempos.Add(new MemoriaLinhaVm { Nome = "tRFC4", Valor = MemoriaTexto.Ns(tempos.Trfc4Ns) });
        vm.Tempos.Add(new MemoriaLinhaVm { Nome = "tFAW", Valor = MemoriaTexto.Ns(tempos.TfawNs) });
        vm.Tempos.Add(new MemoriaLinhaVm { Nome = "tRRD (s/l)", Valor = $"{MemoriaTexto.Ns(tempos.TrrdSNs)} / {MemoriaTexto.Ns(tempos.TrrdLNs)}" });
        vm.Tempos.Add(new MemoriaLinhaVm { Nome = "tWR", Valor = MemoriaTexto.Ns(tempos.TwrNs) });
        vm.Tempos.Add(new MemoriaLinhaVm { Nome = "tWTR (s/l)", Valor = $"{MemoriaTexto.Ns(tempos.TwtrSNs)} / {MemoriaTexto.Ns(tempos.TwtrLNs)}" });

        return vm;
    }

    /// <summary>Letra do slot a partir do endereço SMBus (0x50 = A1, 0x51 = A2...).</summary>
    private static string LetraSlot(int endereco) =>
        "A" + (endereco - SpdLeitor.PrimeiroEndereco + 1).ToString(CultureInfo.InvariantCulture);
}
