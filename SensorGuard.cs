namespace ResetExplode;

/// <summary>Um crash do próprio app registrado pelo Windows no log de eventos.</summary>
public sealed record CrashDoApp(DateTime Momento, string CodigoExcecao, string Aplicativo, string Modulo);

/// <summary>Quanto dos sensores o app pode usar com segurança.</summary>
public enum NivelSensor
{
    /// <summary>Tudo ligado: CPU, memória, discos, rede e GPU.</summary>
    Completo,

    /// <summary>GPU fora da coleta do LibreHardwareMonitor (é o caminho nativo que derruba o app).</summary>
    SemSensoresGpu,

    /// <summary>Nenhum sensor do LibreHardwareMonitor — o app continua funcionando, só sem leituras.</summary>
    SemSensores
}

/// <summary>
/// Decide o nível de segurança dos sensores.
///
/// <para>
/// O LibreHardwareMonitor lê a GPU da AMD pela ADL (código nativo do driver).
/// Quando essa biblioteca devolve ponteiro inválido, o .NET leva um access
/// violation dentro de <c>Marshal.PtrToStructure</c> — e access violation
/// <b>não dá para pegar em C#</b>: o processo morre na hora. A única defesa
/// possível é não chamar o caminho quebrado.
/// </para>
///
/// <para>
/// A evidência vem do log de eventos: o Windows registra a morte do app em
/// "Application Error" (evento 1000), com o nome do aplicativo e o código de
/// exceção. Access violation (0xc0000005) e falha do motor de execução
/// (0x80131623) contam como falha nativa — o tipo que a linguagem não
/// consegue recuperar.
/// </para>
/// </summary>
public static class SensorGuard
{
    /// <summary>
    /// Quantos crashes nativos derrubam todos os sensores (e não só os da GPU).
    /// Um crash já desliga a GPU — que é o caminho nativo perigoso. Só depois
    /// de vários crashes seguidos é que o resto também sai, porque aí o problema
    /// não é só o driver da AMD.
    /// </summary>
    public const int CrashesParaDesligarTudo = 3;

    /// <summary>Janela padrão de análise quando o usuário não reabilitou nada.</summary>
    public static readonly TimeSpan JanelaPadrao = TimeSpan.FromHours(24);

    /// <summary>
    /// Normaliza o código de exceção para a forma "0x" + 8 dígitos hex.
    /// O log do Windows grava o hex <b>sem</b> o prefixo ("c0000005"), mas
    /// algumas fontes trazem "0xc0000005" ou o valor em decimal.
    /// </summary>
    public static string NormalizarCodigo(string? codigo)
    {
        if (string.IsNullOrWhiteSpace(codigo)) return "";
        string c = codigo.Trim();

        if (c.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return FormatarHex(c[2..]);

        // O Windows grava o hexadecimal puro ("c0000005", "80131623").
        if (c.Length <= 8 && c.All(Uri.IsHexDigit))
            return FormatarHex(c);

        // Decimal grande: é o mesmo código escrito de outra forma.
        if (long.TryParse(c, out long dec))
            return "0x" + dec.ToString("x8");

        return c.ToLowerInvariant();
    }

    private static string FormatarHex(string hex)
    {
        string limpo = hex.TrimStart('0');
        if (limpo.Length == 0) limpo = "0";
        return "0x" + limpo.ToLowerInvariant().PadLeft(8, '0');
    }

    /// <summary>É uma falha nativa (que a linguagem não consegue recuperar)?</summary>
    public static bool EhFalhaNativa(string? codigo)
    {
        string n = NormalizarCodigo(codigo);
        return n is "0xc0000005" or "0x80131623";
    }

    /// <summary>
    /// Conta os crashes nativos do app dentro da janela considerada.
    /// A janela começa no reabilitamento (quando o usuário reativa à mão) ou,
    /// sem reabilitamento, <see cref="JanelaPadrao"/> atrás de <paramref name="agora"/>.
    /// </summary>
    public static int ContarCrashes(IReadOnlyList<CrashDoApp> crashes, DateTime agora, DateTime? reabilitadoEm = null)
    {
        DateTime inicio = reabilitadoEm ?? agora - JanelaPadrao;
        int n = 0;
        foreach (var c in crashes)
        {
            if (c.Momento < inicio || c.Momento > agora) continue;
            if (!EhFalhaNativa(c.CodigoExcecao)) continue;
            n++;
        }
        return n;
    }

    /// <summary>
    /// Nível de segurança a aplicar. O pior caso vence: se o usuário já tinha
    /// desligado a GPU e ainda houve crash, desliga tudo.
    /// </summary>
    public static NivelSensor Nivel(
        IReadOnlyList<CrashDoApp> crashes,
        DateTime agora,
        DateTime? reabilitadoEm = null,
        bool gpuDesligadaNasPreferencias = false)
    {
        var nivel = gpuDesligadaNasPreferencias ? NivelSensor.SemSensoresGpu : NivelSensor.Completo;
        int n = ContarCrashes(crashes, agora, reabilitadoEm);
        if (n >= CrashesParaDesligarTudo) return NivelSensor.SemSensores;
        if (n >= 1 && nivel == NivelSensor.Completo) return NivelSensor.SemSensoresGpu;
        return nivel;
    }

    /// <summary>O LibreHardwareMonitor pode ser usado?</summary>
    public static bool UsarLhm(NivelSensor nivel) => nivel != NivelSensor.SemSensores;

    /// <summary>A GPU entra na coleta do LibreHardwareMonitor (caminho nativo da ADL)?</summary>
    public static bool UsarGpuAdl(NivelSensor nivel) => nivel == NivelSensor.Completo;

    /// <summary>
    /// Depois que o processo de coleta morre, a GPU deve ficar de fora?
    /// Sim: ela é o caminho nativo que derruba as coisas, e o resto dos sensores
    /// continua funcionando sem ela.
    /// </summary>
    public static bool GpuDepoisDaMorte(int mortes) => mortes >= 1;

    /// <summary>Texto para o usuário entender o que o app fez e por quê.</summary>
    public static string Mensagem(NivelSensor nivel, int crashes)
    {
        return nivel switch
        {
            NivelSensor.SemSensores =>
                $"O app travou {crashes}× por falha nativa nos sensores. Os sensores foram desligados " +
                "para o programa não morrer de novo — o resto do RESET EXPLODE continua normal.",
            NivelSensor.SemSensoresGpu when crashes > 0 =>
                $"O app travou {crashes}× por falha nativa nos sensores da GPU (driver da AMD). " +
                "Os sensores da GPU foram desligados; CPU, memória e discos continuam funcionando.",
            NivelSensor.SemSensoresGpu =>
                "Os sensores da GPU estão desligados nas configurações.",
            _ => ""
        };
    }
}
