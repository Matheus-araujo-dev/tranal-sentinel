using Xunit;

namespace TranalSentinel.Tests;

/// <summary>
/// Testes das linhas do painel flutuante.
///
/// <para>
/// Quatro blocos, sempre, na mesma ordem: CPU, GPU, RAM e ventoinha. O que
/// não deu para ler vira "—" — nunca zero, nunca o valor de antes.
/// </para>
/// </summary>
public class PainelRegrasTestes
{
    private static PainelRetrato Retrato(
        double? tempCpu = 50.35, double? cargaCpu = 47,
        double? tempGpu = 44, double? cargaGpu = 12,
        double? ram = 68,
        IReadOnlyList<PainelVentoinha>? ventoinhas = null) =>
        new(tempCpu, cargaCpu, tempGpu, cargaGpu, ram,
            ventoinhas ?? new[] { new PainelVentoinha("Fan #1", 3409) });

    [Fact]
    public void Linhas_SempreComOsQuatroBlocosNaOrdem()
    {
        var linhas = PainelRegras.Linhas(Retrato());

        Assert.Equal(4, linhas.Count);
        Assert.Equal("CPU", linhas[0].Rotulo);
        Assert.Equal("GPU", linhas[1].Rotulo);
        Assert.Equal("RAM", linhas[2].Rotulo);
        Assert.Equal("VENT", linhas[3].Rotulo);
    }

    [Fact]
    public void LinhaDaCpu_ComTemperaturaECarga_MostraOsDois()
    {
        var cpu = PainelRegras.Linhas(Retrato())[0];

        Assert.Equal("50,4 °C · 47%", cpu.Valor);
    }

    [Fact]
    public void LinhaDaCpu_ComSoTemperatura_MostraSoEla()
    {
        var cpu = PainelRegras.Linhas(Retrato(cargaCpu: null))[0];

        Assert.Equal("50,4 °C", cpu.Valor);
    }

    [Fact]
    public void LinhaDaCpu_SemValor_NaoMostraZero()
    {
        var cpu = PainelRegras.Linhas(Retrato(tempCpu: null, cargaCpu: null))[0];

        Assert.Equal("—", cpu.Valor);
    }

    [Fact]
    public void LinhaDaGpu_ComTemperaturaECarga_MostraOsDois()
    {
        var gpu = PainelRegras.Linhas(Retrato())[1];

        Assert.Equal("44 °C · 12%", gpu.Valor);
    }

    [Fact]
    public void LinhaDaRam_ComValor_MostraOPorcentual()
    {
        var ram = PainelRegras.Linhas(Retrato())[2];

        Assert.Equal("68%", ram.Valor);
    }

    [Fact]
    public void LinhaDaRam_SemValor_ViraEmDash()
    {
        var ram = PainelRegras.Linhas(Retrato(ram: null))[2];

        Assert.Equal("—", ram.Valor);
    }

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
        var vent = PainelRegras.Linhas(Retrato(ventoinhas: new[]
        {
            new PainelVentoinha("Fan #1", null),
        }))[3];

        // A ventoinha que parou de dar leitura mostra o nome e um travessão:
        // o RPM velho não pode aparecer como se fosse o de agora.
        Assert.Equal("Fan #1 · —", vent.Valor);
    }

    [Fact]
    public void LinhaDaVentoinha_ComLeitura_MostraONomeEORpm()
    {
        var vent = PainelRegras.Linhas(Retrato())[3];

        Assert.Equal("Fan #1 · 3.409 RPM", vent.Valor);
    }

    [Fact]
    public void LinhaDaVentoinha_SemNomeMostraSoORpm()
    {
        var vent = PainelRegras.Linhas(Retrato(ventoinhas: new[]
        {
            new PainelVentoinha("", 3409),
        }))[3];

        Assert.Equal("3.409 RPM", vent.Valor);
    }

    [Fact]
    public void LinhaDaVentoinha_SemVentoinhaNoApp_ViraEmDash()
    {
        var vent = PainelRegras.Linhas(Retrato(ventoinhas: Array.Empty<PainelVentoinha>()))[3];

        Assert.Equal("—", vent.Valor);
    }
}
