using System.Globalization;
using Xunit;

namespace TranalSentinel.Tests;

/// <summary>
/// Testes do formatador de unidades dos sensores.
///
/// <para>
/// O mesmo número tem que sair do mesmo jeito em PCs de culturas
/// diferentes: vírgula decimal e ponto de milhar, sempre.
/// </para>
/// </summary>
public class SensorTextoTestes
{
    [Fact]
    public void Temperatura_ComValor_ArredondaParaUmaCasa()
    {
        Assert.Equal("50,4 °C", SensorTexto.Temperatura(50.35));
        Assert.Equal("50 °C", SensorTexto.Temperatura(50.0));
    }

    [Fact]
    public void Porcentual_ComValor_ArredondaParaInteiro()
    {
        Assert.Equal("47%", SensorTexto.Porcentual(47.4));
        Assert.Equal("48%", SensorTexto.Porcentual(47.6));
    }

    [Fact]
    public void Gigabytes_ComValor_ArredondaParaUmaCasa()
    {
        Assert.Equal("8,0 GB", SensorTexto.Gigabytes(8.04));
    }

    [Fact]
    public void Mhz_ComValor_UsaPontoDeMilhar()
    {
        Assert.Equal("2.790 MHz", SensorTexto.Mhz(2790));
    }

    [Fact]
    public void Rpm_ComValor_UsaPontoDeMilhar()
    {
        Assert.Equal("3.409 RPM", SensorTexto.Rpm(3409.4));
    }

    [Fact]
    public void ValorAusente_ViraEmDash()
    {
        Assert.Equal("—", SensorTexto.Temperatura(null));
        Assert.Equal("—", SensorTexto.Porcentual(null));
        Assert.Equal("—", SensorTexto.Gigabytes(null));
        Assert.Equal("—", SensorTexto.Mhz(null));
        Assert.Equal("—", SensorTexto.Watts(null));
        Assert.Equal("—", SensorTexto.Volts(null));
        Assert.Equal("—", SensorTexto.Rpm(null));
    }

    [Fact]
    public void ValorNaoFinito_ViraEmDash()
    {
        // O sensor pode devolver infinito ou NaN. O número texto cuida
        // disso quando o valor vira texto decimal, mas a conversão para
        // inteiro não: sem a guarda, o infinito viraria um número
        // gigante de 19 dígitos na tela.
        Assert.Equal("—", SensorTexto.Temperatura(double.NaN));
        Assert.Equal("—", SensorTexto.Porcentual(double.NaN));
        Assert.Equal("—", SensorTexto.Porcentual(double.PositiveInfinity));
        Assert.Equal("—", SensorTexto.Mhz(double.PositiveInfinity));
        Assert.Equal("—", SensorTexto.Watts(double.NaN));
        Assert.Equal("—", SensorTexto.Volts(double.NaN));
        Assert.Equal("—", SensorTexto.Rpm(double.PositiveInfinity));
    }

    [Fact]
    public void OsNumerosNaoDependemDaCulturaDoWindows()
    {
        var cultura = CultureInfo.CurrentCulture;

        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("en-US");

            Assert.Equal("50,4 °C", SensorTexto.Temperatura(50.35));
            Assert.Equal("8,0 GB", SensorTexto.Gigabytes(8.04));
            Assert.Equal("3.409 RPM", SensorTexto.Rpm(3409.4));
        }
        finally
        {
            CultureInfo.CurrentCulture = cultura;
        }
    }

    [Fact]
    public void Gigahertz_ComValor_UsaDuasCasas()
    {
        Assert.Equal("4,45 GHz", SensorTexto.Gigahertz(4.451));
    }

    [Fact]
    public void ValorQueNaoCabeEmUmInteiro_ViraEmDash()
    {
        // Um sensor que devolvesse 1e30 faria o cast saturar em
        // long.MaxValue e a tela mostraria 19 dígitos. Número errado é
        // pior que número ausente.
        Assert.Equal("—", SensorTexto.Rpm(1e30));
        Assert.Equal("—", SensorTexto.Porcentual(1e30));
        Assert.Equal("—", SensorTexto.Mhz(1e30));
        Assert.Equal("—", SensorTexto.Gigabytes(1e30));
        Assert.Equal("—", SensorTexto.Volts(1e30));
        Assert.Equal("—", SensorTexto.Gigahertz(1e30));
    }
}
