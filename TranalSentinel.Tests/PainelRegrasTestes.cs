using Xunit;

namespace TranalSentinel.Tests;

/// <summary>
/// Testes das regras do monitor no desktop: a ventoinha que ele mostra, o
/// canto escolhido e a posição guardada. O que não deu para ler vira "—" —
/// nunca zero, nunca o valor de antes.
/// </summary>
public class PainelRegrasTestes
{
    [Fact]
    public void EscolherVentoinha_ComVariasAMaiorRpmVence()
    {
        // A vencedora entra por ultimo: se a regra voltasse a ser "a
        // primeira que esta dando leitura", este teste quebraria.
        var escolhida = PainelRegras.EscolherVentoinha(new[]
        {
            new PainelVentoinha("GPU Fan", 1407),
            new PainelVentoinha("Fan #2", 0),
            new PainelVentoinha("Fan #1", 3409),
        });

        Assert.Equal("Fan #1", escolhida!.Nome);
    }

    [Fact]
    public void EscolherVentoinha_ComEmpateEscolheOMesmoNomeSempre()
    {
        var escolhidas = PainelRegras.EscolherVentoinha(new[]
        {
            new PainelVentoinha("Fan #3", 2000),
            new PainelVentoinha("Fan #1", 2000),
        });

        Assert.Equal("Fan #1", escolhidas!.Nome);
    }

    [Fact]
    public void EscolherVentoinha_SemNenhumaLendo_EscolheUmaComRpmAusente()
    {
        // A primeira por ordem alfabetica entra por ultimo: se o desempate
        // voltasse a ser a ordem de chegada dos sensores, este teste
        // quebraria.
        var escolhida = PainelRegras.EscolherVentoinha(new[]
        {
            new PainelVentoinha("Fan #3", null),
            new PainelVentoinha("Fan #2", null),
        });

        Assert.Equal("Fan #2", escolhida!.Nome);
        Assert.Null(escolhida.Rpm);
    }

    [Fact]
    public void EscolherVentoinha_SemNenhumaVentoinha_DevolveNulo()
    {
        Assert.Null(PainelRegras.EscolherVentoinha(Array.Empty<PainelVentoinha>()));
    }

    [Fact]
    public void LinhaDaVentoinha_SemLeitura_NaoMostraORpmVelho()
    {
        // A ventoinha que parou de dar leitura mostra o nome e um travessão:
        // o RPM velho não pode aparecer como se fosse o de agora.
        Assert.Equal("Fan #1 · —", PainelRegras.ValorDaVentoinha(new PainelVentoinha("Fan #1", null)));
    }

    [Fact]
    public void LinhaDaVentoinha_ComLeitura_MostraONomeEORpm()
    {
        Assert.Equal("Fan #1 · 3.409 RPM", PainelRegras.ValorDaVentoinha(new PainelVentoinha("Fan #1", 3409)));
    }

    [Fact]
    public void LinhaDaVentoinha_SemNomeMostraSoORpm()
    {
        Assert.Equal("3.409 RPM", PainelRegras.ValorDaVentoinha(new PainelVentoinha("", 3409)));
    }

    [Fact]
    public void LinhaDaVentoinha_SemVentoinhaNoApp_ViraEmDash()
    {
        Assert.Equal("—", PainelRegras.ValorDaVentoinha(null));
    }

    [Theory]
    [InlineData("TL", 12, 12)]
    [InlineData("TR", 1920 - 260 - 12, 12)]
    [InlineData("BL", 12, 1040 - 120 - 12)]
    [InlineData("BR", 1920 - 260 - 12, 1040 - 120 - 12)]
    public void PosicaoNoCanto_ColaNoCantoEscolhidoDaAreaDeTrabalho(string canto, double x, double y)
    {
        // A área de trabalho termina em 1040: a barra de tarefas (40 px) não
        // pode cobrir o monitor colado embaixo.
        var (px, py) = PainelRegras.PosicaoNoCanto(canto, 260, 120, 0, 0, 1920, 1040);
        Assert.Equal(x, px);
        Assert.Equal(y, py);
    }

    [Fact]
    public void PosicaoNoCanto_CantoDesconhecido_ValeComoSuperiorEsquerdo()
    {
        Assert.Equal((12d, 12d), PainelRegras.PosicaoNoCanto(null, 260, 120, 0, 0, 1920, 1040));
        Assert.Equal((12d, 12d), PainelRegras.PosicaoNoCanto("xx", 260, 120, 0, 0, 1920, 1040));
    }

    [Fact]
    public void PosicaoNoCanto_SegueOTamanhoDoMonitor()
    {
        // Maior (o tamanho "Grande"), ele continua colado no canto direito, e
        // não sai da tela.
        var (x, _) = PainelRegras.PosicaoNoCanto("TR", 338, 156, 0, 0, 1920, 1040);
        Assert.Equal(1920 - 338 - 12, x);
    }

    [Fact]
    public void PainelDentroDaTela_Fica()
    {
        Assert.True(PainelRegras.DentroDaTela(100, 100, 260, 120, 0, 0, 1920, 1080));
    }

    [Fact]
    public void PainelForaDaTela_NaoFica()
    {
        Assert.False(PainelRegras.DentroDaTela(5000, 100, 260, 120, 0, 0, 1920, 1080));
    }

    [Fact]
    public void PainelEncostadoNaBordaDaTela_Fica()
    {
        // A borda conta: um painel que termina exatamente na última coluna
        // ainda está na tela.
        Assert.True(PainelRegras.DentroDaTela(1660, 960, 260, 120, 0, 0, 1920, 1080));
    }

    [Fact]
    public void PainelTotalmenteFora_NaoFica()
    {
        Assert.False(PainelRegras.DentroDaTela(-400, -300, 260, 120, 0, 0, 1920, 1080));
    }

    [Fact]
    public void PainelEncostadoForaDaTela_NaoFica()
    {
        // A borda invisível conta como fora: o painel termina exatamente
        // onde a tela começa (à esquerda) e começa exatamente onde ela
        // termina (à direita). Com ">" virando ">=", ele passaria a
        // contar como dentro — e o painel voltaria para um monitor que
        // não é mais o dele.
        Assert.False(PainelRegras.DentroDaTela(-260, 100, 260, 120, 0, 0, 1920, 1080));
        Assert.False(PainelRegras.DentroDaTela(1920, 100, 260, 120, 0, 0, 1920, 1080));
        Assert.False(PainelRegras.DentroDaTela(100, -120, 260, 120, 0, 0, 1920, 1080));
        Assert.False(PainelRegras.DentroDaTela(100, 1080, 260, 120, 0, 0, 1920, 1080));
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
