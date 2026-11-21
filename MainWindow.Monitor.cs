using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using LibreHardwareMonitor.Hardware;

namespace TranalSentinel;

/// <summary>
/// Integração da <see cref="MonitorWindow"/> com a janela principal: liga e
/// desliga a janela unificada (modos desktop e jogo), o relógio que a
/// atualiza, a posição guardada e o seletor de métricas do overlay.
/// </summary>
public partial class MainWindow
{
    /// <summary>A janela unificada de monitoramento (null enquanto ela não foi ligada).</summary>
    private MonitorWindow? _monitor2;

    /// <summary>O relógio que atualiza o monitor unificado.</summary>
    private DispatcherTimer? _monitorTimer;

    // ------------------------------------------------------------------ overlay: itens configuráveis

    private HashSet<string>? _ovShow;
    private HashSet<string> OvShow => _ovShow ??= _settings.OverlayItems is { } l ? l.ToHashSet() : OverlayMetrics.Defaults();

    /// <summary>Dicas das métricas do overlay.</summary>
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

    // ------------------------------------------------------------------ overlay: seletor de métricas

    /// <summary>Cria as caixas de seleção da aba Monitor, agrupadas por componente.</summary>
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
        UpdateMonitor();
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

    // ------------------------------------------------------------------ leitura dos sensores (overlay)

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
        if (show.Contains("cpu_power")) cpu.Add(_monitor.CpuPower is float cw && cw > 0 ? SensorTexto.Watts(cw) : SensorTexto.Ausente);
        Line("CPU", cpu);

        var ram = new List<string>();
        var (total, availB) = Optimizer.MemoryStatus();
        if (show.Contains("ram_used")) ram.Add(total > 0 ? SensorTexto.Gigabytes((total - availB) / 1073741824.0) : SensorTexto.Ausente);
        if (show.Contains("ram_pct")) ram.Add(P(s.RamPct));
        Line("RAM", ram);
        return lines;
    }

    // ------------------------------------------------------------------ painel (modo desktop)

    /// <summary>
    /// Decide quando o monitor vira overlay (jogo em tela cheia na frente) e
    /// quando volta a ser painel. A regra é pura; aqui só entra a medida.
    /// </summary>
    private readonly MonitorModoRegras _modoMonitor = new();

    /// <summary>
    /// Liga ou desliga o monitor — o interruptor ÚNICO da janela flutuante.
    ///
    /// <para>
    /// Antes havia dois (painel e overlay) mandando na mesma janela, e ligar
    /// um derrubava o outro. Agora o monitor nasce como painel (arrastável,
    /// com o botão ×) e vira overlay sozinho quando um jogo em tela cheia está
    /// na frente (<see cref="MonitorModoRegras"/>).
    /// </para>
    /// </summary>
    private void ChkPainel_Changed(object sender, RoutedEventArgs e)
    {
        if (ChkPainel.IsChecked == true)
        {
            CriarMonitor();
            _modoMonitor.Reiniciar();

            _monitor2!.Topmost = _settings.PainelSempreNoTopo;
            _monitor2.TrocarModo(MonitorWindow.Modo.Desktop);

            if (!_monitor2.IsVisible) _monitor2.Show();

            if (UpdatePainel())
            {
                _monitor2.UpdateLayout();
                RestaurarPosicaoDoPainel();

                IniciarTimerMonitor();
            }
        }
        else
        {
            _monitorTimer?.Stop();

            // No modo jogo a janela está no canto do jogo: essa posição não é
            // a que o dono escolheu para o painel e não pode ser gravada.
            if (_monitor2?.ModoAtual == MonitorWindow.Modo.Desktop) SalvarPosicaoDoPainel();
            _monitor2?.Hide();
        }

        _settings.PainelVisivel = ChkPainel.IsChecked == true;
        _settings.Save();
        AtualizarEstadoFrames();
    }

    /// <summary>
    /// Troca o monitor entre painel e overlay, guardando e devolvendo a
    /// posição que o dono deu ao painel.
    /// </summary>
    private void TrocarModoDoMonitor(MonitorWindow.Modo alvo)
    {
        if (_monitor2 is null || _monitor2.ModoAtual == alvo) return;

        if (alvo == MonitorWindow.Modo.Jogo)
        {
            // O overlay vai para o canto do jogo: antes, guarda onde o painel estava.
            SalvarPosicaoDoPainel();
            _monitor2.TrocarModo(MonitorWindow.Modo.Jogo);
            ApplyOverlayOptions();
            _monitor2.Topmost = true;
            _monitor2.KeepOnTop();
        }
        else
        {
            _monitor2.TrocarModo(MonitorWindow.Modo.Desktop);
            _monitor2.SetScale(1);
            _monitor2.Topmost = _settings.PainelSempreNoTopo;
            UpdatePainel();
            _monitor2.UpdateLayout();
            RestaurarPosicaoDoPainel();
        }
    }

    /// <summary>Liga ou desliga o "sempre no topo" do painel.</summary>
    private void ChkPainelOpcao_Changed(object sender, RoutedEventArgs e)
    {
        if (_monitor2 is not null) _monitor2.Topmost = ChkPainelTopo.IsChecked == true;
        _settings.PainelSempreNoTopo = ChkPainelTopo.IsChecked == true;
        _settings.Save();
    }

    /// <summary>Devolve o painel para o canto superior direito do monitor principal.</summary>
    private void PainelCanto_Click(object sender, RoutedEventArgs e)
    {
        if (_monitor2 is null) return;

        var area = SystemParameters.WorkArea;
        var (largura, altura) = TamanhoDoMonitor();
        _monitor2.Left = area.Right - largura - 12;
        _monitor2.Top = area.Top + 12;

        SalvarPosicaoDoPainel();
    }

    private (double largura, double altura) TamanhoDoMonitor()
    {
        double largura = _monitor2 is { ActualWidth: > 0 } ? _monitor2.ActualWidth : 260;
        double altura = _monitor2 is { ActualHeight: > 0 } ? _monitor2.ActualHeight : 120;
        return (largura, altura);
    }

    private bool UpdatePainel()
    {
        if (_monitor2 is null || !_monitor2.IsVisible) return false;

        try
        {
            if (_gpuBenchActive)
            {
                _monitor2.UpdatePainel(PainelRegras.LinhasSemLeitura());
                return true;
            }

            var s = BottleneckAnalyzer.Sample(_monitor, _gpus.FirstOrDefault(), null);

            var ventoinhas = new List<PainelVentoinha>();
            try
            {
                ventoinhas = _ventoinhas.Cartoes()
                    .Select(c => new PainelVentoinha(VentoinhaRegras.NomeLegivel(c.Nome), c.RpmAtual))
                    .ToList();
            }
            catch { }

            var retrato = new PainelRetrato(
                s.CpuTemp, s.CpuTotal, s.GpuTemp, s.GpuLoad, s.RamPct, ventoinhas);

            _monitor2.UpdatePainel(PainelRegras.Linhas(retrato));
            return true;
        }
        catch (Exception ex)
        {
            _monitorTimer?.Stop();
            ChkPainel.IsChecked = false;
            _monitor2?.Hide();
            RegistroDiario.Registrar("Falha ao atualizar o monitor: " + ex.Message, "MONITOR");
            return false;
        }
    }

    private void SalvarPosicaoDoPainel()
    {
        if (_monitor2 is null) return;
        if (!PainelRegras.PosicaoValida(_monitor2.Left, _monitor2.Top)) return;

        _settings.PainelX = _monitor2.Left;
        _settings.PainelY = _monitor2.Top;
        _settings.Save();
    }

    private void RestaurarPosicaoDoPainel()
    {
        if (_monitor2 is null) return;

        var (largura, altura) = TamanhoDoMonitor();

        if (_settings.PainelX is double x && _settings.PainelY is double y
            && MonitorWindow.DentroDaTela(x, y, largura, altura))
        {
            _monitor2.Left = x;
            _monitor2.Top = y;
            return;
        }

        var area = SystemParameters.WorkArea;
        _monitor2.Left = area.Right - largura - 12;
        _monitor2.Top = area.Top + 12;
    }

    /// <summary>Prepara a página de monitor (chamada quando o menu é aberto).</summary>
    private void InitMonitor()
    {
        ChkPainel.IsChecked = _monitor2 is not null && _monitor2.IsVisible;
        ChkPainelTopo.IsChecked = _settings.PainelSempreNoTopo;
    }

    // ------------------------------------------------------------------ overlay (modo jogo)

    private void OvOption_Changed(object sender, RoutedEventArgs e)
    {
        if (!_overlayReady) return;
        _settings.OverlayCorner = CheckedTag(OvCornerPanel) ?? "TL";
        _settings.OverlayScale = double.Parse(CheckedTag(OvScalePanel) ?? "1", System.Globalization.CultureInfo.InvariantCulture);
        _settings.Save();
        ApplyOverlayOptions();
    }

    private void ApplyOverlayOptions()
    {
        if (_monitor2 == null) return;
        _monitor2.Corner = _settings.OverlayCorner;

        // O tamanho escolhido é do overlay; o painel fica sempre no tamanho normal.
        if (_monitor2.ModoAtual == MonitorWindow.Modo.Jogo || !_monitor2.IsVisible)
            _monitor2.SetScale(_settings.OverlayScale);
        _monitor2.Reposition();
    }

    private void UpdateMonitor()
    {
        // O FPS é o que diz que há um jogo na frente: com o monitor ligado, a
        // detecção (ETW) fica ligada também.
        AtualizarEstadoFrames();
        var f = _frames.GetForegroundStats();

        if (_monitor2 != null && _monitor2.IsVisible && ChkPainel.IsChecked == true && !_gpuBenchActive)
        {
            bool jogo = f != null && MonitorModoRegras.PareceJogo(true, MonitorWindow.PrimeiroPlanoOcupaOMonitor(f.ProcessId));
            bool emJogo = _modoMonitor.Observar(jogo, DateTime.UtcNow);
            TrocarModoDoMonitor(emJogo ? MonitorWindow.Modo.Jogo : MonitorWindow.Modo.Desktop);
        }

        if (_monitor2 != null && _monitor2.IsVisible)
        {
            if (_monitor2.ModoAtual == MonitorWindow.Modo.Jogo)
            {
                _monitor2.UpdateOverlay(f, BuildOverlayLines(f), OvShow, OvShow.Contains("clock") ? DateTime.Now.ToString("HH:mm") : null);
                _monitor2.KeepOnTop();
            }
            else
            {
                UpdatePainel();
            }
        }
        UpdateOverlayStatus(f);
    }

    private void UpdateOverlayStatus(FrameStats? f)
    {
        if (_frames.Error != null) OvStatus.Text = $"Detecção de FPS indisponível: {_frames.Error}";
        else if (!_frames.Running) OvStatus.Text = "Detecção de FPS parada (liga com o monitor ou com a página de gargalo)";
        else if (f != null) OvStatus.Text = $"Ativa · {f.ProcessName}: {f.Fps:0} FPS ({SensorTexto.Milissegundos(f.FrameTimeMs)}) · 1% low {f.Low1Fps:0}";
        else OvStatus.Text = "Ativa · aguardando um jogo em primeiro plano";
    }

    // ------------------------------------------------------------------ auxiliares

    private void CriarMonitor()
    {
        if (_monitor2 is not null) return;
        _monitor2 = new MonitorWindow();
        // O × só existe no modo painel (no jogo a janela é click-through):
        // fechar desliga o monitor inteiro.
        _monitor2.AoFechar += () => ChkPainel.IsChecked = false;
        _monitor2.AoAbrirJanela += () => Dispatcher.Invoke(MostrarJanela);
    }

    private void IniciarTimerMonitor()
    {
        _monitorTimer ??= new DispatcherTimer(
            TimeSpan.FromMilliseconds(500), DispatcherPriority.Normal,
            (_, _) => UpdateMonitor(), Dispatcher);
        _monitorTimer.Start();
    }
}
