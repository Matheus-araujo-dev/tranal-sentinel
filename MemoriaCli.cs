using System;
using System.Globalization;
using System.Linq;
using System.Text;

namespace TranalSentinel;

/// <summary>
/// TRANAL MEMÓRIA na linha de comando: lê o SPD dos módulos de memória e
/// mostra o resultado em texto.
///
/// <para>
/// Uso: <c>TranalSentinel.exe --memoria</c>
///
/// É o mesmo caminho do app (mesmo leitor, mesmo decodificador, mesma
/// regra), sem abrir janela. Serve para conferir a leitura do SPD por
/// script e para diagnosticar uma máquina sem passar pela interface.
/// </para>
/// </summary>
public static class MemoriaCli
{
    /// <summary>Deu certo.</summary>
    public const int CodigoOk = 0;

    /// <summary>Erro de execução (sem permissão, sem driver PawnIO...).</summary>
    public const int CodigoErro = 1;

    /// <summary>Texto de uso.</summary>
    public static string TextoUso => """
        TRANAL SENTINEL · TRANAL MEMÓRIA (linha de comando)

        Uso:
          TranalSentinel.exe --memoria

        Lê o SPD de cada módulo de memória pelo barramento SMBus e mostra
        capacidades, tempos e perfis de overclock.

        Somente leitura: nada do que está gravado no módulo é alterado.
        Precisa do driver PawnIO instalado e de execução como administrador.
        """;

    /// <summary>Executa a CLI de memória.</summary>
    public static int Executar(string[] args)
    {
        try
        {
            Console.OutputEncoding = Encoding.UTF8;

            MemoriaColetaResultado resultado = MemoriaColeta.Coletar(forcar: true);
            Console.WriteLine(MontarRelatorio(resultado));

            return CodigoOk;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Erro inesperado: " + ex.Message);
            return CodigoErro;
        }
    }

    /// <summary>Monta o relatório em texto.</summary>
    public static string MontarRelatorio(MemoriaColetaResultado resultado)
    {
        var sb = new StringBuilder();

        sb.AppendLine("TRANAL SENTINEL · TRANAL MEMÓRIA");
        sb.AppendLine($"Leitura do SPD dos módulos · {DateTime.Now:dd/MM/yyyy HH:mm:ss}");
        sb.AppendLine(new string('=', 64));
        sb.AppendLine();

        if (!resultado.Disponivel)
        {
            sb.AppendLine("Não foi possível ler os módulos de memória.");
            if (!string.IsNullOrWhiteSpace(resultado.Erro)) sb.AppendLine("Motivo: " + resultado.Erro);
            return sb.ToString();
        }

        sb.AppendLine($"Barramento: {(resultado.Barramento == SmbusModelo.AmdFch ? "AMD FCH (SMBus do processador)" : "Intel ICH/PCH (SMBus do processador)")}");
        sb.AppendLine($"Módulos: {resultado.Modulos.Count} · Capacidade total: {MemoriaTexto.Bytes(resultado.CapacidadeTotalBytes)}");

        // Quem manda é o módulo mais fraco: a mesma regra que a tela usa.
        SpdModulo? moduloManda = MemoriaRegras.ModuloQueManda(resultado.Modulos.Select(m => m.Modulo));

        // Tempos ativos: o que a BIOS programou de verdade. A comparação com
        // o perfil é feita uma vez e usada pelas duas seções.
        int velocidadeConfig = resultado.VelocidadeConfigMts ?? 0;
        RelatorioTimings? relatorio = null;
        if (resultado.TimingsAtivos is TimingsAtivos ativos && moduloManda != null)
        {
            relatorio = MemoriaRegras.CompararTimings(ativos, moduloManda, velocidadeConfig);
        }

        if (velocidadeConfig > 0 && moduloManda != null)
        {
            ModoMemoria modo = MemoriaRegras.Classificar(velocidadeConfig, moduloManda);
            sb.AppendLine($"Clock configurado na BIOS: {velocidadeConfig} MT/s");
            sb.AppendLine($"Situação: {MemoriaRegras.Descrever(modo, velocidadeConfig, moduloManda, relatorio)}");
        }

        if (relatorio != null && resultado.TimingsAtivos is TimingsAtivos temposAtivos)
        {
            sb.AppendLine();
            sb.AppendLine("TEMPOS ATIVOS (lidos dos registradores do controlador de memória)");
            sb.AppendLine($"  {MemoriaAtivosTexto.Resumo(temposAtivos)}");
            sb.AppendLine($"  Contra o {relatorio.Referencia}: {relatorio.Resumo}");
            sb.AppendLine("  (tempos em ciclos: a coluna ativa usa o clock do controlador, a do perfil o clock do perfil)");
            foreach (MemoriaAtivoLinhaVm linha in MemoriaAtivosTexto.Linhas(relatorio))
            {
                sb.AppendLine($"  {linha.Nome,-9} ativo {linha.Ativo,4} · perfil {linha.Referencia,4} · {linha.Situacao}");
            }
        }
        else if (!string.IsNullOrWhiteSpace(resultado.ErroTimingsAtivos))
        {
            sb.AppendLine();
            sb.AppendLine($"TEMPOS ATIVOS: não disponíveis — {resultado.ErroTimingsAtivos}");
        }

        foreach (MemoriaSpd modulo in resultado.Modulos)
        {
            SpdModulo spd = modulo.Modulo;
            SpdPerfil? perfil = spd.MelhorPerfil;

            sb.AppendLine();
            sb.AppendLine($"MÓDULO A{modulo.EnderecoSmbus - SpdLeitor.PrimeiroEndereco + 1} · SMBus 0x{modulo.EnderecoSmbus:X2}");
            sb.AppendLine($"  {spd.Fabricante} · {spd.PartNumber} · {spd.Gigabytes:0.#} GB");
            sb.AppendLine($"  {spd.TipoModulo} · {spd.Ranks} rank{(spd.Ranks > 1 ? "s" : "")} · {spd.LarguraBits} bits ({spd.LarguraChip}) · VDD {MemoriaTexto.Voltagem(spd.TensaoNominal)}");
            if (spd.Serial.Length > 0) sb.AppendLine($"  Serial: {spd.Serial} · fabricado em {spd.FabricadoEm} · DRAM {spd.FabricanteDram}");
            sb.AppendLine($"  Padrão JEDEC: {spd.Jedec.VelocidadeMts} MT/s · {spd.Jedec.ResumoCiclos} · tCK {MemoriaTexto.Ps(spd.Jedec.TckPs)}");
            sb.AppendLine($"  Perfil: {(perfil != null ? $"{perfil.Nome} · {perfil.Timing.VelocidadeMts} MT/s · {perfil.Timing.ResumoCiclos} · {MemoriaTexto.Voltagem(perfil.Voltagem)}" : "nenhum")}");

            SpdTiming tempos = perfil?.Timing ?? spd.Jedec;
            sb.AppendLine($"  Tempos: tAA {MemoriaTexto.Ns(tempos.TaaNs)} · tRCD {MemoriaTexto.Ns(tempos.TrcdNs)} · tRP {MemoriaTexto.Ns(tempos.TrpNs)}");
            sb.AppendLine($"         tRAS {MemoriaTexto.Ns(tempos.TrasNs)} · tRC {MemoriaTexto.Ns(tempos.TrcNs)} · tRFC1 {MemoriaTexto.Ns(tempos.Trfc1Ns)}");
            sb.AppendLine($"         tFAW {MemoriaTexto.Ns(tempos.TfawNs)} · tRRD {MemoriaTexto.Ns(tempos.TrrdSNs)} / {MemoriaTexto.Ns(tempos.TrrdLNs)} · tWR {MemoriaTexto.Ns(tempos.TwrNs)}");
        }

        sb.AppendLine();
        sb.AppendLine("Somente leitura: o conteúdo gravado nos módulos não foi alterado.");

        return sb.ToString();
    }
}
