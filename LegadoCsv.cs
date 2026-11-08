using System.Globalization;
using System.IO;

namespace TranalSentinel;

/// <summary>
/// O histórico antigo do TRANAL SENTINEL: os arquivos CSV que o app gravava
/// antes do banco de dados.
///
/// <para>
/// A partir da v0.28 o histórico mora num banco SQLite
/// (<see cref="Banco"/>). Este classe existe para uma coisa só: ler o que já
/// estava em CSV quando o banco é criado pela primeira vez, para o histórico
/// antigo não se perder na migração.
///
/// <para>
/// O formato é o mesmo de sempre — CSV sem cabeçalho, separador ";" e
/// timestamp ISO 8601 com milissegundos:
///
/// <code>
/// 2026-11-05T14:31:22.500;78.5;43;92.4;3200;71;100;2010;2048;87.4;12.5;0.3
/// </code>
///
/// <para>
/// Os arquivos legados <b>não são apagados</b> depois da migração: eles
/// ficam como cópia do histórico anterior. Por isso a leitura aqui nunca
/// escreve, e nunca apaga nada.
/// </para>
/// </summary>
public static class LegadoCsv
{
    /// <summary>
    /// Pasta onde ficava o log contínuo de sensores (o histórico antigo).
    /// </summary>
    public static string PastaSensores => PastaSensoresOverride ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TranalSentinel", "sensores");

    /// <summary>
    /// Pasta onde ficavam os arquivos de uso (um por dia).
    /// </summary>
    public static string PastaUso => PastaUsoOverride ?? Path.Combine(AppSettings.Folder, "uso");

    /// <summary>Redireciona a pasta do log de sensores (só os testes usam).</summary>
    internal static string? PastaSensoresOverride;

    /// <summary>Redireciona a pasta do histórico de uso (só os testes usam).</summary>
    internal static string? PastaUsoOverride;

    /// <summary>O arquivo do log contínuo de sensores.</summary>
    public static string ArquivoSensores => Path.Combine(PastaSensores, "historico.csv");

    /// <summary>
    /// O arquivo de uso de um dia.
    ///
    /// <para>
    /// A data sai em cultura invariante: o nome do arquivo é lido de volta
    /// com o mesmo critério, e um Windows em outra língua não pode escrever
    /// um nome que ninguém consegue ler.
    /// </para>
    /// </summary>
    public static string ArquivoDeUso(DateTime dia) =>
        Path.Combine(PastaUso, "uso-" + dia.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".csv");

    /// <summary>Quantos campos numéricos a linha de sensor tem (fora o timestamp).</summary>
    private const int CamposNumericos = 11;

    // ------------------------------------------------------------------ formato

    /// <summary>
    /// Monta a linha CSV de uma amostra. Função pura: não lê nem escreve nada.
    /// </summary>
    public static string MontarLinhaAmostra(AmostraSensor amostra)
    {
        var campos = new[]
        {
            Momento(amostra.Momento),
            Numero(amostra.CpuTemp),
            Numero(amostra.CpuCarga),
            Numero(amostra.CpuConsumo),
            Numero(amostra.CpuClock),
            Numero(amostra.GpuTemp),
            Numero(amostra.GpuCarga),
            Numero(amostra.GpuClock),
            Numero(amostra.GpuVram),
            Numero(amostra.RamUso),
            Numero(amostra.DiscoLeitura),
            Numero(amostra.DiscoEscrita)
        };
        return string.Join(";", campos);
    }

    /// <summary>
    /// Lê uma linha do CSV de sensores e devolve a amostra. Função pura.
    ///
    /// <para>
    /// Devolve <c>null</c> quando a linha está vazia, truncada ou sem
    /// timestamp legível. Um campo numérico que não dá para ler vira "vazio"
    /// (o sensor não existia), em vez de derrubar a linha inteira: um único
    /// campo ruim não pode apagar o histórico do PC inteiro.
    /// </para>
    /// </summary>
    public static AmostraSensor? LerAmostra(string linha)
    {
        if (string.IsNullOrWhiteSpace(linha)) return null;

        var partes = linha.Split(';');
        if (partes.Length < CamposNumericos + 1) return null;

        if (!DateTime.TryParseExact(partes[0].Trim(), "yyyy-MM-ddTHH:mm:ss.fff",
                                    CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal,
                                    out var momento))
            return null;

        return new AmostraSensor(
            momento,
            Valor(partes[1]), Valor(partes[2]), Valor(partes[3]), Valor(partes[4]),
            Valor(partes[5]), Valor(partes[6]), Valor(partes[7]), Valor(partes[8]),
            Valor(partes[9]), Valor(partes[10]), Valor(partes[11]));
    }

    /// <summary>
    /// Monta a linha CSV de uma troca de programa. Função pura.
    /// </summary>
    public static string MontarLinhaUso(TrocaUso troca)
    {
        string momento = troca.Momento.ToLocalTime()
            .ToString("yyyy-MM-ddTHH:mm:ss.fff", CultureInfo.InvariantCulture);

        return string.Join(";", momento, Higienizar(troca.Processo), Higienizar(troca.Titulo));
    }

    /// <summary>
    /// Lê uma linha do CSV de uso. Função pura. Devolve null quando a linha
    /// está vazia ou sem timestamp legível.
    /// </summary>
    public static TrocaUso? LerUso(string linha)
    {
        if (string.IsNullOrWhiteSpace(linha)) return null;

        var partes = linha.Split(';');

        // Três campos: timestamp, processo e título. Uma escrita interrompida
        // deixa a linha curta — e uma linha curta viraria um programa
        // fantasma no ranking ("chr", de "chrome" cortado pela metade).
        if (partes.Length < 3) return null;

        if (!DateTime.TryParseExact(partes[0].Trim(), "yyyy-MM-ddTHH:mm:ss.fff",
                                    CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal,
                                    out var momento))
            return null;

        string processo = partes[1].Trim();
        if (processo.Length == 0) return null;

        return new TrocaUso(momento, processo, partes[2]);
    }

    // ------------------------------------------------------------------ leitura dos arquivos

    /// <summary>
    /// Lê todas as amostras do log de sensores antigo. Devolve lista vazia
    /// quando o arquivo não existe (o app nunca rodou, ou já foi podado).
    /// </summary>
    public static List<AmostraSensor> LerAmostras()
    {
        var resultado = new List<AmostraSensor>();
        try
        {
            if (!File.Exists(ArquivoSensores)) return resultado;

            foreach (var linha in File.ReadLines(ArquivoSensores))
            {
                var amostra = LerAmostra(linha);
                if (amostra is not null) resultado.Add(amostra);
            }
        }
        catch
        {
            // Arquivo trancado ou ilegível: a migração segue sem ele (e o
            // app avisa que o histórico antigo não pôde ser importado).
        }
        return resultado;
    }

    /// <summary>
    /// Lê todas as trocas de programa dos arquivos de uso antigos (um por
    /// dia). Devolve lista vazia quando não há nenhum arquivo.
    /// </summary>
    public static List<TrocaUso> LerTrocas()
    {
        var resultado = new List<TrocaUso>();
        try
        {
            if (!Directory.Exists(PastaUso)) return resultado;

            foreach (string arquivo in Directory.EnumerateFiles(PastaUso, "uso-*.csv"))
            {
                foreach (var linha in File.ReadLines(arquivo))
                {
                    var troca = LerUso(linha);
                    if (troca is not null) resultado.Add(troca);
                }
            }
        }
        catch
        {
            // Pasta trancada ou ilegível: a migração segue sem ela.
        }
        return resultado;
    }

    // ------------------------------------------------------------------ auxiliares

    /// <summary>O timestamp na hora local, com milissegundos.</summary>
    private static string Momento(DateTime momento) =>
        momento.ToLocalTime().ToString("yyyy-MM-ddTHH:mm:ss.fff", CultureInfo.InvariantCulture);

    /// <summary>Um número em cultura invariante (ponto decimal), ou vazio.</summary>
    private static string Numero(float? valor) =>
        valor is float v ? v.ToString("0.##", CultureInfo.InvariantCulture) : "";

    /// <summary>Lê um campo numérico do arquivo; algo ilegível vira "vazio".</summary>
    private static float? Valor(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return null;
        if (!float.TryParse(texto.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var valor)) return null;
        return float.IsFinite(valor) ? valor : null;
    }

    /// <summary>
    /// Higieniza um texto para o CSV: separador e quebras de linha viram
    /// espaço, e o texto é cortado para não estourar a linha.
    /// </summary>
    private static string Higienizar(string? texto)
    {
        if (string.IsNullOrEmpty(texto)) return "";

        string limpo = texto.Replace(';', ' ').Replace('\r', ' ').Replace('\n', ' ').Trim();
        return limpo.Length > 200 ? limpo[..200] : limpo;
    }
}
