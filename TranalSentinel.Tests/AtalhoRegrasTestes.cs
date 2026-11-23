using Xunit;

namespace TranalSentinel.Tests;

/// <summary>
/// O atalho do monitor, escolhido pelo dono: o padrão, o que serve e como a
/// tela o escreve.
/// </summary>
public class AtalhoRegrasTestes
{
    [Fact]
    public void Padrao_EhCtrlShiftO()
    {
        Assert.Equal("Ctrl + Shift + O", AtalhoRegras.Texto(AtalhoRegras.Padrao));
    }

    [Fact]
    public void NadaGravado_ValeOPadrao()
    {
        Assert.Equal(AtalhoRegras.Padrao, AtalhoRegras.Carregar(null, null));
        Assert.Equal(AtalhoRegras.Padrao, AtalhoRegras.Carregar(AtalhoRegras.Ctrl, null));
    }

    [Fact]
    public void GravadoValido_EhOGravado()
    {
        var a = AtalhoRegras.Carregar(AtalhoRegras.Ctrl | AtalhoRegras.Alt, 0x7B);
        Assert.Equal("Ctrl + Alt + F12", AtalhoRegras.Texto(a));
    }

    [Fact]
    public void GravadoInvalido_VoltaParaOPadrao()
    {
        // Editado à mão no settings.json: Shift + O dispararia ao digitar.
        Assert.Equal(AtalhoRegras.Padrao, AtalhoRegras.Carregar(AtalhoRegras.Shift, 0x4F));
    }

    [Theory]
    [InlineData(0u, 0x4Fu)]                     // só a letra
    [InlineData(AtalhoRegras.Shift, 0x4Fu)]     // Shift + letra
    public void SemCtrlAltOuWin_NaoServe(uint mods, uint tecla)
    {
        Assert.NotNull(AtalhoRegras.Erro(new Atalho(mods, tecla)));
    }

    [Theory]
    [InlineData(0x20u)]   // espaço
    [InlineData(0x10u)]   // o próprio Shift
    [InlineData(0x1Bu)]   // Esc
    public void TeclaQueNaoEhLetraNumeroOuF_NaoServe(uint tecla)
    {
        Assert.NotNull(AtalhoRegras.Erro(new Atalho(AtalhoRegras.Ctrl, tecla)));
    }

    [Theory]
    [InlineData(AtalhoRegras.Ctrl | AtalhoRegras.Shift, 0x4Du, "Ctrl + Shift + M")]
    [InlineData(AtalhoRegras.Alt, 0x31u, "Alt + 1")]
    [InlineData(AtalhoRegras.Win | AtalhoRegras.Shift, 0x87u, "Shift + Win + F24")]
    public void CombinacaoQueServe_EhEscritaNaOrdemCtrlAltShiftWin(uint mods, uint tecla, string texto)
    {
        var a = new Atalho(mods, tecla);
        Assert.Null(AtalhoRegras.Erro(a));
        Assert.Equal(texto, AtalhoRegras.Texto(a));
    }

    [Fact]
    public void ModificadorDesconhecido_NaoServe()
    {
        Assert.NotNull(AtalhoRegras.Erro(new Atalho(AtalhoRegras.Ctrl | AtalhoRegras.SemRepeticao, 0x4F)));
    }
}
