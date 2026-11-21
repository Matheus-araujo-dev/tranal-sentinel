namespace TranalSentinel;

/// <summary>Item que pode aparecer no overlay. Group: "FPS", "GPU", "VRAM", "CPU", "RAM" ou "Outros".</summary>
public sealed record OverlayMetric(string Key, string Group, string Label, bool Default);

/// <summary>Linha do overlay já pronta para mostrar (ex.: "GPU" · "98%  65 °C  2790 MHz").</summary>
public sealed record OverlayLine(string Label, string Value);

/// <summary>Catálogo do que o overlay pode mostrar, na ordem em que aparece.</summary>
public static class OverlayMetrics
{
    public static readonly OverlayMetric[] All =
    {
        new("fps", "FPS", "FPS", true),
        new("frametime", "FPS", "Frametime (ms)", true),
        new("low1", "FPS", "1% low", true),
        new("graph", "FPS", "Gráfico de frametime", true),

        new("gpu_load", "GPU", "Uso", true),
        new("gpu_temp", "GPU", "Temperatura", true),
        new("gpu_hotspot", "GPU", "Hotspot", false),
        new("gpu_clock", "GPU", "Clock do núcleo", true),
        new("gpu_memclock", "GPU", "Clock da memória", false),
        new("gpu_power", "GPU", "Consumo", true),
        new("gpu_volt", "GPU", "Tensão do núcleo", false),
        new("gpu_fan", "GPU", "Ventoinha", false),
        new("gpu_perfcap", "GPU", "Limitador (NVIDIA)", false),

        new("vram_used", "VRAM", "Usada (GB)", true),
        new("vram_pct", "VRAM", "Uso (%)", false),
        new("vram_temp", "VRAM", "Temperatura", false),

        new("cpu_load", "CPU", "Uso total", true),
        new("cpu_maxcore", "CPU", "Núcleo mais ocupado", false),
        new("cpu_temp", "CPU", "Temperatura", true),
        new("cpu_clock", "CPU", "Clock médio", true),
        new("cpu_clockmax", "CPU", "Clock máximo (núcleo)", false),
        new("cpu_power", "CPU", "Consumo", true),

        new("ram_used", "RAM", "Usada (GB)", true),
        new("ram_pct", "RAM", "Uso (%)", false),

        new("fan", "Ventoinha", "Ventoinha do sistema", true),

        new("game", "Outros", "Nome do jogo", true),
        new("clock", "Outros", "Hora", false),
    };

    /// <summary>As métricas que vêm marcadas para quem nunca escolheu nada.</summary>
    public static HashSet<string> Defaults() => All.Where(m => m.Default).Select(m => m.Key).ToHashSet();

    /// <summary>O rótulo da linha de cada grupo de sensores, na ordem do monitor.</summary>
    private static readonly (string Grupo, string Rotulo)[] Linhas =
    {
        ("GPU", "GPU"), ("VRAM", "VRAM"), ("CPU", "CPU"), ("RAM", "RAM"), ("Ventoinha", "VENT"),
    };

    /// <summary>
    /// As linhas escolhidas, todas com "—": é o que o monitor mostra enquanto
    /// um benchmark roda.
    ///
    /// <para>
    /// Durante o bench as leituras ficam pausadas (cada consulta ao driver
    /// aparece como engasgo na medição): o número que estava na tela seria um
    /// número parado mostrado como se fosse de agora. Número velho é pior que
    /// número ausente.
    /// </para>
    /// </summary>
    public static IReadOnlyList<OverlayLine> LinhasSemLeitura(IReadOnlySet<string> mostrar) =>
        Linhas
            .Where(l => All.Any(m => m.Group == l.Grupo && mostrar.Contains(m.Key)))
            .Select(l => new OverlayLine(l.Rotulo, SensorTexto.Ausente))
            .ToList();

    /// <summary>
    /// Acrescenta a ventoinha às escolhas gravadas antes de ela existir.
    ///
    /// <para>
    /// Até a v0.37.1 o painel do desktop mostrava a ventoinha sempre; agora
    /// ela é uma métrica de "O que mostrar". Quem já tinha escolhas gravadas
    /// (sem "fan") perderia a linha na atualização, sem aviso. A migração vale
    /// uma vez só: a marca <paramref name="jaMigrado"/> impede que a
    /// ventoinha volte depois que o dono a desmarcar.
    /// </para>
    /// </summary>
    /// <returns>As escolhas (a mesma lista quando não há o que mudar) e se a migração mudou algo.</returns>
    public static (List<string>? Itens, bool Mudou) MigrarVentoinha(List<string>? itens, bool jaMigrado)
    {
        if (jaMigrado || itens is null || itens.Contains("fan")) return (itens, false);
        return (new List<string>(itens) { "fan" }, true);
    }
}
