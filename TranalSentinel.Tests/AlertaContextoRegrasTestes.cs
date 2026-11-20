using Xunit;

namespace TranalSentinel.Tests;

/// <summary>
/// Testes dos alertas contextuais por programa: o cruzamento de
/// sensores com o programa em foco.
/// </summary>
public class AlertaContextoRegrasTestes
{
    private static RegraAlerta Regra(string sensor, string operacao, double limite) =>
        new() { Sensor = sensor, Operacao = operacao, Limite = limite };

    [Fact]
    public void SemPrograma_SemAlerta()
    {
        var sensores = new ContextoSensores { TemperaturaCpu = 95f };
        var regras = new[] { Regra("cpu-temp", ">=", 90) };

        var alertas = AlertaContextoRegras.Verificar(sensores, null, regras);
        Assert.Empty(alertas);
    }

    [Fact]
    public void CpuAcimaDoLimite_GeraAlerta()
    {
        var sensores = new ContextoSensores { TemperaturaCpu = 95f };
        var regras = new[] { Regra("cpu-temp", ">=", 90) };

        var alertas = AlertaContextoRegras.Verificar(sensores, "Chrome", regras);

        Assert.Single(alertas);
        Assert.Equal("Chrome", alertas[0].Programa);
        Assert.Equal("cpu-temp", alertas[0].Sensor);
        Assert.Equal(95f, alertas[0].Valor);
    }

    [Fact]
    public void CpuAbaixoDoLimite_SemAlerta()
    {
        var sensores = new ContextoSensores { TemperaturaCpu = 60f };
        var regras = new[] { Regra("cpu-temp", ">=", 90) };

        var alertas = AlertaContextoRegras.Verificar(sensores, "Chrome", regras);
        Assert.Empty(alertas);
    }

    [Fact]
    public void OperacaoMenorOuIgual_Funciona()
    {
        var sensores = new ContextoSensores { TemperaturaCpu = 30f };
        var regras = new[] { Regra("cpu-temp", "<=", 35) };

        var alertas = AlertaContextoRegras.Verificar(sensores, "Idle", regras);

        Assert.Single(alertas);
        Assert.Equal(30f, alertas[0].Valor);
    }

    [Fact]
    public void SensorSemLeitura_SemAlerta()
    {
        var sensores = new ContextoSensores { TemperaturaCpu = null };
        var regras = new[] { Regra("cpu-temp", ">=", 90) };

        var alertas = AlertaContextoRegras.Verificar(sensores, "Chrome", regras);
        Assert.Empty(alertas);
    }

    [Fact]
    public void TextoAlerta_Formatado()
    {
        var a = new AlertaContextual("cpu-temp", "CPU", "°C", 95f, 90.0, "Chrome", DateTime.Now);
        string t = AlertaContextoRegras.TextoAlerta(a);

        Assert.Contains("CPU", t);
        Assert.Contains("Chrome", t);
        Assert.Contains("limite", t);
    }

    [Fact]
    public void RankingPorPrograma_AgrupaEOrdena()
    {
        var historico = new AlertaContextual[]
        {
            new("cpu-temp", "CPU", "°C", 95f, 90.0, "Chrome", DateTime.Now),
            new("gpu-temp", "GPU", "°C", 85f, 80.0, "Chrome", DateTime.Now),
            new("cpu-temp", "CPU", "°C", 92f, 90.0, "Game", DateTime.Now),
        };

        var ranking = AlertaContextoRegras.RankingPorPrograma(historico);

        Assert.Equal(2, ranking.Count);
        Assert.Equal("Chrome", ranking[0].Programa);
        Assert.Equal(2, ranking[0].Contagem);
    }

    [Fact]
    public void TextoRanking_SemHistorico()
    {
        var ranking = AlertaContextoRegras.RankingPorPrograma(Array.Empty<AlertaContextual>());
        string t = AlertaContextoRegras.TextoRanking(ranking);
        Assert.Contains("Nenhum programa", t);
    }
}
