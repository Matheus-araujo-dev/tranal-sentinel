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

    [Fact]
    public void PainelEncostadoForaDaTela_NaoFica()
    {
        // A borda invisível conta como fora: o painel termina exatamente
        // onde a tela começa (à esquerda) e começa exatamente onde ela
        // termina (à direita). Com ">" virando ">=", ele passaria a
        // contar como dentro — e o painel voltaria para um monitor que
        // não é mais o dele.
        Assert.False(PainelWindow.DentroDaTela(-260, 100, 260, 120, 0, 0, 1920, 1080));
        Assert.False(PainelWindow.DentroDaTela(1920, 100, 260, 120, 0, 0, 1920, 1080));
        Assert.False(PainelWindow.DentroDaTela(100, -120, 260, 120, 0, 0, 1920, 1080));
        Assert.False(PainelWindow.DentroDaTela(100, 1080, 260, 120, 0, 0, 1920, 1080));
    }

    [Fact]
    public void PosicaoSemValor_NaoPodeSerGravada()
    {
        // Uma janela que ainda não foi posicionada tem Left/Top sem valor:
        // gravar isso no arquivo apagaria a posição que o dono tinha
        // deixado. A regra é pura, e por isso dá para testar aqui.
        Assert.False(PainelRegras.PosicaoValida(double.NaN, 100));
        Assert.False(PainelRegras.PosicaoValida(100, double.NaN));
        Assert.False(PainelRegras.PosicaoValida(double.NaN, double.NaN));
    }

    [Fact]
    public void PosicaoComNumero_PodeSerGravada()
    {
        Assert.True(PainelRegras.PosicaoValida(2354, 12));
        Assert.True(PainelRegras.PosicaoValida(0, 0));
        Assert.True(PainelRegras.PosicaoValida(-400, -300));
    }
}
