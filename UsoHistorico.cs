using System.Globalization;
using System.IO;
using System.Text;

namespace TranalSentinel;

/// <summary>
/// O histórico de uso: que programa esteve em frente, e quando.
///
/// <para>
/// É um CSV sem cabeçalho, um arquivo por dia, com separador ";" — como o
/// log de sensores. A linha é gravada só quando a janela em frente muda
/// (trocar de aba do navegador a cada segundo não interessa); a duração de
/// cada uso é a distância até a próxima linha.
/// </para>
///
/// <para>
/// O formato da linha é:
/// <code>
/// 2026-11-06T14:31:22.500;chrome;Painel - TRANAL SENTINEL
/// </code>
/// O timestamp é ISO 8601 com milissegundos (hora local). O título é
/// higienizado: ";" e quebras de linha viram espaço, para não quebrar o
/// CSV.
/// </para>
///
/// <para>
/// A escrita é silenciosa por desingênio (como o log diário): falhar no
/// registro de uso nunca pode derrubar o app.
/// </para>
/// </summary>
public static class UsoHistorico
{
    /// <summary>Pasta onde ficam os arquivos de uso.</summary>
    public static string Pasta => PastaOverride ?? Path.Combine(AppSettings.Folder, "uso");

    /// <summary>Quantos dias de histórico são mantidos. O mais antigo é apagado.</summary>
    public const int DiasRetidos = 30;

    /// <summary>Troca a pasta do histórico. Só os testes usam isto.</summary>
    internal static string? PastaOverride;

    private static readonly object Tranca = new();

    /// <summary>UTF-8 sem BOM: o BOM no começo da linha quebraria a leitura.</summary>
    private static readonly Encoding SemBom = new UTF8Encoding(false);

    /// <summary>O arquivo de um dia.</summary>
    public static string ArquivoDe(DateTime dia) =>
        Path.Combine(Pasta, $"uso-{dia:yyyy-MM-dd}.csv");

    /// <summary>
    /// Acrescenta uma troca ao histórico do dia. Nunca lança: o registro de
    /// uso é um extra, e perder uma linha é melhor que derrubar o app.
    /// </summary>
    public static void Gravar(TrocaUso troca)
    {
        try
        {
            lock (Tranca)
            {
                Directory.CreateDirectory(Pasta);
                File.AppendAllText(ArquivoDe(troca.Momento), MontarLinha(troca) + Environment.NewLine, SemBom);
            }
        }
        catch
        {
            // Arquivo trancado, sem permissão, disco cheio: o histórico de uso
            // é acessório e não pode derrubar o app.
        }

        Podar();
    }

    /// <summary>
    /// Monta a linha CSV. Função pura: não lê nem escreve nada.
    /// </summary>
    public static string MontarLinha(TrocaUso troca)
    {
        string momento = troca.Momento.ToLocalTime()
            .ToString("yyyy-MM-ddTHH:mm:ss.fff", CultureInfo.InvariantCulture);

        return string.Join(";", momento, Higienizar(troca.Processo), Higienizar(troca.Titulo));
    }

    /// <summary>
    /// Lê uma linha do CSV. Função pura. Devolve null quando a linha está
    /// vazia ou sem timestamp legível.
    /// </summary>
    public static TrocaUso? LerLinha(string linha)
    {
        if (string.IsNullOrWhiteSpace(linha)) return null;

        var partes = linha.Split(';');
        if (partes.Length < 2) return null;

        if (!DateTime.TryParseExact(partes[0].Trim(), "yyyy-MM-ddTHH:mm:ss.fff",
                                    CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal,
                                    out var momento))
            return null;

        string processo = partes[1].Trim();
        if (processo.Length == 0) return null;

        string titulo = partes.Length > 2 ? partes[2] : "";
        return new TrocaUso(momento, processo, titulo);
    }

    /// <summary>
    /// Lê as trocas de um dia. Devolve lista vazia quando o dia não existe
    /// (o app nunca rodou, ou já foi podado).
    /// </summary>
    public static List<TrocaUso> Ler(DateTime dia)
    {
        var resultado = new List<TrocaUso>();
        try
        {
            lock (Tranca)
            {
                string arquivo = ArquivoDe(dia);
                if (!File.Exists(arquivo)) return resultado;

                foreach (var linha in File.ReadLines(arquivo))
                {
                    var troca = LerLinha(linha);
                    if (troca is null) continue;
                    if (troca.Momento.Date != dia.Date) continue;
                    resultado.Add(troca);
                }
            }
        }
        catch
        {
            // Arquivo trancado por outra leitura: sem histórico, sem drama.
        }

        return resultado;
    }

    /// <summary>Lista os dias que têm histórico, do mais novo para o mais antigo.</summary>
    public static List<DateTime> DiasComUso()
    {
        var dias = new List<DateTime>();
        try
        {
            if (!Directory.Exists(Pasta)) return dias;
            foreach (string arquivo in Directory.EnumerateFiles(Pasta, "uso-*.csv"))
            {
                string nome = Path.GetFileNameWithoutExtension(arquivo);   // uso-2026-11-06
                if (nome.Length < 13) continue;
                string data = nome[4..];
                if (DateTime.TryParseExact(data, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                                           DateTimeStyles.None, out var dia))
                    dias.Add(dia);
            }
        }
        catch
        {
            // Pasta trancada ou inacessível: sem lista de dias.
        }

        dias.Sort((a, b) => b.CompareTo(a));
        return dias;
    }

    /// <summary>Apaga os dias mais antigos que <see cref="DiasRetidos"/>.</summary>
    public static void Podar()
    {
        try
        {
            lock (Tranca)
            {
                if (!Directory.Exists(Pasta)) return;
                var limite = DateTime.Now.Date.AddDays(-DiasRetidos);

                foreach (string arquivo in Directory.EnumerateFiles(Pasta, "uso-*.csv"))
                {
                    try
                    {
                        if (File.GetLastWriteTime(arquivo) < limite) File.Delete(arquivo);
                    }
                    catch
                    {
                        // Arquivo trancado: fica para a próxima vez.
                    }
                }
            }
        }
        catch
        {
            // Sem rotação, o arquivo só cresce até a próxima tentativa.
        }
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
