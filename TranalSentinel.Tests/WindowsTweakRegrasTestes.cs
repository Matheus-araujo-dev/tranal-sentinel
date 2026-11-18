using Xunit;

namespace TranalSentinel.Tests;

/// <summary>
/// Regras dos ajustes de Windows que o TRANAL SENTINEL aplica.
/// </summary>
/// <remarks>
/// Os dois ajustes nascem de defeitos medidos no PC do dono em 17/11/2026:
/// a rolagem dos navegadores parada (o Windows informava zero linhas por
/// entalhe) e o Alt+Tab sem desenhar nada (o switcher clássico foi removido
/// da Microsoft no Windows 11 24H2 em diante).
/// </remarks>
public class WindowsTweakRegrasTestes
{
    [Theory]
    [InlineData(0u, true)]
    [InlineData(1u, false)]
    [InlineData(3u, false)]
    [InlineData(120u, false)]
    public void RolagemDoNavegadorQuebrada_SomenteZeroQuebra(uint linhas, bool esperado)
    {
        Assert.Equal(esperado, WindowsTweakRegras.RolagemDoNavegadorQuebrada(linhas));
    }

    [Fact]
    public void RolagemDoNavegadorQuebrada_SemLeituraNaoAfirmaDefeito()
    {
        // Não deu para ler: o app não pode dizer que a rolagem está quebrada.
        Assert.False(WindowsTweakRegras.RolagemDoNavegadorQuebrada(null));
    }

    [Fact]
    public void PadraoDoWindows_ETresLinhasETresCaracteres()
    {
        // O padrão do Windows: é o valor que o dono já tinha no registro.
        Assert.Equal(3u, WindowsTweakRegras.LinhasPorEntalhePadrao);
        Assert.Equal(3u, WindowsTweakRegras.CaracteresPorEntalhePadrao);
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(0, false)]
    [InlineData(2, false)]
    public void AltTabLegadoQuebrado_SomenteOValorClassicoQuebra(int valor, bool esperado)
    {
        Assert.Equal(esperado, WindowsTweakRegras.AltTabLegadoQuebrado(valor));
    }

    [Fact]
    public void AltTabLegadoQuebrado_SemLeituraNaoAfirmaDefeito()
    {
        Assert.False(WindowsTweakRegras.AltTabLegadoQuebrado(null));
    }

    [Fact]
    public void AltTab_OModernoEZeroEOClassicoEUm()
    {
        // O registro do Windows: 0 = switcher do Windows 11, 1 = clássico.
        Assert.Equal(0, WindowsTweakRegras.AltTabModerno);
        Assert.Equal(1, WindowsTweakRegras.AltTabClassico);
    }

    [Fact]
    public void AltTab_ODefeitoDesapareceComOSwitcherModerno()
    {
        // O caminho que o app usa: com o valor moderno, o defeito não existe.
        Assert.True(WindowsTweakRegras.AltTabLegadoQuebrado(WindowsTweakRegras.AltTabClassico));
        Assert.False(WindowsTweakRegras.AltTabLegadoQuebrado(WindowsTweakRegras.AltTabModerno));
    }

    [Fact]
    public void AltTab_OModernoDesapareceOValorQueOAppGrava()
    {
        // O que o app grava tem de ser o valor moderno: é o único que desenha.
        Assert.False(WindowsTweakRegras.AltTabLegadoQuebrado(WindowsTweakRegras.AltTabModerno));
    }

    [Fact]
    public void AltTab_PrecisaDoExplorerRenascendo()
    {
        // O switcher é desenhado pelo Explorer: sem reiniciar, o dono continua
        // sem ver o quadro e acha que a correção não pegou.
        Assert.True(WindowsTweakRegras.AltTabPrecisaReiniciarOExplorer);
    }
}
