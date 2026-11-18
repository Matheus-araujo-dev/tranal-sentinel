namespace TranalSentinel;

/// <summary>
/// Regras puras dos ajustes de Windows que o app aplica: o que conta como
/// quebrado, que valor é o certo e o que precisa ser reiniciado para valer.
/// </summary>
/// <remarks>
/// <para>
/// Núcleo puro: aqui não existe chamada de Windows, registro ou P/Invoke. O
/// <see cref="WindowsTweaks"/> é quem executa; estas regras só dizem o que
/// fazer, e é por isso que dá para testá-las sem o PC.
/// </para>
/// <para>
/// As duas regras nascem de defeitos medidos no PC do dono em 17/11/2026:
/// a rolagem do navegador parada e o Alt+Tab sem desenhar nada.
/// </para>
/// </remarks>
public static class WindowsTweakRegras
{
    /// <summary>
    /// Linhas que a roda rola por entalhe, no padrão do Windows.
    /// </summary>
    public const uint LinhasPorEntalhePadrao = 3;

    /// <summary>
    /// Caracteres que a roda rola por entalhe na rolagem horizontal, no padrão
    /// do Windows.
    /// </summary>
    public const uint CaracteresPorEntalhePadrao = 3;

    /// <summary>
    /// O Windows entrega a cada aplicativo quantas linhas a roda rola por
    /// entalhe (<c>SPI_GETWHEELSCROLLLINES</c>). O Chromium — Opera, Chrome e
    /// todo aplicativo Electron — multiplica o delta da roda por esse valor:
    /// se o valor for zero, o delta vira zero e a página não rola nada. Os
    /// aplicativos do Windows usam o padrão próprio e continuam rolando, por
    /// isso o defeito aparece só no navegador.
    /// </summary>
    /// <param name="linhas">
    /// Quantas linhas o Windows está informing. <c>null</c> significa que não
    /// deu para ler: nesse caso o defeito não pode ser afirmado.
    /// </param>
    public static bool RolagemDoNavegadorQuebrada(uint? linhas) => linhas == 0;

    /// <summary>
    /// O Windows 11 24H2 em diante removeu a interface clássica do Alt+Tab.
    /// Com o ajuste legado ligado, o Alt+Tab troca de janela e não desenha
    /// nada — é o defeito que o dono viu.
    /// </summary>
    /// <param name="altTabSettings">
    /// O valor do registro <c>AltTabSettings</c>. <c>null</c> significa que
    /// não deu para ler.
    /// </param>
    public static bool AltTabLegadoQuebrado(int? altTabSettings) => altTabSettings == AltTabClassico;

    /// <summary>Valor do switcher moderno do Windows 11 (o que funciona).</summary>
    public const int AltTabModerno = 0;

    /// <summary>
    /// Valor do switcher clássico: existe no registro, mas a interface dele
    /// foi removida da Microsoft no Windows 11 24H2 em diante.
    /// </summary>
    public const int AltTabClassico = 1;

    /// <summary>
    /// O switcher do Alt+Tab é desenhado pelo Explorer: o ajuste só entra em
    /// vigor quando o Explorer renasce. Sem isso, o dono continua sem ver o
    /// quadro de miniaturas e acha que a correção não pegou.
    /// </summary>
    public const bool AltTabPrecisaReiniciarOExplorer = true;
}
