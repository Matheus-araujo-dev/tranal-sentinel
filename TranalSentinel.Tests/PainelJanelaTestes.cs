using Xunit;

namespace TranalSentinel.Tests;

/// <summary>
/// Testes da parte pura da janela do painel.
///
/// <para>
/// A janela do WPF não entra na suíte (ela exige uma thread com STA). A
/// pergunta "o painel ainda está em algum monitor?" é aritmética pura, e é
/// ela que os testes cobrem.
/// </para>
/// </summary>
public class PainelJanelaTestes
{
    [Fact]
    public void PainelDentroDaTela_Fica()
    {
        Assert.True(PainelWindow.DentroDaTela(100, 100, 260, 120, 0, 0, 1920, 1080));
    }

    [Fact]
    public void PainelForaDaTela_NaoFica()
    {
        Assert.False(PainelWindow.DentroDaTela(5000, 100, 260, 120, 0, 0, 1920, 1080));
    }

    [Fact]
    public void PainelEncostadoNaBordaDaTela_Fica()
    {
        // A borda conta: um painel que termina exatamente na última coluna
        // ainda está na tela.
        Assert.True(PainelWindow.DentroDaTela(1660, 960, 260, 120, 0, 0, 1920, 1080));
    }

    [Fact]
    public void PainelTotalmenteFora_NaoFica()
    {
        Assert.False(PainelWindow.DentroDaTela(-400, -300, 260, 120, 0, 0, 1920, 1080));
    }
}
