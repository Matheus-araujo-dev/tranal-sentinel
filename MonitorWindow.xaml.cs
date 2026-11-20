using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;

namespace TranalSentinel;

/// <summary>
/// Janela unificada de monitoramento: substitui o <c>OverlayWindow</c> (OSD
/// sobre jogos) e o <c>PainelWindow</c> (painel arrastável no desktop) numa
/// janela só, com dois modos.
///
/// <list type="bullet">
///   <item><b>Desktop</b> — arrastável, interativo, mostra CPU/GPU/RAM/ventoinha
///   (linhas do <see cref="PainelRegras"/>). Duplo clique abre a janela
///   principal. Botão "×" esconde.</item>
///   <item><b>Jogo</b> — click-through (<c>WS_EX_TRANSPARENT</c>), segue o
///   monitor do jogo, mostra FPS/frametime/gráfico e sensores configuráveis
///   (linhas do <see cref="OverlayMetrics"/>).</item>
/// </list>
///
/// A troca de modo esconde e reabre a janela com os estilos certos — é o
/// jeito seguro no WPF (trocar <c>WS_EX_TRANSPARENT</c> sem esconder não
/// atualiza o hit-test do Windows).
/// </summary>
public partial class MonitorWindow : Window
{
    /// <summary>O modo em que a janela está agora.</summary>
    public enum Modo { Desktop, Jogo }

    private Modo _modo = Modo.Desktop;
    private IntPtr _hwnd;

    // ---- arrasto (modo desktop) ----
    private bool _arrastando;
    private Point _origemEmPixels;
    private double _leftInicial, _topInicial;

    // ---- overlay (modo jogo): seguir o monitor do jogo ----
    private IntPtr _hook;
    private IntPtr _monitor;
    private readonly WinEventProc _onForeground;

    public string Corner { get; set; } = "TL";

    public MonitorWindow()
    {
        InitializeComponent();
        _onForeground = (_, _, hwnd, _, _, _, _) => { TrackGame(hwnd); KeepOnTop(); };

        SourceInitialized += (_, _) =>
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            AplicarEstilo();
        };

        SizeChanged += (_, _) => { if (_modo == Modo.Jogo) KeepOnTop(); };

        // Arrasto: só funciona no modo desktop.
        MouseLeftButtonDown += (_, e) =>
        {
            if (_modo != Modo.Desktop || e.ButtonState != MouseButtonState.Pressed) return;
            _origemEmPixels = PointToScreen(e.GetPosition(this));
            GuardarPosicaoInicial();
            try { if (!CaptureMouse()) return; _arrastando = true; } catch { }
        };
        MouseMove += (_, e) => Arrastando(e);
        MouseLeftButtonUp += (_, _) => FimDoArrasto();
        MouseDoubleClick += (_, e) =>
        {
            if (_modo != Modo.Desktop) return;
            if (VeioDoBotao(e.OriginalSource)) return;
            AoAbrirJanela?.Invoke();
        };

        IsVisibleChanged += (_, _) =>
        {
            if (_modo == Modo.Jogo)
            {
                if (IsVisible && _hook == IntPtr.Zero)
                {
                    _hook = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, _onForeground, 0, 0, WINEVENT_OUTOFCONTEXT);
                    TrackGame(GetForegroundWindow());
                    KeepOnTop();
                }
                else if (!IsVisible) Unhook();
            }
        };
        Closed += (_, _) => Unhook();
    }

    /// <summary>Disparado quando o dono pede para abrir a janela principal (duplo clique no modo desktop).</summary>
    public event Action? AoAbrirJanela;

    /// <summary>Disparado quando o dono fecha o monitor (botão "×" no modo desktop).</summary>
    public event Action? AoFechar;

    /// <summary>O modo atual da janela.</summary>
    public Modo ModoAtual => _modo;

    /// <summary>
    /// Troca o modo da janela. A janela é escondida e reaberta para o
    /// Windows atualizar o hit-test.
    /// </summary>
    public void TrocarModo(Modo novo)
    {
        if (novo == _modo) return;
        bool visivel = IsVisible;
        if (visivel) Hide();
        _modo = novo;
        if (_hwnd != IntPtr.Zero) AplicarEstilo();
        AplicarLayout();
        if (visivel) Show();
    }

    // ---- seção de FPS (modo jogo) ----

    public void SetScale(double s) { Scale.ScaleX = Scale.ScaleY = s; }

    /// <summary>Atualiza a seção de FPS e as linhas de sensores do overlay.</summary>
    public void UpdateOverlay(FrameStats? f, IReadOnlyList<OverlayLine> lines, IReadOnlySet<string> show, string? extra)
    {
        static Visibility V(bool on) => on ? Visibility.Visible : Visibility.Collapsed;
        bool fps = show.Contains("fps"), ft = show.Contains("frametime");
        SecaoFps.Visibility = V(fps || ft || show.Contains("low1") || show.Contains("graph"));
        OFpsRow.Visibility = V(fps || ft);
        OFps.Visibility = OFpsUnit.Visibility = V(fps);
        OFrameTime.Visibility = V(ft);
        OFrameTime.Margin = new Thickness(fps ? 12 : 0, 0, 0, fps ? 4 : 0);
        OLow.Visibility = V(show.Contains("low1"));
        OGraph.Visibility = V(show.Contains("graph"));

        if (f != null)
        {
            OFps.Text = $"{f.Fps:0}";
            OFrameTime.Text = SensorTexto.Milissegundos(f.FrameTimeMs);
            OLow.Text = $"1% low {f.Low1Fps:0}";
        }
        else
        {
            OFps.Text = "—";
            OFrameTime.Text = fps ? "" : SensorTexto.Ausente;
            OLow.Text = "aguardando jogo";
        }

        var foot = new List<string>();
        if (show.Contains("game") && f != null) foot.Add(f.ProcessName);
        if (extra != null) foot.Add(extra);
        OGame.Text = string.Join("  ·  ", foot);
        OGame.Visibility = V(foot.Count > 0);

        ORows.ItemsSource = lines;
        ORows.Visibility = V(lines.Count > 0);
        if (OGraph.Visibility == Visibility.Visible) DrawGraph(f?.RecentFrameTimes);
    }

    // ---- seção do painel (modo desktop) ----

    /// <summary>Atualiza as linhas do painel (CPU, GPU, RAM, ventoinha).</summary>
    public void UpdatePainel(IReadOnlyList<PainelLinha> linhas)
    {
        PLinhas.ItemsSource = linhas;
    }

    // ---- posição ----

    public void Reposition() => KeepOnTop();

    /// <summary>
    /// A posição da janela está em algum monitor?
    /// </summary>
    public static bool DentroDaTela(double left, double top, double largura, double altura) =>
        PainelRegras.DentroDaTela(left, top, largura, altura,
            SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);

    // ---- internos ----

    private void AplicarEstilo()
    {
        if (_hwnd == IntPtr.Zero) return;
        int ex = GetWindowLong(_hwnd, GWL_EXSTYLE);

        // Limpa os bits que cada modo controla.
        ex &= ~(WS_EX_TRANSPARENT | WS_EX_LAYERED | WS_EX_TOOLWINDOW);
        // WS_EX_NOACTIVATE fica sempre — nenhum dos dois modos rouba foco.
        ex |= WS_EX_NOACTIVATE;

        if (_modo == Modo.Jogo)
            ex |= WS_EX_TRANSPARENT | WS_EX_LAYERED | WS_EX_TOOLWINDOW;

        SetWindowLong(_hwnd, GWL_EXSTYLE, ex);
    }

    private void AplicarLayout()
    {
        if (_modo == Modo.Jogo)
        {
            Cabecalho.Visibility = Visibility.Collapsed;
            PLinhas.Visibility = Visibility.Collapsed;
            SecaoFps.Visibility = Visibility.Visible;
            ORows.Visibility = Visibility.Visible;
            Box.Background = new SolidColorBrush(Color.FromArgb(0xC8, 0, 0, 0));
            Box.Padding = new Thickness(12, 9, 12, 9);
        }
        else
        {
            Cabecalho.Visibility = Visibility.Visible;
            PLinhas.Visibility = Visibility.Visible;
            SecaoFps.Visibility = Visibility.Collapsed;
            ORows.Visibility = Visibility.Collapsed;
            OGame.Visibility = Visibility.Collapsed;
            Box.Background = new SolidColorBrush(Color.FromArgb(0xD9, 0x0A, 0x0A, 0x0A));
            Box.Padding = new Thickness(12, 9, 12, 9);
        }
    }

    /// <summary>Cola no canto do monitor do jogo e volta para o topo (modo jogo).</summary>
    public void KeepOnTop()
    {
        if (_hwnd == IntPtr.Zero || !IsVisible) return;
        if (_modo != Modo.Jogo) return;
        if (_monitor == IntPtr.Zero) _monitor = MonitorFromPoint(new POINT(), MONITOR_DEFAULTTOPRIMARY);
        var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(_monitor, ref mi) || !GetWindowRect(_hwnd, out var wr)) return;

        int w = wr.Right - wr.Left, h = wr.Bottom - wr.Top;
        int m = (int)Math.Round(12 * VisualTreeHelper.GetDpi(this).DpiScaleX);
        var r = mi.rcMonitor;
        int x = Corner.EndsWith("R") ? r.Right - w - m : r.Left + m;
        int y = Corner.StartsWith("B") ? r.Bottom - h - m : r.Top + m;
        SetWindowPos(_hwnd, HWND_TOPMOST, x, y, 0, 0, SWP_NOSIZE | SWP_NOACTIVATE | SWP_NOOWNERZORDER);
    }

    private void TrackGame(IntPtr fg)
    {
        if (fg == IntPtr.Zero) return;
        GetWindowThreadProcessId(fg, out uint pid);
        if (pid == (uint)Environment.ProcessId) return;
        _monitor = MonitorFromWindow(fg, MONITOR_DEFAULTTONEAREST);
    }

    private void Unhook()
    {
        if (_hook == IntPtr.Zero) return;
        UnhookWinEvent(_hook);
        _hook = IntPtr.Zero;
    }

    private void DrawGraph(IReadOnlyList<double>? ft)
    {
        OGraph.Children.Clear();
        double w = OGraph.Width, h = OGraph.Height;
        OGraph.Children.Add(new Line { X1 = 0, X2 = w, Y1 = h - 0.5, Y2 = h - 0.5, Stroke = new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)) });
        if (ft == null || ft.Count < 2) return;

        double max = Math.Max(ft.Max(), ft.Average() * 2);
        var line = new Polyline { Stroke = Brushes.White, StrokeThickness = 1 };
        double step = w / (ft.Count - 1);
        for (int i = 0; i < ft.Count; i++)
            line.Points.Add(new Point(i * step, h - 1 - (h - 2) * Math.Min(1, ft[i] / max)));
        OGraph.Children.Add(line);
    }

    // ---- arrasto (modo desktop) ----

    private void Arrastando(MouseEventArgs e)
    {
        if (!_arrastando || _modo != Modo.Desktop || e.LeftButton != MouseButtonState.Pressed) return;
        var tela = PointToScreen(e.GetPosition(this));
        var dpi = VisualTreeHelper.GetDpi(this);
        Left = _leftInicial + (tela.X - _origemEmPixels.X) / dpi.DpiScaleX;
        Top = _topInicial + (tela.Y - _origemEmPixels.Y) / dpi.DpiScaleY;
    }

    private void FimDoArrasto()
    {
        _arrastando = false;
        try { ReleaseMouseCapture(); } catch { }
    }

    private void GuardarPosicaoInicial()
    {
        if (PainelRegras.PosicaoValida(Left, Top))
        {
            _leftInicial = Left;
            _topInicial = Top;
            return;
        }
        var canto = PointToScreen(new Point(0, 0));
        var dpi = VisualTreeHelper.GetDpi(this);
        _leftInicial = canto.X / dpi.DpiScaleX;
        _topInicial = canto.Y / dpi.DpiScaleY;
    }

    private void BtnFechar_Click(object sender, RoutedEventArgs e) => AoFechar?.Invoke();

    private bool VeioDoBotao(object? origem) =>
        origem is DependencyObject d && BtnFechar.IsAncestorOf(d);

    // ---- P/Invoke ----

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x20, WS_EX_LAYERED = 0x80000, WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x8000000;
    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private const uint SWP_NOSIZE = 0x1, SWP_NOACTIVATE = 0x10, SWP_NOOWNERZORDER = 0x200;
    private const uint EVENT_SYSTEM_FOREGROUND = 0x0003, WINEVENT_OUTOFCONTEXT = 0;
    private const uint MONITOR_DEFAULTTOPRIMARY = 1, MONITOR_DEFAULTTONEAREST = 2;

    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct MONITORINFO { public int cbSize; public RECT rcMonitor, rcWork; public uint dwFlags; }
    private delegate void WinEventProc(IntPtr hook, uint ev, IntPtr hwnd, int idObject, int idChild, uint thread, uint time);

    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr h, int index);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr h, int index, int value);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr h, uint flags);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(POINT p, uint flags);
    [DllImport("user32.dll")] private static extern bool GetMonitorInfo(IntPtr mon, ref MONITORINFO mi);
    [DllImport("user32.dll")] private static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr module, WinEventProc proc, uint pid, uint thread, uint flags);
    [DllImport("user32.dll")] private static extern bool UnhookWinEvent(IntPtr hook);
}
