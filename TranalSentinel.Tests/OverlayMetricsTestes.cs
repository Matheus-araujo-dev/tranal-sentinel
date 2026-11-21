using Xunit;

namespace TranalSentinel.Tests;

/// <summary>
/// As escolhas de "O que mostrar" do monitor: a ventoinha que entra uma vez
/// só nas escolhas antigas, e as linhas sem leitura durante um benchmark.
/// </summary>
public class OverlayMetricsTestes
{
    [Fact]
    public void EscolhasAntigasSemVentoinha_GanhamAVentoinhaUmaVez()
    {
        // As escolhas gravadas no PC do dono antes da v0.37.2 (sem "fan"): o
        // painel antigo mostrava a ventoinha sempre, e ela não pode sumir.
        var antigas = new List<string> { "fps", "gpu_load", "cpu_temp", "ram_pct", "game" };

        var (itens, mudou) = OverlayMetrics.MigrarVentoinha(antigas, jaMigrado: false);

        Assert.True(mudou);
        Assert.Contains("fan", itens!);
        Assert.DoesNotContain("fan", antigas);   // a lista gravada não é alterada por baixo
    }

    [Fact]
    public void DepoisDeMigrado_AVentoinhaDesmarcadaNaoVolta()
    {
        var semVentoinha = new List<string> { "fps", "cpu_temp" };

        var (itens, mudou) = OverlayMetrics.MigrarVentoinha(semVentoinha, jaMigrado: true);

        Assert.False(mudou);
        Assert.DoesNotContain("fan", itens!);
    }

    [Fact]
    public void SemEscolhasGravadas_ValeOPadrao()
    {
        // null = nunca configurado: o padrão já traz a ventoinha.
        var (itens, mudou) = OverlayMetrics.MigrarVentoinha(null, jaMigrado: false);

        Assert.Null(itens);
        Assert.False(mudou);
        Assert.Contains("fan", OverlayMetrics.Defaults());
    }

    [Fact]
    public void DuranteOBench_AsLinhasEscolhidasMostramSoOTraco()
    {
        var mostrar = new HashSet<string> { "fps", "gpu_temp", "cpu_load", "fan" };

        var linhas = OverlayMetrics.LinhasSemLeitura(mostrar);

        Assert.Equal(new[] { "GPU", "CPU", "VENT" }, linhas.Select(l => l.Label));
        Assert.All(linhas, l => Assert.Equal(SensorTexto.Ausente, l.Value));
    }

    [Fact]
    public void DuranteOBench_SoFpsMarcado_NaoTemLinhaDeSensor()
    {
        Assert.Empty(OverlayMetrics.LinhasSemLeitura(new HashSet<string> { "fps", "frametime", "low1" }));
    }
}
