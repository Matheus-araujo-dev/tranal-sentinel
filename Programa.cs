namespace TranalSentinel;

/// <summary>
/// A porta de entrada do executável.
///
/// <para>
/// Existe por causa do processo de coleta de sensores
/// (<see cref="SensorWorker"/>): ele é o mesmo executável, mas não abre
/// janela nenhuma. Quando ele subia pelo <see cref="App"/>, o WPF inteiro
/// era carregado à toa (o <c>App.xaml</c> monta o tema com todos os estilos
/// na construção) — e esse processo fica aberto o tempo todo. Aqui o
/// coletor sobe antes do WPF e nunca o carrega.
/// </para>
/// </summary>
internal static class Programa
{
    /// <summary>
    /// Sobe o coletor de sensores sem WPF, ou o app normal (janela e linha de
    /// comando) pelo <see cref="App"/>.
    /// </summary>
    [STAThread]
    public static int Main(string[] args)
    {
        if (SensorWorker.TentarRodar(args)) return 0;
        return RodarApp();
    }

    /// <summary>
    /// O app com WPF. Fica num método à parte, que não pode ser embutido:
    /// o JIT resolve os tipos do método inteiro ao compilá-lo, e com o
    /// <see cref="App"/> dentro do <see cref="Main"/> o WPF era carregado
    /// também no coletor (medido em 20/11/2026: o PresentationFramework
    /// continuava no processo).
    /// </summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static int RodarApp()
    {
        var app = new App();
        app.InitializeComponent();
        return app.Run();
    }
}
