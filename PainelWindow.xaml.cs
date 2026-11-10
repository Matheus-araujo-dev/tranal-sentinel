using System.Windows;
using System.Windows.Input;
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

    public PainelWindow()
    {
        InitializeComponent();

        // O corpo do painel é a alça de arrastar.
        MouseLeftButtonDown += (_, e) =>
        {
            if (e.ButtonState != MouseButtonState.Pressed) return;
            _arrastando = true;
            _origemEmPixels = PointToScreen(e.GetPosition(this));
            try { CaptureMouse(); }
            catch
            {
                // A captura do mouse pode falhar se a janela perdeu o foco
                // entre o clique e a captura: o arrasto simplesmente não
                // começa, e o painel continua onde está.
            }
        };

        MouseMove += (_, e) => Arrastando(e);
        MouseLeftButtonUp += (_, _) => FimDoArrasto();
        MouseDoubleClick += (_, _) => AoAbrirJanela?.Invoke();
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
    /// A posição é guardada em DIPs (a unidade do WPF) e a tela virtual é lida
    /// na mesma unidade. Se o dono trocou de monitor, o painel volta para o
    /// canto em vez de ficar fora da tela.
    /// </para>
    /// </summary>
    public static bool DentroDaTela(double left, double top, double largura, double altura) =>
        DentroDaTela(left, top, largura, altura,
            SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);

    /// <summary>
    /// A mesma decisão, com os limites da tela recebidos.
    ///
    /// <para>
    /// A janela do WPF não dá para testar na suíte (ela exige uma thread com
    /// STA), mas a pergunta — o painel ainda está em algum monitor? — é
    /// aritmética pura. Por isso ela mora aqui, com a tela como argumento.
    /// </para>
    /// </summary>
    public static bool DentroDaTela(double left, double top, double largura, double altura,
                                    double telaX, double telaY, double telaLargura, double telaAltura)
    {
        return left + largura > telaX && left < telaX + telaLargura
            && top + altura > telaY && top < telaY + telaAltura;
    }

    private void Arrastando(MouseEventArgs e)
    {
        if (!_arrastando || e.LeftButton != MouseButtonState.Pressed) return;

        var tela = PointToScreen(e.GetPosition(this));
        var dpi = VisualTreeHelper.GetDpi(this);

        // PointToScreen devolve pixels do dispositivo; Left/Top são DIPs.
        // Sem a conversão, o painel andaria torto em tela com escala.
        Left = (tela.X - _origemEmPixels.X) / dpi.DpiScaleX;
        Top = (tela.Y - _origemEmPixels.Y) / dpi.DpiScaleY;
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

    private void BtnFechar_Click(object sender, RoutedEventArgs e) => AoFechar?.Invoke();
}
