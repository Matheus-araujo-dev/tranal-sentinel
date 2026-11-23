namespace TranalSentinel;

/// <summary>
/// Um atalho global: os modificadores (as constantes de <see cref="AtalhoRegras"/>,
/// os mesmos bits do <c>RegisterHotKey</c>) e a tecla (código virtual do Windows).
/// </summary>
/// <param name="Modificadores">Ctrl, Alt, Shift e Win somados.</param>
/// <param name="Tecla">O código virtual da tecla (0x41 = A, 0x70 = F1...).</param>
public sealed record Atalho(uint Modificadores, uint Tecla);

/// <summary>
/// A regra do atalho do monitor: qual vale, se uma combinação serve e como
/// ela é escrita na tela. Não conhece WPF nem o Windows (é testável).
///
/// <para>
/// O atalho é escolha do dono (20/11/2026): o Ctrl + Shift + O, o padrão,
/// é o mesmo que o AMD Software usa para o painel de métricas dele (medido no
/// PC do dono: o Windows recusou o registro com o erro 1409 porque o
/// RadeonSoftware já tinha o atalho). Quem abre primeiro fica com ele.
/// </para>
/// </summary>
public static class AtalhoRegras
{
    /// <summary>Modificador Alt (MOD_ALT).</summary>
    public const uint Alt = 0x1;
    /// <summary>Modificador Ctrl (MOD_CONTROL).</summary>
    public const uint Ctrl = 0x2;
    /// <summary>Modificador Shift (MOD_SHIFT).</summary>
    public const uint Shift = 0x4;
    /// <summary>Modificador Windows (MOD_WIN).</summary>
    public const uint Win = 0x8;
    /// <summary>Sem repetição ao segurar (MOD_NOREPEAT): é somado na hora de registrar, não guardado.</summary>
    public const uint SemRepeticao = 0x4000;

    /// <summary>O atalho de quem nunca escolheu: Ctrl + Shift + O.</summary>
    public static readonly Atalho Padrao = new(Ctrl | Shift, 0x4F);

    /// <summary>
    /// O atalho que vale a partir do que está gravado. Nada gravado, ou uma
    /// combinação que não serve (arquivo editado à mão), vira o padrão.
    /// </summary>
    public static Atalho Carregar(uint? modificadores, uint? tecla)
    {
        if (modificadores is null || tecla is null) return Padrao;
        var a = new Atalho(modificadores.Value, tecla.Value);
        return Erro(a) is null ? a : Padrao;
    }

    /// <summary>
    /// Por que a combinação não serve (null quando serve).
    ///
    /// <para>
    /// Precisa de Ctrl, Alt ou Win: um atalho global de Shift + letra (ou de
    /// uma letra só) seria disparado ao digitar, e o monitor ligaria no meio
    /// de um texto. A tecla tem que ser letra, número ou F1 a F24, que são as
    /// que o app sabe escrever na tela.
    /// </para>
    /// </summary>
    public static string? Erro(Atalho a)
    {
        if ((a.Modificadores & ~(Alt | Ctrl | Shift | Win)) != 0)
            return "Modificador desconhecido.";
        if ((a.Modificadores & (Ctrl | Alt | Win)) == 0)
            return "Use Ctrl, Alt ou Win junto com a tecla: um atalho só de Shift (ou de uma tecla só) dispararia enquanto você digita.";
        if (NomeDaTecla(a.Tecla) is null)
            return "Use uma letra, um número ou uma tecla de F1 a F24.";
        return null;
    }

    /// <summary>O atalho como a tela o escreve ("Ctrl + Shift + O").</summary>
    public static string Texto(Atalho a)
    {
        var partes = new List<string>();
        if ((a.Modificadores & Ctrl) != 0) partes.Add("Ctrl");
        if ((a.Modificadores & Alt) != 0) partes.Add("Alt");
        if ((a.Modificadores & Shift) != 0) partes.Add("Shift");
        if ((a.Modificadores & Win) != 0) partes.Add("Win");
        partes.Add(NomeDaTecla(a.Tecla) ?? $"tecla {a.Tecla:X2}");
        return string.Join(" + ", partes);
    }

    /// <summary>O nome da tecla (letra, número ou F1–F24); null para as outras.</summary>
    public static string? NomeDaTecla(uint vk) => vk switch
    {
        >= 0x30 and <= 0x39 => ((char)vk).ToString(),         // 0–9
        >= 0x41 and <= 0x5A => ((char)vk).ToString(),         // A–Z
        >= 0x70 and <= 0x87 => "F" + (vk - 0x70 + 1),         // F1–F24
        _ => null,
    };
}
