namespace TranalSentinel;

/// <summary>
/// A leitura dos argumentos da linha de comando, em um lugar só: o
/// <see cref="App"/> e o <see cref="Programa"/> (que sobe o coletor de
/// sensores antes do WPF) leem os argumentos do mesmo jeito.
/// </summary>
internal static class LinhaDeComando
{
    /// <summary>Diz se a flag está presente (sem diferenciar maiúsculas).</summary>
    public static bool Tem(string[] args, string nome)
        => Array.Exists(args, a => string.Equals(a, nome, StringComparison.OrdinalIgnoreCase));

    /// <summary>Lê o texto de um argumento do tipo "--nome valor"; sem ele, texto vazio.</summary>
    public static string Texto(string[] args, string nome)
    {
        int i = Array.FindIndex(args, a => string.Equals(a, nome, StringComparison.OrdinalIgnoreCase));
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : "";
    }

    /// <summary>Lê o valor de um argumento do tipo "--nome valor"; sem ele (ou inválido), o padrão.</summary>
    public static int Inteiro(string[] args, string nome, int padrao)
    {
        int i = Array.FindIndex(args, a => string.Equals(a, nome, StringComparison.OrdinalIgnoreCase));
        if (i < 0 || i + 1 >= args.Length) return padrao;
        return int.TryParse(args[i + 1], out int n) ? n : padrao;
    }
}
