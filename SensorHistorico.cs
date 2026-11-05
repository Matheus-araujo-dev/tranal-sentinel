using System.Globalization;
using System.IO;
using System.Text;

namespace TranalSentinel;

/// <summary>
/// Uma amostra do estado dos sensores, como fica no histórico contínuo.
///
/// <para>
/// Tudo é anulável de propósito: um sensor que não existe naquele PC (ou que
/// o driver não deixou ler) fica vazio, em vez de virar zero — zero é um
/// valor válido e não pode servir de "não li".
/// </para>
/// </summary>
/// <param name="Momento">Quando a amostra foi tirada (hora local).</param>
/// <param name="CpuTemp">Temperatura da CPU, em °C.</param>
/// <param name="CpuCarga">Carga total da CPU, em %.</param>
/// <param name="CpuConsumo">Consumo do pacote da CPU, em W.</param>
/// <param name="CpuClock">Clock médio dos núcleos da CPU, em MHz.</param>
/// <param name="GpuTemp">Temperatura da GPU, em °C.</param>
/// <param name="GpuCarga">Carga da GPU, em %.</param>
/// <param name="GpuClock">Clock do núcleo da GPU, em MHz.</param>
/// <param name="GpuVram">Memória de vídeo em uso, em MB.</param>
/// <param name="RamUso">Memória RAM em uso, em %.</param>
/// <param name="DiscoLeitura">Taxa de leitura do disco, em MB/s.</param>
/// <param name="DiscoEscrita">Taxa de escrita do disco, em MB/s.</param>
public sealed record AmostraSensor(
    DateTime Momento,
    float? CpuTemp, float? CpuCarga, float? CpuConsumo, float? CpuClock,
    float? GpuTemp, float? GpuCarga, float? GpuClock, float? GpuVram,
    float? RamUso, float? DiscoLeitura, float? DiscoEscrita);

/// <summary>
/// O log contínuo de sensores: o que o PC estava fazendo antes de travar.
///
/// <para>
/// É este arquivo que responde "por que o PC congelou": o TRANAL SAÚDE conta
/// os travamentos (Kernel-Power 41), mas sozinho ele não diz nada sobre o que
/// os sensores faziam na hora. Com o log, a retrospectively mostra a
/// temperatura da CPU, a carga da GPU e o clock nos minutos que antecederam o
/// congelamento.
/// </para>
///
/// <para>
/// O formato é um CSV sem cabeçalho, com separador ";" — assim o append é
/// simples (uma linha por amostra) e o arquivo pode ser aberto em qualquer
/// editor. Cada linha tem o timestamp ISO 8601 com milissegundos
/// (<c>yyyy-MM-ddTHH:mm:ss.fff</c>, hora local) e depois os campos numéricos,
/// sempre em cultura invariante ("0.##", ponto decimal), vazios quando o
/// sensor não existia:
///
/// <code>
/// 2026-11-05T14:31:22.500;78.5;43;92.4;3200;71;100;2010;2048;87.4;12.5;0.3
/// </code>
///
/// A ordem dos campos é: cpu_temp, cpu_carga, cpu_consumo, cpu_clock,
/// gpu_temp, gpu_carga, gpu_clock, gpu_vram, ram_uso, disco_leitura,
/// disco_escrita.
/// </para>
///
/// <para>
/// A cadência é fixa (5 s) e não depende do perfil de recursos: o log não
/// precisa da resolução de 1 s que a tela usa, e 5 s basta para dizer o que a
/// máquina estava fazendo antes de travar.
/// </para>
/// </summary>
public static class SensorHistorico
{
    /// <summary>De quanto em quanto tempo uma amostra é gravada.</summary>
    public static TimeSpan IntervaloGravacao => TimeSpan.FromSeconds(5);

    /// <summary>
    /// A pasta do arquivo. Por padrão fica em
    /// <c>%LOCALAPPDATA%\TranalSentinel\sensores</c>.
    /// </summary>
    public static string Pasta => PastaOverride ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TranalSentinel", "sensores");

    /// <summary>O arquivo do histórico.</summary>
    public static string CaminhoArquivo => Path.Combine(Pasta, "historico.csv");

    /// <summary>
    /// Tamanho máximo do arquivo. Passando disso, <see cref="Podar"/> fica só
    /// com a metade mais nova — o que interessa é o que aconteceu antes de
    /// travar, não o que aconteceu há semanas.
    /// </summary>
    public const long LimiteBytes = 8 * 1024 * 1024;

    /// <summary>
    /// Troca a pasta do log. Só os testes usam isto: eles não podem escrever
    /// no <c>%LOCALAPPDATA%</c> do usuário. Vazio = a pasta de verdade.
    /// </summary>
    internal static string? PastaOverride;

    /// <summary>Quantos campos numéricos a linha tem (fora o timestamp).</summary>
    private const int CamposNumericos = 11;

    private static readonly object Tranca = new();

    /// <summary>UTF-8 sem BOM: o BOM no começo da linha quebraria a leitura.</summary>
    private static readonly Encoding SemBom = new UTF8Encoding(false);

    /// <summary>
    /// Monta a linha CSV de uma amostra. Função pura: não lê nem escreve nada.
    /// </summary>
    public static string MontarLinha(AmostraSensor amostra)
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
    /// Lê uma linha do CSV e devolve a amostra. Função pura.
    ///
    /// <para>
    /// Devolve <c>null</c> quando a linha está vazia, truncada ou sem
    /// timestamp legível. Um campo numérico que não dá para ler vira "vazio"
    /// (o sensor não existia), em vez de derrubar a linha inteira: um único
    /// campo ruim não pode apagar o histórico do PC inteiro.
    /// </para>
    /// </summary>
    public static AmostraSensor? LerLinha(string linha)
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
    /// Acrescenta a amostra ao arquivo (cria o arquivo na primeira vez).
    ///
    /// <para>
    /// Nunca lança: o log de sensores é um extra. Se o arquivo estiver
    /// bloqueado por outro processo, ou a pasta não puder ser criada, o app
    /// continua funcionando — só perde o histórico.
    /// </para>
    /// </summary>
    public static void Gravar(AmostraSensor amostra)
    {
        try
        {
            lock (Tranca)
            {
                Directory.CreateDirectory(Pasta);
                if (!File.Exists(CaminhoArquivo)) File.WriteAllText(CaminhoArquivo, "", SemBom);
                File.AppendAllText(CaminhoArquivo, MontarLinha(amostra) + Environment.NewLine, SemBom);
            }
        }
        catch
        {
            // Arquivo bloqueado, sem permissão, disco cheio: o log é um extra
            // e não pode derrubar o app.
        }

        Podar();
    }

    /// <summary>
    /// Lê as amostras gravadas a partir de <paramref name="desde"/>.
    /// Não lança: um arquivo ilegível devolve lista vazia.
    /// </summary>
    public static List<AmostraSensor> Ler(DateTime desde)
    {
        var resultado = new List<AmostraSensor>();
        try
        {
            lock (Tranca)
            {
                if (!File.Exists(CaminhoArquivo)) return resultado;

                foreach (var linha in File.ReadLines(CaminhoArquivo))
                {
                    var amostra = LerLinha(linha);
                    if (amostra is null) continue;
                    if (amostra.Momento < desde) continue;
                    resultado.Add(amostra);
                }
            }
        }
        catch
        {
            // Arquivo trancado por outra leitura: sem histórico, sem drama.
        }
        return resultado;
    }

    /// <summary>
    /// Rotação por tamanho: quando o arquivo passa do limite, fica só a metade
    /// mais nova (as linhas recentes). Abaixo do limite, não faz nada.
    /// </summary>
    public static void Podar()
    {
        try
        {
            lock (Tranca)
            {
                var info = new FileInfo(CaminhoArquivo);
                if (!info.Exists || info.Length <= LimiteBytes) return;

                var linhas = File.ReadAllLines(CaminhoArquivo);
                int manter = (linhas.Length + 1) / 2;
                var novas = linhas.Skip(linhas.Length - manter).ToArray();
                File.WriteAllLines(CaminhoArquivo, novas, SemBom);
            }
        }
        catch
        {
            // Sem rotação, o arquivo só cresce até a próxima tentativa.
        }
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
}
