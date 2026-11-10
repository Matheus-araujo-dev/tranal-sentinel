using System.Globalization;
using Xunit;

namespace TranalSentinel.Tests;

/// <summary>
/// Testes do formatador de números da casa.
/// </summary>
public class NumeroTextoTestes
{
    [Fact]
    public void NumeroComCasas_GaranteAsCasas()
    {
        Assert.Equal("8,0", NumeroTexto.Numero(8.0, 1));
        Assert.Equal("1,234", NumeroTexto.Numero(1.234, 3));
    }

    [Fact]
    public void NumeroComCasas_Arredonda()
    {
        Assert.Equal("8,0", NumeroTexto.Numero(8.04, 1));
        Assert.Equal("4,45", NumeroTexto.Numero(4.451, 2));
    }

    [Fact]
    public void NumeroComCasas_NaoDependeDaCultura()
    {
        var cultura = CultureInfo.CurrentCulture;

        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("en-US");

            Assert.Equal("1,234", NumeroTexto.Numero(1.234, 3));
        }
        finally
        {
            CultureInfo.CurrentCulture = cultura;
        }
    }

    [Fact]
    public void NumeroComCasas_ComValorNaoFinito_ViraEmDash()
    {
        Assert.Equal("—", NumeroTexto.Numero(double.NaN, 1));
        Assert.Equal("—", NumeroTexto.Numero(double.PositiveInfinity, 3));
    }
}
