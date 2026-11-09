using System.IO;
using System.Text;

namespace TranalSentinel;

/// <summary>
/// Log diário do app: um arquivo por dia, com o que o TRANAL SENTINEL fez.
/// </summary>
/// <remarks>
/// <para>
/// É o "diário do assistente": o que mudou, o que o app detectou, o que o
/// usuário aplicou. Fica em texto simples, um arquivo por dia, para o usuário
/// conseguir abrir com o Bloco de Notas e grep.
/// </para>
/// <para>
/// A escrita é silenciosa por desingênio: falhar no log nunca pode derrubar o
/// app (é um arquivo de apoio, não uma função crítica).
/// </para>
/// </remarks>
public static class RegistroDiario
{
    /// <summary>Pasta onde ficam os arquivos de log.</summary>
    public static string Pasta => PastaOverride ?? Path.Combine(AppSettings.Folder, "log");

    /// <summary>
    /// Redireciona a pasta do diário (só os testes usam isto: a suíte não
    /// escreve no diário do usuário).
    /// </summary>
    internal static string? PastaOverride;

    /// <summary>Arquivo do dia de hoje.</summary>
    public static string ArquivoDeHoje => Path.Combine(Pasta, $"tranal-{DateTime.Now:yyyy-MM-dd}.log");

    /// <summary>Quantos dias de log são mantidos. O mais antigo é apagado.</summary>
    public const int DiasMantidos = 30;

    /// <summary>
    /// Registra um evento no log do dia.
    /// </summary>
    /// <param name="mensagem">O que aconteceu (sem data/hora: ela é acrescentada aqui).</param>
    /// <param name="categoria">Rótulo curto que agrupa os eventos (ex.: "SENSORES", "ATUALIZACAO").</param>
    public static void Registrar(string mensagem, string categoria = "INFO")
    {
        try
        {
            Directory.CreateDirectory(Pasta);
            string linha = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{categoria}] {mensagem}";
            File.AppendAllText(ArquivoDeHoje, linha + Environment.NewLine, Encoding.UTF8);
            ApagarAntigos();
        }
        catch
        {
            // Log é acessório: nunca derruba o app por causa dele.
        }
    }

    /// <summary>Lê o log de um dia. Devolve string vazia se o dia não existir.</summary>
    public static string Ler(DateTime dia)
    {
        try
        {
            string arquivo = Path.Combine(Pasta, $"tranal-{dia:yyyy-MM-dd}.log");
            return File.Exists(arquivo) ? File.ReadAllText(arquivo) : "";
        }
        catch
        {
            return "";
        }
    }

    /// <summary>Lista os dias que têm log, do mais novo para o mais antigo.</summary>
    public static List<DateTime> DiasComLog()
    {
        var dias = new List<DateTime>();
        try
        {
            if (!Directory.Exists(Pasta)) return dias;
            foreach (string arquivo in Directory.EnumerateFiles(Pasta, "tranal-*.log"))
            {
                string nome = Path.GetFileNameWithoutExtension(arquivo);   // tranal-2026-11-05
                if (nome.Length < 16) continue;
                string data = nome[7..];
                if (DateTime.TryParseExact(data, "yyyy-MM-dd", null,
                        System.Globalization.DateTimeStyles.None, out var d))
                    dias.Add(d);
            }
        }
        catch { }
        dias.Sort((a, b) => b.CompareTo(a));
        return dias;
    }

    private static void ApagarAntigos()
    {
        try
        {
            if (!Directory.Exists(Pasta)) return;
            var limite = DateTime.Now.Date.AddDays(-DiasMantidos);
            foreach (string arquivo in Directory.EnumerateFiles(Pasta, "tranal-*.log"))
            {
                try
                {
                    if (File.GetLastWriteTime(arquivo) < limite) File.Delete(arquivo);
                }
                catch { }
            }
        }
        catch { }
    }
}
