using Xunit;

namespace TranalSentinel.Tests;

/// <summary>
/// Testes do relatório semanal: resumo dos sensores, decisão de
/// geração e cálculo da semana anterior.
/// </summary>
public class RelatorioSemanalRegrasTestes
{
    private static AgregadoDiario Agreg(int dia, float? cpuMedia, float? cpuMax,
        float? gpuMedia = null, float? gpuMax = null, float? ramMedia = null, float? ramMax = null) =>
        new(new DateTime(2026, 11, 1).AddDays(dia),
            null, cpuMax, cpuMedia, null, null, null,
            null, gpuMax, gpuMedia, null, null, null,
            null, ramMax, ramMedia, 100);

    [Fact]
    public void ResumoSensores_SemDados()
    {
        var r = RelatorioSemanalRegras.ResumoSensores(Array.Empty<AgregadoDiario>());
        Assert.Equal(0, r.DiasComDado);
        Assert.Null(r.CpuTempMedia);
    }

    [Fact]
    public void ResumoSensores_ComDados()
    {
        var agregados = new[]
        {
            Agreg(0, cpuMedia: 60, cpuMax: 80, ramMedia: 50, ramMax: 70),
            Agreg(1, cpuMedia: 70, cpuMax: 90, ramMedia: 60, ramMax: 80),
        };

        var r = RelatorioSemanalRegras.ResumoSensores(agregados);

        Assert.Equal(2, r.DiasComDado);
        Assert.NotNull(r.CpuTempMedia);
        Assert.True(MathF.Abs(r.CpuTempMedia.Value - 65f) < 0.1f);
        Assert.Equal(90f, r.CpuTempMax);
        Assert.NotNull(r.RamUsoMedia);
        Assert.True(MathF.Abs(r.RamUsoMedia.Value - 55f) < 0.1f);
        Assert.Equal(80f, r.RamUsoMax);
    }

    [Fact]
    public void DeveGerar_SegundaFeira_NuncaGerado()
    {
        // 2026-11-23 é segunda-feira
        var segunda = new DateTime(2026, 11, 23);
        Assert.True(RelatorioSemanalRegras.DeveGerar(segunda, null));
    }

    [Fact]
    public void DeveGerar_NaoSegundaFeira()
    {
        var terca = new DateTime(2026, 11, 24);
        Assert.False(RelatorioSemanalRegras.DeveGerar(terca, null));
    }

    [Fact]
    public void DeveGerar_JaGeradoEstaSemana()
    {
        var segunda = new DateTime(2026, 11, 23);
        var ultimoRelatorio = new DateTime(2026, 11, 23);
        Assert.False(RelatorioSemanalRegras.DeveGerar(segunda, ultimoRelatorio));
    }

    [Fact]
    public void SemanaAnterior_CalculaCorreto()
    {
        var segunda = new DateTime(2026, 11, 23);
        var (inicio, fim) = RelatorioSemanalRegras.SemanaAnterior(segunda);

        Assert.Equal(new DateTime(2026, 11, 16), inicio);
        Assert.Equal(new DateTime(2026, 11, 22), fim);
    }

    [Fact]
    public void Texto_RelatorioCompleto()
    {
        var sensores = new ResumoSensoresSemanal(65f, 90f, 55f, 80f, 60f, 85f, 7);
        var uso = new ResumoUsoSemanal(40, "Chrome", 15);
        var trafego = new ResumoTrafegoSemanal(1024L * 1024 * 1024 * 5, "Chrome", 7);
        var relatorio = new RelatorioSemanal(
            new DateTime(2026, 11, 16), new DateTime(2026, 11, 22),
            sensores, uso, trafego,
            new[] { "CPU acima de 90°C" },
            Array.Empty<Tendencia>(),
            Array.Empty<DesvioBaseline>());

        string texto = RelatorioSemanalRegras.Texto(relatorio);

        Assert.Contains("RELATÓRIO SEMANAL", texto);
        Assert.Contains("SENSORES", texto);
        Assert.Contains("USO DO PC", texto);
        Assert.Contains("TRÁFEGO DE REDE", texto);
        Assert.Contains("Chrome", texto);
        Assert.Contains("CPU acima de 90°C", texto);
    }
}
