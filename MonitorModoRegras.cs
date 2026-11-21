namespace TranalSentinel;

/// <summary>
/// Quando o monitor flutuante vira overlay de jogo — e quando volta a ser
/// painel.
///
/// <para>
/// POR QUE EXISTE: o painel flutuante e o overlay eram dois interruptores
/// mandando na MESMA janela (<see cref="MonitorWindow"/>): ligar um trocava o
/// modo e o outro perdia o controle. Agora há um interruptor só ("Mostrar o
/// monitor") e o modo é decidido aqui: com um jogo em primeiro plano ocupando
/// o monitor inteiro, a janela vira o overlay (clique atravessa, sem arrastar
/// e sem fechar, no canto escolhido); fora do jogo, volta a ser o painel que o
/// dono arrasta e fecha, no lugar onde ele deixou.
/// </para>
///
/// <para>
/// A regra é pura (sem WPF, sem relógio próprio): quem mede é a janela, quem
/// decide é esta classe — e os testes simulam o tempo.
/// </para>
/// </summary>
public sealed class MonitorModoRegras
{
    /// <summary>
    /// Quanto tempo o jogo precisa ficar na frente para o painel virar
    /// overlay. Um alt-tab rápido por cima de um vídeo em tela cheia não
    /// troca o modo.
    /// </summary>
    public static readonly TimeSpan EntrarNoJogo = TimeSpan.FromSeconds(1.5);

    /// <summary>
    /// Quanto tempo sem jogo na frente para o overlay voltar a ser painel.
    /// Maior que <see cref="EntrarNoJogo"/>: a tela de carregamento ou um
    /// menu do jogo que para de desenhar por um instante não pode fazer o
    /// painel piscar no meio da partida.
    /// </summary>
    public static readonly TimeSpan SairDoJogo = TimeSpan.FromSeconds(4);

    /// <summary>Folga, em pixels, para a janela ainda contar como tela cheia.</summary>
    public const int FolgaDaTelaCheia = 2;

    private bool _emJogo;
    private DateTime? _divergeDesde;

    /// <summary>True quando a janela está (ou deve estar) no modo jogo.</summary>
    public bool EmJogo => _emJogo;

    /// <summary>
    /// Parece um jogo? O programa em primeiro plano está entregando quadros
    /// (o ETW viu Present nos últimos 2 s) e a janela dele cobre o monitor
    /// inteiro — tela cheia ou "sem bordas", que é como se joga.
    /// </summary>
    /// <remarks>
    /// Jogo em janela não conta: navegador, player de vídeo e app em Electron
    /// também entregam quadros, e o painel virando overlay por cima de um
    /// vídeo em janela seria o defeito oposto.
    /// </remarks>
    public static bool PareceJogo(bool entregandoQuadros, bool ocupaOMonitor) =>
        entregandoQuadros && ocupaOMonitor;

    /// <summary>
    /// A janela tem exatamente o tamanho do monitor (com
    /// <see cref="FolgaDaTelaCheia"/> de folga, nos dois sentidos)?
    /// </summary>
    /// <remarks>
    /// MEDIDO na revisão: uma janela maximizada comum tem o retângulo 8 px
    /// MAIOR que a área de trabalho em cada lado (a borda invisível do
    /// Windows). Com a barra de tarefas escondida ou num segundo monitor sem
    /// barra, "cobrir o monitor" (só &gt;=) aceitava o navegador maximizado
    /// como jogo. Tela cheia e "sem bordas" têm o retângulo igual ao do
    /// monitor; por isso a comparação é de igualdade, e quem chama ainda
    /// descarta a janela maximizada com barra de título.
    /// </remarks>
    public static bool OcupaOMonitor(
        int janelaEsq, int janelaTopo, int janelaDir, int janelaBase,
        int monitorEsq, int monitorTopo, int monitorDir, int monitorBase) =>
        Math.Abs(janelaEsq - monitorEsq) <= FolgaDaTelaCheia
        && Math.Abs(janelaTopo - monitorTopo) <= FolgaDaTelaCheia
        && Math.Abs(janelaDir - monitorDir) <= FolgaDaTelaCheia
        && Math.Abs(janelaBase - monitorBase) <= FolgaDaTelaCheia;

    /// <summary>
    /// Recebe o que foi visto agora e devolve o modo: true = jogo (overlay),
    /// false = desktop (painel). O modo só troca depois que a observação
    /// contrária durou <see cref="EntrarNoJogo"/> ou <see cref="SairDoJogo"/>.
    /// </summary>
    /// <param name="jogoNaFrente">O resultado de <see cref="PareceJogo"/> agora.</param>
    /// <param name="agora">O instante da observação.</param>
    public bool Observar(bool jogoNaFrente, DateTime agora)
    {
        if (jogoNaFrente == _emJogo)
        {
            _divergeDesde = null;
            return _emJogo;
        }

        _divergeDesde ??= agora;
        var espera = jogoNaFrente ? EntrarNoJogo : SairDoJogo;
        if (agora - _divergeDesde.Value >= espera)
        {
            _emJogo = jogoNaFrente;
            _divergeDesde = null;
        }

        return _emJogo;
    }

    /// <summary>
    /// Volta ao começo (modo desktop). Chamado quando o monitor é ligado: o
    /// que foi visto antes de ele ser desligado não vale mais.
    /// </summary>
    public void Reiniciar() => ComecarEm(false);

    /// <summary>
    /// Começa direto num modo, sem esperar a histerese. Ligar o monitor (ou o
    /// Ctrl + Shift + O) com o jogo já na frente começa como overlay: o painel
    /// arrastável não pode aparecer por cima da partida nem por um instante.
    /// </summary>
    /// <param name="emJogo">True para começar como overlay.</param>
    public void ComecarEm(bool emJogo)
    {
        _emJogo = emJogo;
        _divergeDesde = null;
    }
}
