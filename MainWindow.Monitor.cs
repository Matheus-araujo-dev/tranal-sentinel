using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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
    private HashSet<string> OvShow => _ovShow ??= CarregarEscolhasDoMonitor();

    /// <summary>
    /// As escolhas de "O que mostrar", com a ventoinha acrescentada uma vez a
    /// quem gravou as escolhas antes de ela existir (o painel antigo a mostrava
    /// sempre, e ela não pode sumir na atualização).
    /// </summary>
    private HashSet<string> CarregarEscolhasDoMonitor()
    {
        var (itens, mudou) = OverlayMetrics.MigrarVentoinha(_settings.OverlayItems, _settings.OverlayVentoinhaMigrada);
        if (!_settings.OverlayVentoinhaMigrada)
        {
            _settings.OverlayItems = itens;
            _settings.OverlayVentoinhaMigrada = true;
            _settings.Save();
            if (mudou) RegistroDiario.Registrar("A ventoinha entrou nas escolhas do monitor (o painel antigo a mostrava sempre).", "MONITOR");
        }
        return itens is { } l ? l.ToHashSet() : OverlayMetrics.Defaults();
    }

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
        ["fan"] = "A ventoinha do sistema que está girando mais rápido agora, com o nome e o RPM. É a mesma da página TRANAL VENTOINHAS.",
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

        // A ventoinha: a mesma escolha do antigo painel (a de maior RPM entre
        // as que dão leitura), com a regra em PainelRegras.
        if (show.Contains("fan"))
            lines.Add(new OverlayLine("VENT", PainelRegras.ValorDaVentoinha(PainelRegras.EscolherVentoinha(VentoinhasDoMonitor()))));
        return lines;
    }

    /// <summary>As ventoinhas que a página TRANAL VENTOINHAS acompanha, no formato do monitor.</summary>
    private List<PainelVentoinha> VentoinhasDoMonitor()
    {
        try
        {
            return _ventoinhas.Cartoes()
                .Select(c => new PainelVentoinha(VentoinhaRegras.NomeLegivel(c.Nome), c.RpmAtual))
                .ToList();
        }
        catch (Exception ex)
        {
            // Sem a lista das ventoinhas, a linha mostra "—": número ausente, nunca inventado.
            System.Diagnostics.Debug.WriteLine("Ventoinhas do monitor: " + ex.Message);
            return new List<PainelVentoinha>();
        }
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

            // Ligado com o jogo já em tela cheia na frente (o Ctrl + Shift + O
            // dentro da partida): começa direto como overlay. O FPS ainda não
            // foi medido nesse instante, então vale só a tela cheia; se não for
            // jogo, a histerese devolve o painel em seguida.
            bool jogoNaFrente = !_gpuBenchActive && MonitorWindow.PrimeiroPlanoOcupaOMonitor();
            _modoMonitor.ComecarEm(jogoNaFrente);

            if (jogoNaFrente)
            {
                _monitor2!.TrocarModo(MonitorWindow.Modo.Jogo);
                ApplyOverlayOptions();
                _monitor2.Topmost = true;
                if (!_monitor2.IsVisible) _monitor2.Show();
                _monitor2.KeepOnTop();
                IniciarTimerMonitor();
            }
            else
            {
                _monitor2!.Topmost = _settings.PainelSempreNoTopo;
                _monitor2.TrocarModo(MonitorWindow.Modo.Desktop);

                // O tamanho escolhido vale nos dois modos.
                _monitor2.SetScale(_settings.OverlayScale);

                if (!_monitor2.IsVisible) _monitor2.Show();

                if (AtualizarConteudo(null))
                {
                    _monitor2.UpdateLayout();
                    RestaurarPosicaoDoPainel();

                    IniciarTimerMonitor();
                }
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

        // O benchmark desliga e religa o monitor só para pausar: isso não é
        // escolha do dono e não vai para o disco (se o app cair no meio do
        // bench, o monitor tem que voltar no próximo arranque).
        if (!_pausandoMonitor)
        {
            _settings.PainelVisivel = ChkPainel.IsChecked == true;
            _settings.Save();
        }
        AtualizarEstadoFrames();
    }

    /// <summary>
    /// True enquanto um benchmark pausa o monitor (consultas ao driver geram
    /// engasgos no frametime). A pausa não grava a preferência.
    /// </summary>
    private bool _pausandoMonitor;

    /// <summary>Pausa o monitor durante um benchmark. Devolve se ele estava ligado.</summary>
    private bool PausarMonitor()
    {
        bool estava = ChkPainel.IsChecked == true;
        if (!estava) return false;

        _pausandoMonitor = true;
        try { ChkPainel.IsChecked = false; }
        finally { _pausandoMonitor = false; }
        return true;
    }

    /// <summary>Religa o monitor depois do benchmark, se ele estava ligado.</summary>
    private void RetomarMonitor(bool estava)
    {
        if (!estava) return;

        _pausandoMonitor = true;
        try { ChkPainel.IsChecked = true; }
        finally { _pausandoMonitor = false; }
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
            _monitor2.SetScale(_settings.OverlayScale);
            _monitor2.Topmost = _settings.PainelSempreNoTopo;
            AtualizarConteudo(null);
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

    /// <summary>
    /// Leva o painel do desktop para o canto escolhido na página MONITOR (a
    /// área de trabalho do monitor principal, sem a barra de tarefas) e grava
    /// essa posição. Depois o dono ainda pode arrastar.
    /// </summary>
    private void ColocarPainelNoCanto()
    {
        if (_monitor2 is null || _monitor2.ModoAtual != MonitorWindow.Modo.Desktop) return;

        _monitor2.UpdateLayout();
        var area = SystemParameters.WorkArea;
        var (largura, altura) = TamanhoDoMonitor();
        var (x, y) = PainelRegras.PosicaoNoCanto(_settings.OverlayCorner, largura, altura,
                                                 area.Left, area.Top, area.Right, area.Bottom);
        _monitor2.Left = x;
        _monitor2.Top = y;

        SalvarPosicaoDoPainel();
    }

    /// <summary>
    /// O painel está no canto escolhido? Serve para a troca de tamanho: um
    /// painel colado no canto direito, ao crescer, precisa continuar colado
    /// nele (e não sair da tela); um que o dono arrastou fica onde está.
    /// </summary>
    private bool PainelEstaNoCanto()
    {
        if (_monitor2 is null || _monitor2.ModoAtual != MonitorWindow.Modo.Desktop) return false;
        var area = SystemParameters.WorkArea;
        var (largura, altura) = TamanhoDoMonitor();
        var (x, y) = PainelRegras.PosicaoNoCanto(_settings.OverlayCorner, largura, altura,
                                                 area.Left, area.Top, area.Right, area.Bottom);
        return Math.Abs(_monitor2.Left - x) < 2 && Math.Abs(_monitor2.Top - y) < 2;
    }

    private (double largura, double altura) TamanhoDoMonitor()
    {
        double largura = _monitor2 is { ActualWidth: > 0 } ? _monitor2.ActualWidth : 260;
        double altura = _monitor2 is { ActualHeight: > 0 } ? _monitor2.ActualHeight : 120;
        return (largura, altura);
    }

    /// <summary>
    /// Preenche o monitor com o que o dono escolheu em "O que mostrar" — o
    /// mesmo conteúdo no desktop e no jogo. Devolve false se a atualização
    /// falhou (o monitor é desligado e a falha vai para o diário).
    /// </summary>
    private bool AtualizarConteudo(FrameStats? f)
    {
        if (_monitor2 is null || !_monitor2.IsVisible) return false;

        try
        {
            // Durante um benchmark as leituras ficam pausadas: nada de consultar
            // o driver nem de mostrar o número parado como se fosse de agora.
            // Vale para todo bench, inclusive os que não pausam o monitor.
            if (_gpuBenchActive)
            {
                _monitor2.UpdateOverlay(null, OverlayMetrics.LinhasSemLeitura(OvShow), OvShow, null);
                return true;
            }

            _monitor2.UpdateOverlay(f, BuildOverlayLines(f), OvShow, OvShow.Contains("clock") ? DateTime.Now.ToString("HH:mm") : null);
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

        // Sem posição guardada (ou fora da tela): o canto escolhido.
        ColocarPainelNoCanto();
    }

    /// <summary>Prepara a página de monitor (chamada quando o menu é aberto).</summary>
    private void InitMonitor()
    {
        ChkPainel.IsChecked = _monitor2 is not null && _monitor2.IsVisible;
        ChkPainelTopo.IsChecked = _settings.PainelSempreNoTopo;
    }

    // ------------------------------------------------------------------ overlay (modo jogo)

    /// <summary>
    /// O dono escolheu um canto (ou clicou de novo no que já estava marcado):
    /// vale nos dois modos. No desktop o painel vai para lá na hora; no jogo
    /// o overlay fica nesse canto da tela do jogo.
    /// </summary>
    private void OvCorner_Click(object sender, RoutedEventArgs e)
    {
        if (!_overlayReady) return;
        _settings.OverlayCorner = CheckedTag(OvCornerPanel) ?? "TL";
        _settings.Save();
        ApplyOverlayOptions();
        ColocarPainelNoCanto();
    }

    /// <summary>O dono escolheu o tamanho: vale nos dois modos.</summary>
    private void OvScale_Changed(object sender, RoutedEventArgs e)
    {
        if (!_overlayReady) return;
        bool noCanto = PainelEstaNoCanto();
        _settings.OverlayScale = double.Parse(CheckedTag(OvScalePanel) ?? "1", System.Globalization.CultureInfo.InvariantCulture);
        _settings.Save();
        ApplyOverlayOptions();

        // Colado no canto, ele continua no canto depois de crescer ou
        // encolher; arrastado pelo dono, fica onde está (desde que na tela).
        if (noCanto) ColocarPainelNoCanto();
        else if (_monitor2 is { ModoAtual: MonitorWindow.Modo.Desktop })
        {
            _monitor2.UpdateLayout();
            var (largura, altura) = TamanhoDoMonitor();
            if (!MonitorWindow.DentroDaTela(_monitor2.Left, _monitor2.Top, largura, altura)) ColocarPainelNoCanto();
        }
    }

    private void ApplyOverlayOptions()
    {
        if (_monitor2 == null) return;
        _monitor2.Corner = _settings.OverlayCorner;
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
            AtualizarConteudo(f);
            if (_monitor2.ModoAtual == MonitorWindow.Modo.Jogo) _monitor2.KeepOnTop();
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

    // ------------------------------------------------------------------ atalho

    /// <summary>O atalho em vigor (o escolhido, ou o padrão).</summary>
    private Atalho _atalho = AtalhoRegras.Padrao;

    /// <summary>O Windows aceitou o registro do atalho em vigor?</summary>
    private bool _atalhoAtivo;

    /// <summary>O botão está esperando a combinação nova.</summary>
    private bool _capturandoAtalho;

    /// <summary>
    /// Registra no Windows o atalho gravado (ou o padrão). Se outro programa
    /// já usa a combinação, o Windows recusa: o motivo vai para o diário e
    /// para a página, onde o dono escolhe outra.
    /// </summary>
    private void RegistrarAtalhoInicial()
    {
        _atalho = AtalhoRegras.Carregar(_settings.AtalhoMonitorModificadores, _settings.AtalhoMonitorTecla);
        if (!TentarRegistrarAtalho(_atalho, out int erro))
            RegistroDiario.Registrar(
                $"O atalho {AtalhoRegras.Texto(_atalho)} não pôde ser registrado (erro {erro}): outro programa já usa esse atalho. Escolha outro na página MONITOR.",
                "MONITOR");
        AtualizarTelaDoAtalho(null);
    }

    /// <summary>Troca o registro do Windows para <paramref name="a"/> (o anterior sai antes).</summary>
    private bool TentarRegistrarAtalho(Atalho a, out int erro)
    {
        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        UnregisterHotKey(hwnd, HotkeyId);
        bool ok = RegisterHotKey(hwnd, HotkeyId, a.Modificadores | AtalhoRegras.SemRepeticao, a.Tecla);
        erro = ok ? 0 : System.Runtime.InteropServices.Marshal.GetLastWin32Error();
        _atalhoAtivo = ok;
        return ok;
    }

    /// <summary>Escreve o atalho no botão e o estado dele (ativo, recusado ou o aviso dado).</summary>
    private void AtualizarTelaDoAtalho(string? aviso)
    {
        if (BtnAtalho is null) return;
        BtnAtalho.Content = _capturandoAtalho ? "aperte o atalho…" : AtalhoRegras.Texto(_atalho);
        AtalhoStatus.Text = aviso ?? (_atalhoAtivo
            ? "Ativo: liga e desliga o monitor, inclusive dentro do jogo."
            : $"O Windows recusou o {AtalhoRegras.Texto(_atalho)}: outro programa já usa essa combinação (o AMD Software usa Ctrl + Shift + O no painel de métricas dele). Clique no botão e escolha outra.");
    }

    private void BtnAtalho_Click(object sender, RoutedEventArgs e)
    {
        // Enquanto captura, o atalho atual sai do Windows: senão apertar a
        // mesma combinação ligaria o monitor em vez de chegar ao botão.
        _capturandoAtalho = true;
        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        UnregisterHotKey(hwnd, HotkeyId);
        Keyboard.Focus(BtnAtalho);
        AtualizarTelaDoAtalho("Aperte a combinação: Ctrl, Alt ou Win junto com uma letra, um número ou F1 a F24. Esc cancela.");
    }

    private void BtnAtalho_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!_capturandoAtalho) return;
        e.Handled = true;

        Key tecla = e.Key == Key.System ? e.SystemKey : e.Key;
        if (tecla == Key.Escape) { CancelarCapturaDoAtalho(); return; }

        // Só o modificador ainda: espera a tecla de verdade.
        if (tecla is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
                  or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin) return;

        var m = Keyboard.Modifiers;
        uint mods = 0;
        if (m.HasFlag(ModifierKeys.Control)) mods |= AtalhoRegras.Ctrl;
        if (m.HasFlag(ModifierKeys.Alt)) mods |= AtalhoRegras.Alt;
        if (m.HasFlag(ModifierKeys.Shift)) mods |= AtalhoRegras.Shift;
        if (m.HasFlag(ModifierKeys.Windows)) mods |= AtalhoRegras.Win;
        var novo = new Atalho(mods, (uint)KeyInterop.VirtualKeyFromKey(tecla));

        if (AtalhoRegras.Erro(novo) is { } erroDaRegra)
        {
            AtualizarTelaDoAtalho(erroDaRegra + " Tente outra combinação, ou Esc para cancelar.");
            return;
        }
        AplicarAtalho(novo);
    }

    /// <summary>Tenta o atalho novo; se o Windows recusar, o anterior volta.</summary>
    private void AplicarAtalho(Atalho novo)
    {
        _capturandoAtalho = false;
        if (TentarRegistrarAtalho(novo, out int erro))
        {
            _atalho = novo;
            _settings.AtalhoMonitorModificadores = novo.Modificadores;
            _settings.AtalhoMonitorTecla = novo.Tecla;
            _settings.Save();
            RegistroDiario.Registrar($"Atalho do monitor trocado para {AtalhoRegras.Texto(novo)}.", "MONITOR");
            AtualizarTelaDoAtalho(null);
            return;
        }

        TentarRegistrarAtalho(_atalho, out _);
        AtualizarTelaDoAtalho(
            $"O Windows recusou o {AtalhoRegras.Texto(novo)} (erro {erro}): outro programa já usa essa combinação. " +
            $"Continua valendo o {AtalhoRegras.Texto(_atalho)}{(_atalhoAtivo ? "" : ", que também está recusado")}. Clique de novo para escolher outra.");
    }

    private void CancelarCapturaDoAtalho()
    {
        if (!_capturandoAtalho) return;
        _capturandoAtalho = false;
        TentarRegistrarAtalho(_atalho, out _);
        AtualizarTelaDoAtalho(null);
    }

    private void BtnAtalho_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) => CancelarCapturaDoAtalho();

    private void BtnAtalhoPadrao_Click(object sender, RoutedEventArgs e) => AplicarAtalho(AtalhoRegras.Padrao);

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
