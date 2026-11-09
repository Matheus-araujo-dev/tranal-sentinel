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

    /// <summary>
    /// A leitura de um arquivo do histórico antigo: as linhas lidas e se a
    /// leitura ficou completa.
    /// </summary>
    /// <typeparam name="T">O tipo da linha (amostra de sensor ou troca de uso).</typeparam>
    /// <param name="Linhas">As linhas lidas.</param>
    /// <param name="Completa">
    /// False quando a leitura parou no meio (o arquivo trancado, o disco
    /// cheio, o antivírus com o arquivo aberto). Uma leitura pela metade
    /// <b>não pode valer como importação completa</b>: o histórico que
    /// faltou nunca entraria no banco — e ninguém ficaria sabendo.
    /// </param>
    public sealed record Leitura<T>(List<T> Linhas, bool Completa);

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

        if (!DateTime.TryParseExact(partes[0].AsSpan().Trim(), "yyyy-MM-ddTHH:mm:ss.fff",
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
    /// <param name="desde">
    /// O instante mais antigo que entra (null = tudo). A migração passa o
    /// prazo da tabela: o app promete guardar 15 dias de amostras, e
    /// importar anos de histórico só encheria o banco com o que seria
    /// podado em seguida. O CSV antigo continua no disco, com tudo.
    /// </param>
    public static Leitura<AmostraSensor> LerAmostras(DateTime? desde = null)
    {
        var resultado = new List<AmostraSensor>();
        bool completa = true;

        try
        {
            if (!File.Exists(ArquivoSensores)) return new Leitura<AmostraSensor>(resultado, true);

            foreach (var linha in File.ReadLines(ArquivoSensores))
            {
                var amostra = LerAmostra(linha);
                if (amostra is null) continue;
                if (desde is not null && amostra.Momento < desde.Value) continue;

                resultado.Add(amostra);
            }
        }
        catch
        {
            // Arquivo trancado ou ilegível: a leitura parou no meio, e o
            // que faltou não entrou. A migração precisa saber disso — sem
            // o sinal, o histórico que faltou nunca entraria no banco, e
            // ninguém seria avisado.
            completa = false;
        }

        return new Leitura<AmostraSensor>(resultado, completa);
    }

    /// <summary>
    /// Lê todas as trocas de programa dos arquivos de uso antigos (um por
    /// dia). Devolve lista vazia quando não há nenhum arquivo.
    /// </summary>
    /// <param name="desde">
    /// O instante mais antigo que entra (null = tudo). A migração passa o
    /// prazo da tabela (30 dias de histórico de uso): os arquivos de anos
    /// antigos não são importados — e eles continuam no disco.
    /// </param>
    public static Leitura<TrocaUso> LerTrocas(DateTime? desde = null)
    {
        var resultado = new List<TrocaUso>();
        bool completa = true;

        try
        {
            if (!Directory.Exists(PastaUso)) return new Leitura<TrocaUso>(resultado, true);

            foreach (string arquivo in Directory.EnumerateFiles(PastaUso, "uso-*.csv"))
            {
                // O nome do arquivo é a data do dia (uso-AAAA-MM-DD.csv):
                // um arquivo de dia antigo é pulado inteiro, sem ler
                // linha por linha.
                DateTime? diaDoArquivo = DiaDoArquivo(arquivo);
                if (desde is not null && diaDoArquivo is not null && diaDoArquivo.Value < desde.Value.Date)
                    continue;

                try
                {
                    foreach (var linha in File.ReadLines(arquivo))
                    {
                        var troca = LerUso(linha);
                        if (troca is null) continue;
                        if (desde is not null && troca.Momento < desde.Value) continue;

                        resultado.Add(troca);
                    }
                }
                catch
                {
                    // Um arquivo trancado (o antivírus com ele aberto, o
                    // disco cheio) interrompe só ESTE arquivo: os outros
                    // continuam sendo lidos. Mas a leitura ficou pela
                    // metade — e a migração precisa saber disso, senão o
                    // que faltou nunca entraria no banco sem ninguém
                    // ficar sabendo.
                    completa = false;
                }
            }
        }
        catch
        {
            // Pasta trancada ou ilegível: a leitura parou no meio, e o que
            // ficou de fora nunca entraria no banco sem aviso.
            completa = false;
        }

        return new Leitura<TrocaUso>(resultado, completa);
    }

    /// <summary>
    /// A data do dia de um arquivo de uso (o nome é uso-AAAA-MM-DD.csv).
    /// Devolve null quando o nome não segue o padrão — e o arquivo é lido
    /// inteiro, para não perder histórico por causa do nome do arquivo.
    /// </summary>
    private static DateTime? DiaDoArquivo(string caminho)
    {
        string nome = Path.GetFileNameWithoutExtension(caminho);
        if (!nome.StartsWith("uso-", StringComparison.Ordinal)) return null;

        string data = nome[4..];
        if (!DateTime.TryParseExact(data, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var dia))
            return null;

        return dia;
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

        // O Span evita a cópia que a sobrecarga de string faz por baixo
        // dos panos: a migração do CSV grande lê milhões de campos, e a
        // diferença aparece na hora de abrir o app.
        ReadOnlySpan<char> limpo = texto.AsSpan().Trim();
        if (!float.TryParse(limpo, NumberStyles.Float, CultureInfo.InvariantCulture, out var valor)) return null;
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
