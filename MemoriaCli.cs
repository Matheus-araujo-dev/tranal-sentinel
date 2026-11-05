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
        sb.AppendLine($"Módulos: {resultado.Modulos.Count} · Capacidade total: {FormatarBytes(resultado.CapacidadeTotalBytes)}");

        if (resultado.VelocidadeConfigMts is int configurada)
        {
            SpdModulo? primeiro = resultado.Modulos.FirstOrDefault()?.Modulo;
            if (primeiro != null)
            {
                ModoMemoria modo = MemoriaRegras.Classificar(configurada, primeiro);
                sb.AppendLine($"Clock configurado na BIOS: {configurada} MT/s");
                sb.AppendLine($"Situação: {MemoriaRegras.Descrever(modo, configurada, primeiro)}");
            }
        }

        foreach (MemoriaSpd modulo in resultado.Modulos)
        {
            SpdModulo spd = modulo.Modulo;
            SpdPerfil? perfil = spd.MelhorPerfil;

            sb.AppendLine();
            sb.AppendLine($"MÓDULO A{modulo.EnderecoSmbus - SpdLeitor.PrimeiroEndereco + 1} · SMBus 0x{modulo.EnderecoSmbus:X2}");
            sb.AppendLine($"  {spd.Fabricante} · {spd.PartNumber} · {spd.Gigabytes:0.#} GB");
            sb.AppendLine($"  {spd.TipoModulo} · {spd.Ranks} rank{(spd.Ranks > 1 ? "s" : "")} · {spd.LarguraBits} bits ({spd.LarguraChip}) · VDD {FormatarNumero(spd.TensaoNominal)} V");
            if (spd.Serial.Length > 0) sb.AppendLine($"  Serial: {spd.Serial} · fabricado em {spd.FabricadoEm} · DRAM {spd.FabricanteDram}");
            sb.AppendLine($"  Padrão JEDEC: {spd.Jedec.VelocidadeMts} MT/s · {spd.Jedec.ResumoCiclos} · tCK {FormatarNumero(spd.Jedec.TckPs)} ps");
            sb.AppendLine($"  Perfil: {(perfil != null ? $"{perfil.Nome} · {perfil.Timing.VelocidadeMts} MT/s · {perfil.Timing.ResumoCiclos} · {FormatarNumero(perfil.Voltagem)} V" : "nenhum")}");

            SpdTiming tempos = perfil?.Timing ?? spd.Jedec;
            sb.AppendLine($"  Tempos: tAA {FormatarNumero(tempos.TaaNs)} ns · tRCD {FormatarNumero(tempos.TrcdNs)} ns · tRP {FormatarNumero(tempos.TrpNs)} ns");
            sb.AppendLine($"         tRAS {FormatarNumero(tempos.TrasNs)} ns · tRC {FormatarNumero(tempos.TrcNs)} ns · tRFC1 {FormatarNumero(tempos.Trfc1Ns)} ns");
            sb.AppendLine($"         tFAW {FormatarNumero(tempos.TfawNs)} ns · tRRD {FormatarNumero(tempos.TrrdSNs)}/{FormatarNumero(tempos.TrrdLNs)} ns · tWR {FormatarNumero(tempos.TwrNs)} ns");
        }

        sb.AppendLine();
        sb.AppendLine("Somente leitura: o conteúdo gravado nos módulos não foi alterado.");

        return sb.ToString();
    }

    private static string FormatarNumero(double valor) =>
        valor.ToString("0.##", CultureInfo.InvariantCulture).Replace('.', ',');

    private static string FormatarBytes(long bytes) =>
        FormatarNumero(Math.Round(bytes / 1024.0 / 1024.0 / 1024.0, 1)) + " GB";
}
