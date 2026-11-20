using Xunit;

namespace TranalSentinel.Tests;

/// <summary>
/// Testes da baseline personalizada: o cálculo de média e desvio padrão
/// e a detecção de desvios.
/// </summary>
public class BaselineRegrasTestes
{
    private static AgregadoDiario Agreg(float? cpuMedia = 60, float? gpuMedia = null, float? ramMedia = null) =>
        new(DateTime.Today, null, null, cpuMedia, null, null, null,
            null, null, gpuMedia, null, null, null, null, null, ramMedia, 100);

    [Fact]
    public void SemAgregados_BaselineVazia()
    {
        var r = BaselineRegras.Calcular(Array.Empty<AgregadoDiario>());
        Assert.Empty(r);
    }

    [Fact]
    public void ComDadosSuficientes_CalculaMediaEDesvio()
    {
        var agregados = new[]
        {
            Agreg(cpuMedia: 60), Agreg(cpuMedia: 62), Agreg(cpuMedia: 58),
            Agreg(cpuMedia: 61), Agreg(cpuMedia: 59)
        };

        var baseline = BaselineRegras.Calcular(agregados);
        var cpu = baseline.FirstOrDefault(b => b.Chave == "cpu-temp");

        Assert.NotNull(cpu);
        Assert.Equal(5, cpu.DiasComDado);
        Assert.Equal(60f, cpu.Media, 0.1f);
        Assert.True(cpu.DesvioPadrao > 0);
    }

    [Fact]
    public void MenosDeDiasMinimos_NaoDetectaDesvio()
    {
        var agregados = new[] { Agreg(cpuMedia: 60), Agreg(cpuMedia: 62) };
        var baseline = BaselineRegras.Calcular(agregados);
        var cpu = baseline.FirstOrDefault(b => b.Chave == "cpu-temp");

        Assert.NotNull(cpu);
        Assert.Equal(2, cpu.DiasComDado);

        // Com menos de DiasMinimos, comparar não devolve desvios
        var contexto = new ContextoSensores { TemperaturaCpu = 90f };
        var desvios = BaselineRegras.Comparar(baseline, contexto);
        Assert.Empty(desvios);
    }

    [Fact]
    public void ValorMuitoAcima_DetectaDesvio()
    {
        var agregados = new List<AgregadoDiario>();
        for (int i = 0; i < 10; i++)
            agregados.Add(Agreg(cpuMedia: 60));

        var baseline = BaselineRegras.Calcular(agregados);

        // A CPU está 30 graus acima da média: vários desvios padrão
        var contexto = new ContextoSensores { TemperaturaCpu = 90f };
        var desvios = BaselineRegras.Comparar(baseline, contexto);

        // Desvio padrão = 0 com valores iguais → DesvioPadrao < 0.01 → sem desvio
        // O teste com valores iguais não gera desvio (o desvio padrão é zero)
        // Vamos testar com valores variados
        agregados.Clear();
        for (int i = 0; i < 10; i++)
            agregados.Add(Agreg(cpuMedia: 58 + i % 4)); // 58, 59, 60, 61, ...

        baseline = BaselineRegras.Calcular(agregados);
        desvios = BaselineRegras.Comparar(baseline, contexto);
        Assert.NotEmpty(desvios);
        Assert.Equal("cpu-temp", desvios[0].Chave);
    }

    [Fact]
    public void ValorNormal_SemDesvio()
    {
        var agregados = new List<AgregadoDiario>();
        for (int i = 0; i < 10; i++)
            agregados.Add(Agreg(cpuMedia: 58 + i % 4));

        var baseline = BaselineRegras.Calcular(agregados);
        var contexto = new ContextoSensores { TemperaturaCpu = 60f };
        var desvios = BaselineRegras.Comparar(baseline, contexto);

        Assert.Empty(desvios);
    }

    [Fact]
    public void TextoEstado_AprendizadoAntesDeDiasMinimos()
    {
        var baseline = new List<BaselineSensor>();
        string texto = BaselineRegras.TextoEstado(baseline, 2);
        Assert.Contains("Aprendendo", texto);
        Assert.Contains("2/3", texto);
    }

    [Fact]
    public void TextoEstado_BaselinePronta()
    {
        var baseline = new List<BaselineSensor>();
        string texto = BaselineRegras.TextoEstado(baseline, 5);
        Assert.Equal("Baseline pronta", texto);
    }
}
