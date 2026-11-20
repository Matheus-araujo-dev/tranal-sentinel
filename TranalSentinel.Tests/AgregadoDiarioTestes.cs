using Xunit;

namespace TranalSentinel.Tests;

/// <summary>
/// Testes do agregado diário: o cálculo de mínimo, máximo e média
/// de cada sensor a partir das amostras brutas.
/// </summary>
public class AgregadoDiarioTestes
{
    private static readonly DateTime Dia = new(2026, 11, 20);

    private static AmostraSensor Amostra(float? cpuTemp = null, float? cpuCarga = null,
        float? gpuTemp = null, float? gpuCarga = null, float? ramUso = null) =>
        new(Dia.AddHours(12), cpuTemp, cpuCarga, null, null,
            gpuTemp, gpuCarga, null, null, ramUso, null, null);

    [Fact]
    public void SemAmostras_DevolveNulo()
    {
        var r = AgregadoDiarioRegras.Calcular(Dia, Array.Empty<AmostraSensor>());

        Assert.Equal(0, r.Amostras);
        Assert.Null(r.CpuTempMin);
        Assert.Null(r.CpuTempMax);
        Assert.Null(r.CpuTempMedia);
    }

    [Fact]
    public void UmaAmostra_MinMaxMediaIguais()
    {
        var amostras = new[] { Amostra(cpuTemp: 60f) };
        var r = AgregadoDiarioRegras.Calcular(Dia, amostras);

        Assert.Equal(1, r.Amostras);
        Assert.Equal(60f, r.CpuTempMin);
        Assert.Equal(60f, r.CpuTempMax);
        Assert.Equal(60f, r.CpuTempMedia);
    }

    [Fact]
    public void DuasAmostras_MinMaxMediaCorretas()
    {
        var amostras = new[]
        {
            Amostra(cpuTemp: 50f, ramUso: 40f),
            Amostra(cpuTemp: 70f, ramUso: 60f)
        };
        var r = AgregadoDiarioRegras.Calcular(Dia, amostras);

        Assert.Equal(2, r.Amostras);
        Assert.Equal(50f, r.CpuTempMin);
        Assert.Equal(70f, r.CpuTempMax);
        Assert.Equal(60f, r.CpuTempMedia);
        Assert.Equal(40f, r.RamUsoMin);
        Assert.Equal(60f, r.RamUsoMax);
        Assert.Equal(50f, r.RamUsoMedia);
    }

    [Fact]
    public void SensorNulo_NaoEntraNaConta()
    {
        var amostras = new[]
        {
            Amostra(cpuTemp: 50f, gpuTemp: null),
            Amostra(cpuTemp: 70f, gpuTemp: 80f)
        };
        var r = AgregadoDiarioRegras.Calcular(Dia, amostras);

        Assert.Equal(60f, r.CpuTempMedia);
        Assert.Equal(80f, r.GpuTempMedia);
        Assert.Equal(80f, r.GpuTempMin);
    }

    [Fact]
    public void TodosOsSensoresNulos_DevolveNulo()
    {
        var amostras = new[] { Amostra() };
        var r = AgregadoDiarioRegras.Calcular(Dia, amostras);

        Assert.Equal(1, r.Amostras);
        Assert.Null(r.CpuTempMedia);
        Assert.Null(r.GpuTempMedia);
        Assert.Null(r.RamUsoMedia);
    }
}
