using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace TranalSentinel;

/// <summary>
/// O painel flutuante: uma janela pequena, arrastável, com os números do PC
/// sempre à vista.
///
/// <para>
/// Ela é a segunda janela do app (como o <see cref="OverlayWindow"/>), e não
/// um app à parte: nasce com o TRANAL SENTINEL e morre com ele.
///
/// <para>
/// O botão "×" esconde o painel — o app continua rodando na bandeja, e só o
/// "Sair" da bandeja encerra tudo.
/// </para>
/// </summary>
public partial class PainelWindow : Window
{
    private bool _arrastando;
    private Point _origemEmPixels;
    private double _leftInicial;
    private double _topInicial;

    public PainelWindow()
    {
        InitializeComponent();

        // O painel nunca rouba o foco do jogo. O "ShowActivated=False" só
        // protege o Show: sem este estilo do Windows, clicar no painel (ou
        // arrastá-lo) ativa a janela e o jogo perde o foco. O mesmo cuidado
        // que o overlay e o toast já têm.
        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            int ex = GetWindowLong(hwnd, GWL_EXSTYLE);
            SetWindowLong(hwnd, GWL_EXSTYLE, ex | WS_EX_NOACTIVATE);
        };

        // O corpo do painel é a alça de arrastar.
        MouseLeftButtonDown += (_, e) =>
        {
            if (e.ButtonState != MouseButtonState.Pressed) return;

            // A posição inicial é guardada aqui: o arrasto soma o
            // deslocamento do mouse a ela. Sem ela, o painel saltaria
            // para o canto da tela em vez de andar.
            _origemEmPixels = PointToScreen(e.GetPosition(this));
            GuardarPosicaoInicial();

            try
            {
                // Sem a captura do mouse, o arrasto só funciona com o
                // cursor sobre o painel — e o painel "gruda" quando o
                // mouse sai da janela.
                if (!CaptureMouse()) return;
                _arrastando = true;
            }
            catch
            {
                // A captura do mouse pode falhar se a janela perdeu o foco
                // entre o clique e a captura: sem captura, o arrasto não
                // começa, e o painel continua onde está.
            }
        };

        MouseMove += (_, e) => Arrastando(e);
        MouseLeftButtonUp += (_, _) => FimDoArrasto();
        MouseDoubleClick += (_, e) =>
        {
            // O botão "×" fica dentro do painel, e o duplo clique nele
            // chega na janela: fechar o painel não pode acabar abrindo a
            // janela principal por baixo dele.
            if (VeioDoBotao(e.OriginalSource)) return;
            AoAbrirJanela?.Invoke();
        };
    }

    /// <summary>Disparado quando o dono pede para abrir a janela principal (duplo clique).</summary>
    public event Action? AoAbrirJanela;

    /// <summary>Disparado quando o dono fecha o painel (ele se esconde; o app continua).</summary>
    public event Action? AoFechar;

    /// <summary>Mostra as linhas (o texto já vem pronto do núcleo puro).</summary>
    public void Update(IReadOnlyList<PainelLinha> linhas)
    {
        PLinhas.ItemsSource = linhas;
    }

    /// <summary>
    /// A posição do painel está em algum monitor?
    ///
    /// <para>
    /// A regra é do núcleo puro (<see cref="PainelRegras.DentroDaTela"/>): a
    /// janela só entrega a tela virtual em DIPs.
    /// </para>
    /// </summary>
    public static bool DentroDaTela(double left, double top, double largura, double altura) =>
        PainelRegras.DentroDaTela(left, top, largura, altura,
            SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);

    private void Arrastando(MouseEventArgs e)
    {
        if (!_arrastando || e.LeftButton != MouseButtonState.Pressed) return;

        var tela = PointToScreen(e.GetPosition(this));
        var dpi = VisualTreeHelper.GetDpi(this);

        // PointToScreen devolve pixels do dispositivo; Left/Top são DIPs.
        // Sem a conversão, o painel andaria torto em tela com escala.
        //
        // E o que entra em Left/Top é a posição inicial mais o
        // deslocamento: a diferença entre o mouse de agora e o mouse do
        // pressionar é o quanto o painel andou, não onde ele está.
        Left = _leftInicial + (tela.X - _origemEmPixels.X) / dpi.DpiScaleX;
        Top = _topInicial + (tela.Y - _origemEmPixels.Y) / dpi.DpiScaleY;
    }

    private void FimDoArrasto()
    {
        _arrastando = false;
        try { ReleaseMouseCapture(); }
        catch
        {
            // Se o mouse não estava capturado, não há o que liberar.
        }
    }

    /// <summary>
    /// Guarda de onde o painel parte (em DIPs).
    ///
    /// <para>
    /// O <c>Left</c> e o <c>Top</c> saem <c>NaN</c> enquanto a janela não foi
    /// posicionada, e o WPF não avisa: o arrasto summiria em silêncio, com o
    /// painel grudado no lugar. Quando é esse o caso, a posição inicial vem
    /// do canto da janela na tela.
    /// </para>
    /// </summary>
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

    /// <summary>
    /// O clique veio do botão "×"?
    ///
    /// <para>
    /// O botão está dentro do painel, e o duplo clique nele chega na
    /// janela. Sem esta guarda, fechar o painel com dois cliques abriria
    /// a janela principal por baixo dele.
    /// </para>
    ///
    /// <para>
    /// O acerto do botão resolve para o glifo (o TextBlock do "×") ou
    /// para o template dele — e os dois são descendentes do botão, então
    /// a guarda os alcança.
    /// </para>
    /// </summary>
    private bool VeioDoBotao(object? origem) =>
        origem is DependencyObject d && BtnFechar.IsAncestorOf(d);

    /// <summary>O índice do estilo estendido da janela (GWL_EXSTYLE).</summary>
    private const int GWL_EXSTYLE = -20;

    /// <summary>
    /// O estilo que diz ao Windows que a janela não deve ser ativada.
    /// </summary>
    ///
    /// <para>
    /// Sem ele, clicar no painel (ou arrastá-lo) ativa a janela e o jogo
    /// perde o foco. O overlay e o toast já usam o mesmo cuidado.
    /// </para>
    /// </summary>
    private const int WS_EX_NOACTIVATE = 0x8000000;

    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr h, int index);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr h, int index, int value);
}
