using Xunit;

namespace TranalSentinel.Tests;

/// <summary>
/// Testes das tendências de longo prazo: regressão linear sobre os
/// agregados diários.
/// </summary>
public class TendenciaRegrasTestes
{
    private static AgregadoDiario Agreg(int dia, float? cpuMedia) =>
        new(new DateTime(2026, 11, 1).AddDays(dia), null, null, cpuMedia, null, null, null,
            null, null, null, null, null, null, null, null, null, 100);

    [Fact]
    public void MenosDeDiasMinimos_SemTendencia()
    {
        var agregados = new[] { Agreg(0, 60), Agreg(1, 62) };
        var t = TendenciaRegras.Calcular(agregados);
        Assert.Empty(t);
    }

    [Fact]
    public void TendenciaSubindo_CpuSubindoLineadamente()
    {
        var agregados = new List<AgregadoDiario>();
        for (int i = 0; i < 14; i++)
            agregados.Add(Agreg(i, 50 + i * 2)); // subindo 2°C/dia

        var t = TendenciaRegras.Calcular(agregados);
        var cpu = t.FirstOrDefault(x => x.Chave == "cpu-temp");

        Assert.NotNull(cpu);
        Assert.Equal(Direcao.Subindo, cpu.Direcao);
        Assert.True(cpu.InclinacaoPorDia > 0);
        Assert.True(cpu.R2 > 0.9f);
    }

    [Fact]
    public void TendenciaDescendo_CpuDescendoLineadamente()
    {
        var agregados = new List<AgregadoDiario>();
        for (int i = 0; i < 14; i++)
            agregados.Add(Agreg(i, 80 - i * 2)); // descendo 2°C/dia

        var t = TendenciaRegras.Calcular(agregados);
        var cpu = t.FirstOrDefault(x => x.Chave == "cpu-temp");

        Assert.NotNull(cpu);
        Assert.Equal(Direcao.Descendo, cpu.Direcao);
        Assert.True(cpu.InclinacaoPorDia < 0);
    }

    [Fact]
    public void ValoresEstaveis_SemTendenciaSignificativa()
    {
        var agregados = new List<AgregadoDiario>();
        for (int i = 0; i < 14; i++)
            agregados.Add(Agreg(i, 60f)); // constante

        var t = TendenciaRegras.Calcular(agregados);
        // Com valores constantes: inclinação = 0, R² indefinido (syy = 0)
        Assert.Empty(t);
    }

    [Fact]
    public void TextoTendencia_FormatadoCorretamente()
    {
        var t = new Tendencia("cpu-temp", "CPU", "°C", 1.5f, 0.95f, 14, Direcao.Subindo);
        string texto = TendenciaRegras.TextoTendencia(t);

        Assert.Contains("subindo", texto);
        Assert.Contains("°C/semana", texto);
        Assert.Contains("14 dias", texto);
    }

    [Fact]
    public void TextoResumo_ContaDirecoes()
    {
        var tendencias = new Tendencia[]
        {
            new("cpu-temp", "CPU", "°C", 1.5f, 0.9f, 14, Direcao.Subindo),
            new("gpu-temp", "GPU", "°C", -1.0f, 0.8f, 14, Direcao.Descendo),
        };

        string texto = TendenciaRegras.TextoResumo(tendencias);
        Assert.Contains("1 subindo", texto);
        Assert.Contains("1 descendo", texto);
    }

    [Fact]
    public void TextoResumo_SemTendencia()
    {
        string texto = TendenciaRegras.TextoResumo(Array.Empty<Tendencia>());
        Assert.Equal("Sem tendências significativas", texto);
    }
}
