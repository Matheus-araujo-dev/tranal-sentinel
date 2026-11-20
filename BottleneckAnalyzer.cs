using LibreHardwareMonitor.Hardware;

namespace TranalSentinel;

public enum Limiter { Gpu, Cpu, Vram, Ram, Thermal, Capped }

/// <summary>Uma leitura (1/s) de tudo que pode limitar o FPS.</summary>
public sealed record LoadSample(
    float? GpuLoad, float? CpuTotal, float? CpuMaxCore, string? BusiestCore,
    double? VramPct, float? RamPct, float? GpuTemp, float? CpuTemp, bool GpuThermalCap, double? Fps)
{
    /// <summary>
    /// Regras (na ordem): VRAM cheia → térmico → GPU ≥ 90% (normal em jogo pesado) → CPU/núcleo ≥ 85% com GPU sobrando →
    /// RAM cheia → nada no limite (trava de FPS, V-Sync ou menu).
    /// </summary>
    public Limiter Classify()
    {
        if (VramPct >= 97) return Limiter.Vram;
        if (GpuThermalCap || CpuTemp >= 95) return Limiter.Thermal;
        if (GpuLoad >= 90) return Limiter.Gpu;
        if (CpuMaxCore >= 85 || CpuTotal >= 85) return Limiter.Cpu;
        if (RamPct >= 92) return Limiter.Ram;
        return Limiter.Capped;
    }
}

public static class BottleneckAnalyzer
{
    /// <summary>Lê os sensores atuais (LibreHardwareMonitor + NVML) da GPU principal e da CPU.</summary>
    public static LoadSample Sample(SensorMonitor m, GpuInfo? gpu, FrameStats? frames)
    {
        var rows = m.Rows;
        float? gpuLoad = null, gpuTemp = null, gpuTempFallback = null;
        float? memUsed = null, memTotal = null;
        double? vramPct = null;
        bool thermal = false;

        // Núcleos da CPU e RAM: uma varredura só, sem alocação de lista.
        SensorRow? busiest = null;
        float? ram = null;

        foreach (var r in rows)
        {
            if (gpu != null && r.Hardware == gpu.Name)
            {
                if (r.Type == SensorType.Load && r.Name == "GPU Core") gpuLoad = r.RawValue;
                if (r.Type == SensorType.Temperature)
                {
                    if (r.Name == "GPU Core") gpuTemp = r.RawValue;
                    else gpuTempFallback ??= r.RawValue;
                }
                if (r.Name == "GPU Memory Used") memUsed = r.RawValue;
                if (r.Name == "GPU Memory Total") memTotal = r.RawValue;
            }

            if (r.HardwareType == HardwareType.Cpu && r.Type == SensorType.Load &&
                r.Name.StartsWith("CPU Core #") && !r.Name.Contains("Thread") && r.RawValue != null)
            {
                if (busiest == null || r.RawValue > busiest.RawValue) busiest = r;
            }

            if (r.HardwareType == HardwareType.Memory && r.Type == SensorType.Load && r.Name == "Memory")
                ram = r.RawValue;
        }

        gpuTemp ??= gpuTempFallback;

        if (gpu != null)
        {
            if (gpu.NvmlHandle != IntPtr.Zero && Nvml.MemoryInfo(gpu.NvmlHandle) is Nvml.Memory mem && mem.Total > 0)
                vramPct = 100.0 * mem.Used / mem.Total;
            else if (memUsed != null && memTotal > 0)
                vramPct = 100.0 * memUsed.Value / memTotal.Value;

            if (gpu.NvmlHandle != IntPtr.Zero && Nvml.ThrottleReasons(gpu.NvmlHandle) is ulong tr)
                thermal = (tr & 0x60) != 0; // SW/HW thermal slowdown
        }

        return new LoadSample(gpuLoad, m.CpuLoad, busiest?.RawValue, busiest?.Name.Replace("CPU Core #", "núcleo "),
                              vramPct, ram, gpuTemp, m.CpuTemperature, thermal, frames?.Fps);
    }

    public static string Title(Limiter l) => l switch
    {
        Limiter.Gpu => "GPU no limite",
        Limiter.Cpu => "Gargalo de CPU",
        Limiter.Vram => "Memória de vídeo cheia",
        Limiter.Ram => "Memória RAM cheia",
        Limiter.Thermal => "Limitado por temperatura",
        _ => "Nada no limite"
    };

    public static string Advice(Limiter l) => l switch
    {
        Limiter.Gpu => "Normal em jogos pesados: a placa de vídeo está sendo bem aproveitada. Para mais FPS, reduza " +
                       "resolução ou qualidade gráfica, ou ative DLSS/FSR.",
        Limiter.Cpu => "A CPU (ou um núcleo dela) não acompanha a GPU. Subir resolução/gráficos quase não custa FPS. " +
                       "Para ganhar FPS: reduza opções que pesam na CPU (distância de visão, multidões, física), feche " +
                       "programas em segundo plano e confira se o XMP/EXPO da RAM está ativo na BIOS.",
        Limiter.Vram => "A memória da placa de vídeo encheu, o que causa travadas. Reduza a qualidade das texturas " +
                        "ou a resolução.",
        Limiter.Ram => "Pouca RAM livre: o Windows passa a usar o disco e o jogo engasga. Feche navegadores e outros programas.",
        Limiter.Thermal => "Temperatura alta está derrubando o clock. Verifique ventilação do gabinete, poeira e pasta térmica.",
        _ => "Nem CPU nem GPU estão no limite: provavelmente há trava de FPS, V-Sync/G-Sync limitando, ou o jogo " +
             "está em menu/pausa. Normal se o FPS já é o desejado."
    };

    /// <summary>Equilíbrio teórico pelo desempenho relativo (GPU: RTX 4070 = 100; CPU: Ryzen 7 7700X = 100).</summary>
    public static (double Ratio, string Text) Balance(double gpuIdx, double cpuIdx)
    {
        double ratio = gpuIdx / cpuIdx;
        string text = ratio switch
        {
            > 1.8 => "GPU muito mais forte que a CPU: em boa parte dos jogos a CPU vai limitar o FPS, principalmente em 1080p.",
            > 1.3 => "GPU um pouco à frente da CPU: jogos competitivos em 1080p tendem a ficar limitados pela CPU; " +
                     "em 1440p/4K a GPU volta a ser o limite.",
            >= 0.7 => "Hardware equilibrado: na maioria dos jogos a GPU será o limite, que é o cenário ideal.",
            > 0.4 => "CPU mais forte que a GPU: a placa de vídeo é o limite. Upgrade de GPU traria o maior ganho.",
            _ => "GPU muito fraca para esta CPU: a placa de vídeo limita quase tudo."
        };
        return (ratio, text);
    }
}

/// <summary>Acumula amostras durante uma sessão de jogo e produz o relatório.</summary>
public sealed class BottleneckSession
{
    private readonly List<(LoadSample Sample, Limiter Limiter)> _samples = new();
    public DateTime Start { get; } = DateTime.Now;
    public TimeSpan Duration { get; init; }
    public int Count => _samples.Count;
    public bool Done => DateTime.Now - Start >= Duration;

    public void Add(LoadSample s) => _samples.Add((s, s.Classify()));

    public IReadOnlyList<(Limiter Limiter, double Pct)> Distribution()
    {
        if (_samples.Count == 0) return Array.Empty<(Limiter, double)>();
        Span<int> counts = stackalloc int[6]; // Gpu, Cpu, Vram, Ram, Thermal, Capped
        foreach (var s in _samples) counts[(int)s.Limiter]++;
        var result = new List<(Limiter, double)>(6);
        double total = Math.Max(1, _samples.Count);
        for (int i = 0; i < counts.Length; i++)
            if (counts[i] > 0) result.Add(((Limiter)i, 100.0 * counts[i] / total));
        result.Sort((a, b) => b.Item2.CompareTo(a.Item2));
        return result;
    }

    public Limiter? Main => _samples.Count == 0 ? null : Distribution()[0].Limiter;

    public double? AvgFps
    {
        get
        {
            double sum = 0; int count = 0;
            foreach (var s in _samples)
                if (s.Sample.Fps > 0) { sum += s.Sample.Fps!.Value; count++; }
            return count > 0 ? sum / count : null;
        }
    }

    public double? Avg(Func<LoadSample, float?> sel)
    {
        double sum = 0; int count = 0;
        foreach (var s in _samples)
        {
            var v = sel(s.Sample);
            if (v != null) { sum += v.Value; count++; }
        }
        return count > 0 ? sum / count : null;
    }

    public double? Max(Func<LoadSample, double?> sel)
    {
        double max = double.MinValue; bool found = false;
        foreach (var s in _samples)
        {
            var v = sel(s.Sample);
            if (v != null) { if (v.Value > max) max = v.Value; found = true; }
        }
        return found ? max : null;
    }
}
