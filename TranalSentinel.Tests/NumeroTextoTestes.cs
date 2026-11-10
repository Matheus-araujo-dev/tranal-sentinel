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

    [Fact]
    public void NumeroComCasas_LimitaAsCasas()
    {
        // Casa negativa não existe, e o formato "F" do double não passa de 15
        // casas decimais: o pedido fora da faixa é ajustado, não quebrado.
        Assert.Equal("8", NumeroTexto.Numero(8.0, -3));
        Assert.Equal("1,234000000000000", NumeroTexto.Numero(1.234, 20));
    }

    [Fact]
    public void NumeroComCasas_ComValorGrande_SaiPorExtenso()
    {
        // Valor grande sai por extenso — nunca em notação científica ("1E+30"),
        // que ninguém entende na tela.
        Assert.Equal("1000000000000000,00", NumeroTexto.Numero(1e15, 2));
        Assert.Equal("1234567890123456,000", NumeroTexto.Numero(1234567890123456.0, 3));
    }

    [Fact]
    public void Numero_ComZeroNegativo_NaoViraSinal()
    {
        // O arredondamento pode devolver um zero com sinal: "-0" na tela é
        // número errado.
        Assert.Equal("0", NumeroTexto.Numero(-0.0));
        Assert.Equal("0,000", NumeroTexto.Numero(-0.0, 3));
    }
}
