using System;
using Xunit;

namespace TranalSentinel.Tests;

/// <summary>
/// Testes dos textos da aba "Uso": como o tempo, o percentual e a
/// temperatura são escritos na tela.
///
/// <para>
/// O texto é sempre no padrão brasileiro, não no da máquina: por isso o
/// teste não depende da configuração regional de quem roda.
/// </para>
/// </summary>
public class UsoTextoTestes
{
    // ------------------------------------------------------------------ duracao

    [Theory]
    [InlineData(0, 0, 0, "0 s")]
    [InlineData(-5, 0, 0, "0 s")]      // relógio que andou para trás
    [InlineData(0, 0, 30, "30 s")]
    [InlineData(0, 0, 59, "59 s")]
    [InlineData(0, 1, 0, "1 min")]
    [InlineData(0, 45, 0, "45 min")]
    [InlineData(0, 59, 59, "59 min")]
    [InlineData(1, 0, 0, "1 h")]
    [InlineData(2, 15, 0, "2 h 15 min")]
    [InlineData(10, 0, 0, "10 h")]
    [InlineData(25, 0, 0, "25 h")]
    [InlineData(25, 30, 0, "25 h 30 min")]
    public void Duracao_escreve_no_padrao_brasileiro(int horas, int minutos, int segundos, string esperado)
    {
        var duracao = new TimeSpan(horas, minutos, segundos);

        Assert.Equal(esperado, UsoTexto.Duracao(duracao));
    }

    [Fact]
    public void Duracao_de_uma_duracao_negativa_nao_vira_texto_negativo()
    {
        Assert.Equal("0 s", UsoTexto.Duracao(TimeSpan.FromMinutes(-5)));
    }

    // ------------------------------------------------------------------ percentual

    [Theory]
    [InlineData(42.0, "42 %")]
    [InlineData(42.44, "42,4 %")]
    [InlineData(0.0, "0 %")]
    [InlineData(100.0, "100 %")]
    public void Percentual_escreve_com_virgula_decimal(double percentual, string esperado)
    {
        Assert.Equal(esperado, UsoTexto.Percentual(percentual));
    }

    [Fact]
    public void Percentual_quebrado_nao_vira_numero_na_tela()
    {
        Assert.Equal("—", UsoTexto.Percentual(double.NaN));
        Assert.Equal("—", UsoTexto.Percentual(double.PositiveInfinity));
        Assert.Equal("—", UsoTexto.Percentual(double.NegativeInfinity));
    }

    // ------------------------------------------------------------------ temperatura

    [Theory]
    [InlineData(81.5f, "81,5 °C")]
    [InlineData(80f, "80 °C")]
    [InlineData(100f, "100 °C")]
    public void Temperatura_escreve_com_virgula_decimal(float celsius, string esperado)
    {
        Assert.Equal(esperado, UsoTexto.Temperatura(celsius));
    }

    [Fact]
    public void Temperatura_sem_valor_nao_vira_zero_graus()
    {
        Assert.Equal("—", UsoTexto.Temperatura(null));
        Assert.Equal("—", UsoTexto.Temperatura(float.NaN));
        Assert.Equal("—", UsoTexto.Temperatura(float.PositiveInfinity));
    }

    // ------------------------------------------------------------------ dia e hora

    [Fact]
    public void RotuloDia_mostra_hoje_e_ontem_pelo_nome()
    {
        var hoje = new DateTime(2026, 11, 6);

        Assert.Equal("hoje", UsoTexto.RotuloDia(hoje, hoje));
        Assert.Equal("ontem", UsoTexto.RotuloDia(hoje.AddDays(-1), hoje));
        Assert.Equal("04/11/2026", UsoTexto.RotuloDia(hoje.AddDays(-2), hoje));
    }

    [Fact]
    public void RotuloDia_ignora_a_hora_do_dia()
    {
        var hoje = new DateTime(2026, 11, 6);

        Assert.Equal("hoje", UsoTexto.RotuloDia(hoje.AddHours(23).AddMinutes(59), hoje));
    }

    [Fact]
    public void Hora_escreve_em_24_horas()
    {
        Assert.Equal("14:31", UsoTexto.Hora(new DateTime(2026, 11, 6, 14, 31, 22)));
        Assert.Equal("00:05", UsoTexto.Hora(new DateTime(2026, 11, 6, 0, 5, 0)));
    }
}
