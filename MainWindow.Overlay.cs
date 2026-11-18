using System.Windows;
using System.Windows.Controls;
using LibreHardwareMonitor.Hardware;

namespace TranalSentinel;

/// <summary>Overlay: o que mostrar (escolhido na aba Overlay) e a montagem das linhas de sensores.</summary>
public partial class MainWindow
{
    private HashSet<string>? _ovShow;
    private HashSet<string> OvShow => _ovShow ??= _settings.OverlayItems is { } l ? l.ToHashSet() : OverlayMetrics.Defaults();

    /// <summary>
    /// O que cada item do seletor "O que mostrar" coloca no overlay.
    ///
    /// <para>
    /// A chave é a mesma de <see cref="OverlayMetric.Key"/>: o catálogo do
    /// overlay e a dica moram lado a lado, para não existirem duas listas
    /// de itens que podem sair de sincronia.
    /// </para>
    /// </summary>
    private static readonly Dictionary<string, string> DicasOverlay = new()
    {
        ["fps"] = "Quadros por segundo que o jogo está desenhando. É o número que diz se a jogada está fluida.",
        ["frametime"] = "Tempo gasto em cada quadro, em milissegundos. Passou de 16,7 ms, o jogo já não segura 60 FPS.",
        ["low1"] = "Média do 1 % pior dos quadros da sessão. Mostra os engasgos que o FPS médio esconde.",
        ["graph"] = "Desenho do tempo de quadro ao longo da sessão. Serve para ver em que momento o jogo engasgou.",
        ["gpu_load"] = "Porcentagem da placa em uso. Em 100 % o gargalo é a GPU, e não a CPU.",
        ["gpu_temp"] = "Temperatura do núcleo da placa. Perto de 85 °C ela já começa a baixar o clock sozinha.",
        ["gpu_hotspot"] = "Temperatura do ponto mais quente da placa. É ela que segura o clock antes do sensor do núcleo.",
        ["gpu_clock"] = "Frequência de trabalho do núcleo da placa, em MHz. Clock caindo com carga alta é throttling.",
        ["gpu_memclock"] = "Frequência de trabalho da memória da placa, em MHz. Mede a velocidade da VRAM.",
        ["gpu_power"] = "Watts que a placa está gastando. Serve para conferir se o limite de energia que você pôs está valendo.",
        ["gpu_volt"] = "Volts entregues ao núcleo da placa. Ajuda a ver o efeito do undervolt na prática.",
        ["gpu_fan"] = "Rotação da ventoinha da placa, em RPM. Quando a placa não expõe RPM, aparece o quanto ela está comandada.",
        ["gpu_perfcap"] = "Motivo que está limitando a placa agora: energia, temperatura ou voltagem. Só existe em placa NVIDIA e diz o que está segurando o desempenho.",
        ["vram_used"] = "Memória de vídeo ocupada, em GB. Quando ela acaba, o jogo passa a usar a memória do sistema e engasga.",
        ["vram_pct"] = "Porcentagem da VRAM em uso. Vale mais que o número em GB em placa com muita memória.",
        ["vram_temp"] = "Temperatura dos chips de memória da placa. Memória quente faz a placa perder clock na memória.",
        ["cpu_load"] = "Ocupação de todos os núcleos do processador, em porcentagem. Em 100 % o gargalo é a CPU.",
        ["cpu_maxcore"] = "O núcleo mais ocupado da CPU. Diz se o gargalo é de um núcleo só ou de todos.",
        ["cpu_temp"] = "Temperatura do processador. Acima de 90 °C ele reduz o clock para se proteger.",
        ["cpu_clock"] = "Frequência média dos núcleos, em GHz. Comparada com o pico, mostra quanto a CPU está baixando.",
        ["cpu_clockmax"] = "Maior frequência que algum núcleo alcançou, em GHz. É o clock que o boost consegue chegar.",
        ["cpu_power"] = "Watts que o processador está gastando. Só aparece quando o app roda como administrador.",
        ["ram_used"] = "Memória do sistema ocupada, em GB. Perto do total, o Windows passa a trocar por disco.",
        ["ram_pct"] = "Porcentagem da memória em uso. É o número que diz se falta memória para o que você está fazendo.",
        ["game"] = "Nome do jogo que está em primeiro plano. Serve para saber qual janela o app está medindo.",
        ["clock"] = "Hora do relógio no rodapé do overlay. Ajuda a marcar o começo da sessão.",
    };

    /// <summary>Cria as caixas de seleção da aba Overlay, agrupadas por componente.</summary>
    private void BuildOverlayPicker()
    {
        OvItemsPanel.Children.Clear();
        foreach (var group in OverlayMetrics.All.GroupBy(m => m.Group))
        {
            OvItemsPanel.Children.Add(new TextBlock
            {
                Text = group.Key.ToUpperInvariant(), Style = (Style)FindResource("Label"), Margin = new Thickness(0, 10, 0, 4),
            });
            var wrap = new WrapPanel();
            foreach (var m in group)
            {
                var cb = new CheckBox { Content = m.Label, Tag = m.Key, IsChecked = OvShow.Contains(m.Key), Margin = new Thickness(0, 0, 18, 6) };
                // A dica entra junto com a caixa: o seletor é reconstruído a
                // cada preset, então o ToolTip vai na criação da caixa para
                // não sumir na primeira reconstrução.
                ToolTipService.SetToolTip(cb, DicasOverlay.TryGetValue(m.Key, out var dica)
                    ? dica
                    : $"Coloca a linha {m.Label} no overlay, junto das outras escolhidas.");
                cb.Click += (_, _) =>
                {
                    if (cb.IsChecked == true) OvShow.Add(m.Key); else OvShow.Remove(m.Key);
                    SaveOverlayItems();
                };
                wrap.Children.Add(cb);
            }
            OvItemsPanel.Children.Add(wrap);
        }
    }

    private void SaveOverlayItems()
    {
        _settings.OverlayItems = OverlayMetrics.All.Select(m => m.Key).Where(OvShow.Contains).ToList();
        _settings.Save();
        UpdateOverlay();
    }

    private void OvPreset_Click(object sender, RoutedEventArgs e)
    {
        var keys = (string)((FrameworkElement)sender).Tag switch
        {
            "all" => OverlayMetrics.All.Select(m => m.Key).ToHashSet(),
            "min" => new HashSet<string> { "fps", "frametime", "low1" },
            _ => OverlayMetrics.Defaults(),
        };
        _ovShow = keys;
        SaveOverlayItems();
        BuildOverlayPicker();
    }

    // ------------------------------------------------------------------ leitura dos sensores

    private SensorRow? GpuRow(SensorType type, string name, bool startsWith = false) =>
        _gpus.Count == 0 ? null : _monitor.Rows.FirstOrDefault(r => r.Hardware == _gpus[0].Name && r.Type == type &&
                                                                   (startsWith ? r.Name.StartsWith(name) : r.Name == name) && r.RawValue != null);

    private uint? MainGpuClock(int nvmlType, string lhmName)
    {
        if (_gpus.Count > 0 && _gpus[0].NvmlHandle != IntPtr.Zero && Nvml.Clock(_gpus[0].NvmlHandle, nvmlType) is uint c) return c;
        return GpuRow(SensorType.Clock, lhmName)?.RawValue is float v && v > 0 ? (uint)v : null;
    }

    /// <summary>VRAM usada e total em GB: NVML quando disponível, senão LibreHardwareMonitor (MB).</summary>
    private (double Used, double Total)? MainGpuVram()
    {
        if (_gpus.Count == 0) return null;
        if (_gpus[0].NvmlHandle != IntPtr.Zero && Nvml.MemoryInfo(_gpus[0].NvmlHandle) is Nvml.Memory m && m.Total > 0)
            return (m.Used / 1073741824.0, m.Total / 1073741824.0);
        if (GpuRow(SensorType.SmallData, "GPU Memory Used")?.RawValue is float u && GpuRow(SensorType.SmallData, "GPU Memory Total")?.RawValue is float t && t > 0)
            return (u / 1024.0, t / 1024.0);
        return null;
    }

    private (float? Avg, float? Max) CpuClocks() => _monitor.CpuClocks();

    /// <summary>Monta as linhas GPU / VRAM / CPU / RAM só com o que foi escolhido; linha vazia não aparece.</summary>
    private List<OverlayLine> BuildOverlayLines(FrameStats? f)
    {
        var show = OvShow;
        var s = BottleneckAnalyzer.Sample(_monitor, _gpus.FirstOrDefault(), f);
        var lines = new List<OverlayLine>();
        string T(float? v) => SensorTexto.Temperatura(v);
        string P(double? v) => SensorTexto.Porcentual(v);
        void Line(string label, List<string> parts) { if (parts.Count > 0) lines.Add(new OverlayLine(label, string.Join("  ", parts))); }

        var gpu = new List<string>();
        if (show.Contains("gpu_load")) gpu.Add(P(s.GpuLoad));
        if (show.Contains("gpu_temp")) gpu.Add(T(s.GpuTemp));
        if (show.Contains("gpu_hotspot")) gpu.Add("HS " + T(GpuRow(SensorType.Temperature, "GPU Hot Spot")?.RawValue));
        if (show.Contains("gpu_clock")) gpu.Add(MainGpuClock(Nvml.ClockGraphics, "GPU Core") is uint c ? SensorTexto.Mhz(c) : SensorTexto.Ausente);
        if (show.Contains("gpu_memclock")) gpu.Add(MainGpuClock(Nvml.ClockMem, "GPU Memory") is uint mc ? $"mem {SensorTexto.Mhz(mc)}" : "mem —");
        if (show.Contains("gpu_power")) gpu.Add(MainGpuPower() is float w ? SensorTexto.Watts(w) : SensorTexto.Ausente);
        if (show.Contains("gpu_volt")) gpu.Add(GpuRow(SensorType.Voltage, "GPU Core")?.RawValue is float v ? SensorTexto.Volts(v) : SensorTexto.Ausente);
        if (show.Contains("gpu_fan"))
            gpu.Add(GpuRow(SensorType.Fan, "GPU Fan", true)?.RawValue is float rpm && rpm > 0 ? SensorTexto.Rpm(rpm)
                  : GpuRow(SensorType.Control, "GPU Fan", true)?.RawValue is float pct ? $"fan {SensorTexto.Porcentual(pct)}" : "fan —");
        if (show.Contains("gpu_perfcap") && MainGpuPerfCap() is { Length: > 0 } cap) gpu.Add(cap);
        Line("GPU", gpu);

        var vram = new List<string>();
        var vr = MainGpuVram();
        if (show.Contains("vram_used")) vram.Add(vr is { } a ? SensorTexto.Gigabytes(a.Used) : SensorTexto.Ausente);
        if (show.Contains("vram_pct")) vram.Add(P(s.VramPct));
        if (show.Contains("vram_temp")) vram.Add(T(GpuRow(SensorType.Temperature, "GPU Memory")?.RawValue));
        Line("VRAM", vram);

        var cpu = new List<string>();
        var (avg, max) = CpuClocks();
        if (show.Contains("cpu_load")) cpu.Add(P(s.CpuTotal));
        if (show.Contains("cpu_maxcore")) cpu.Add(s.CpuMaxCore is float mx ? $"núcleo {SensorTexto.Porcentual(mx)}" : "núcleo —");
        if (show.Contains("cpu_temp")) cpu.Add(T(s.CpuTemp));
        if (show.Contains("cpu_clock")) cpu.Add(avg is float ca ? SensorTexto.Gigahertz(ca / 1000) : SensorTexto.Ausente);
        if (show.Contains("cpu_clockmax")) cpu.Add(max is float cm ? $"pico {SensorTexto.Gigahertz(cm / 1000)}" : "pico —");
        if (show.Contains("cpu_power")) cpu.Add(_monitor.CpuPower is float cw && cw > 0 ? SensorTexto.Watts(cw) : SensorTexto.Ausente); // 0 = sem leitura (precisa de administrador)
        Line("CPU", cpu);

        var ram = new List<string>();
        var (total, availB) = Optimizer.MemoryStatus();
        if (show.Contains("ram_used")) ram.Add(total > 0 ? SensorTexto.Gigabytes((total - availB) / 1073741824.0) : SensorTexto.Ausente);
        if (show.Contains("ram_pct")) ram.Add(P(s.RamPct));
        Line("RAM", ram);
        return lines;
    }
}
