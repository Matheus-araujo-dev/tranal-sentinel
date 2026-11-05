using Xunit;

namespace TranalSentinel.Tests;

/// <summary>
/// Testes da correlação entre o log de sensores e os travamentos: o que o PC
/// estava fazendo nos minutos que antecederam o congelamento.
///
/// <para>
/// Tudo aqui é função pura: nem arquivo nem Windows são tocados.
/// </para>
/// </summary>
public class RetrospectivaTravamentoTestes
{
    /// <summary>O instante do congelamento (a hora em que a máquina parou).</summary>
    private static readonly DateTime Crash = new(2026, 11, 5, 14, 31, 0, DateTimeKind.Local);

    private static TravamentoInfo Travamento(DateTime quando) => new()
    {
        Data = quando,
        Bugcheck = "0",
        Detalhe = "Sem botão de energia (travou sozinho)"
    };

    private static AmostraSensor Amostra(DateTime momento, float? cpuTemp = null, float? cpuCarga = null,
                                          float? cpuClock = null, float? gpuTemp = null, float? gpuCarga = null,
                                          float? ram = null, float? discoLeitura = null, float? discoEscrita = null) =>
        new(momento, cpuTemp, cpuCarga, 100f, cpuClock, gpuTemp, gpuCarga, 2000f, 2048f, ram, discoLeitura, discoEscrita);

    [Fact]
    public void TravamentoComAmostrasNaJanela_MontaOResumoComOsNumeros()
    {
        var travamento = Travamento(Crash.AddSeconds(30));
        var amostras = new List<AmostraSensor>
        {
            Amostra(Crash.AddMinutes(-2), cpuTemp: 90f, cpuCarga: 80f, cpuClock: 3200f,
                    gpuTemp: 70f, gpuCarga: 99f, ram: 85f, discoLeitura: 10f, discoEscrita: 1f),
            Amostra(Crash.AddMinutes(-1), cpuTemp: 95f, cpuCarga: 90f, cpuClock: 2800f,
                    gpuTemp: 72f, gpuCarga: 100f, ram: 91f, discoLeitura: 12f, discoEscrita: 2f),
        };

        var resultado = CorrelatorTravamento.Correlacionar(new List<TravamentoInfo> { travamento }, amostras);

        var retrospectiva = Assert.Single(resultado);
        Assert.True(retrospectiva.TemDados);
        Assert.Contains("CPU a 95 °C", retrospectiva.Resumo);
        Assert.Contains("GPU a 72 °C", retrospectiva.Resumo);
        Assert.Contains("carga da CPU 85 %", retrospectiva.Resumo);   // média de 80 e 90
        Assert.Contains("GPU 100 %", retrospectiva.Resumo);
        Assert.Contains("RAM 91 %", retrospectiva.Resumo);
        Assert.Contains("disco 12 MB/s", retrospectiva.Resumo);
        Assert.Contains("clock da CPU 2,8 GHz", retrospectiva.Resumo);
    }

    [Fact]
    public void TravamentoSemAmostras_NaoTemDados()
    {
        var travamento = Travamento(Crash);

        var resultado = CorrelatorTravamento.Correlacionar(new List<TravamentoInfo> { travamento }, new List<AmostraSensor>());

        var retrospectiva = Assert.Single(resultado);
        Assert.False(retrospectiva.TemDados);
        Assert.Equal("o app não estava rodando na hora do travamento", retrospectiva.Resumo);
    }

    [Fact]
    public void TravamentoReiniciadoHorasDepois_AindaTemRetrospectiva()
    {
        // O caso real do dono: o PC congelou às 10:00 e o dono só voltou e
        // reiniciou às 14:00. A âncora é a hora do desligamento (evento 6008),
        // não a do boot: sem ela o app diria "não estava rodando" e jogaria
        // fora a janela que existe no arquivo.
        var travamento = Travamento(Crash.AddHours(4));
        travamento.InstanteDesligamento = Crash;

        var amostras = new List<AmostraSensor>
        {
            Amostra(Crash.AddMinutes(-2), cpuTemp: 90f, gpuCarga: 40f),
            Amostra(Crash.AddMinutes(-1), cpuTemp: 95f, gpuCarga: 100f),
        };

        var retrospectiva = Assert.Single(
            CorrelatorTravamento.Correlacionar(new List<TravamentoInfo> { travamento }, amostras));

        Assert.True(retrospectiva.TemDados);
        Assert.Contains("CPU a 95 °C", retrospectiva.Resumo);
        Assert.Contains("GPU 100 %", retrospectiva.Resumo);
    }

    [Fact]
    public void TravamentoComAppParadoHorasAntes_NaoTemDados()
    {
        // O congelamento é que importa: o log parou 2 h antes, então o app
        // não estava de pé na hora (mesmo que o boot tenha vindo logo depois).
        var travamento = Travamento(Crash.AddMinutes(30));
        travamento.InstanteDesligamento = Crash;

        var amostras = new List<AmostraSensor> { Amostra(Crash.AddHours(-2), cpuTemp: 95f) };

        var retrospectiva = Assert.Single(
            CorrelatorTravamento.Correlacionar(new List<TravamentoInfo> { travamento }, amostras));

        Assert.False(retrospectiva.TemDados);
        Assert.Equal("o app não estava rodando na hora do travamento", retrospectiva.Resumo);
    }

    [Fact]
    public void DoisTravamentosPerto_UmNaoPegaOResumoDoOutro()
    {
        // Dois congelamentos com menos de 2 minutos de distância: cada um tem
        // que receber a sua retrospectiva (a ordem do correlator é a dos
        // travamentos, e o pareamento é por índice).
        var primeiro = Travamento(Crash);
        primeiro.InstanteDesligamento = Crash;

        var segundo = Travamento(Crash.AddMinutes(1));
        segundo.InstanteDesligamento = Crash.AddMinutes(1);

        var amostras = new List<AmostraSensor>
        {
            Amostra(Crash.AddMinutes(-1), cpuTemp: 70f),
            Amostra(Crash, cpuTemp: 80f),
            Amostra(Crash.AddMinutes(1), cpuTemp: 90f),
        };

        var resultado = CorrelatorTravamento.Correlacionar(
            new List<TravamentoInfo> { primeiro, segundo }, amostras);

        Assert.Equal(2, resultado.Count);
        Assert.Contains("CPU a 80 °C", resultado[0].Resumo);
        Assert.Contains("CPU a 90 °C", resultado[1].Resumo);
    }

    [Fact]
    public void TravamentoComAmostrasAntigas_NaoTemDados()
    {
        // O PC ficou desligado (ou o app não estava de pé): a última amostra
        // é de 20 minutos antes do evento.
        var travamento = Travamento(Crash);
        var amostras = new List<AmostraSensor> { Amostra(Crash.AddMinutes(-20), cpuTemp: 95f) };

        var resultado = CorrelatorTravamento.Correlacionar(new List<TravamentoInfo> { travamento }, amostras);

        var retrospectiva = Assert.Single(resultado);
        Assert.False(retrospectiva.TemDados);
        Assert.Equal("o app não estava rodando na hora do travamento", retrospectiva.Resumo);
    }

    [Fact]
    public void TravamentoComAmostraExatamenteNoLimite_Aceita()
    {
        // 15 minutos é o limite: até aí o app pode ter estado de pé.
        var travamento = Travamento(Crash);
        var amostras = new List<AmostraSensor> { Amostra(Crash.AddMinutes(-15), cpuTemp: 95f) };

        var retrospectiva = Assert.Single(
            CorrelatorTravamento.Correlacionar(new List<TravamentoInfo> { travamento }, amostras));

        Assert.True(retrospectiva.TemDados);
        Assert.Contains("CPU a 95 °C", retrospectiva.Resumo);
    }

    [Fact]
    public void AmostrasEmVoltaDoLimiteDeDoisMinutos_SoEntraOQueEstaNaJanela()
    {
        var travamento = Travamento(Crash.AddSeconds(10));
        var amostras = new List<AmostraSensor>
        {
            Amostra(Crash.AddMinutes(-3), cpuTemp: 95f),
            Amostra(Crash.AddMinutes(-2), cpuTemp: 96f),
            Amostra(Crash.AddMinutes(-1), cpuTemp: 97f),
            Amostra(Crash, cpuTemp: 98f),
        };

        var retrospectiva = Assert.Single(
            CorrelatorTravamento.Correlacionar(new List<TravamentoInfo> { travamento }, amostras));

        Assert.True(retrospectiva.TemDados);
        Assert.Contains("CPU a 98 °C", retrospectiva.Resumo);
        Assert.DoesNotContain("95 °C", retrospectiva.Resumo);
        Assert.DoesNotContain("96 °C", retrospectiva.Resumo);
    }

    [Fact]
    public void SensorQueNuncaApareceu_NaoEntraNoResumo()
    {
        // Sem inventar valor: o que o sensor não mediu, não aparece.
        var travamento = Travamento(Crash.AddSeconds(10));
        var amostras = new List<AmostraSensor> { Amostra(Crash, cpuTemp: 95f) };

        var retrospectiva = Assert.Single(
            CorrelatorTravamento.Correlacionar(new List<TravamentoInfo> { travamento }, amostras));

        Assert.True(retrospectiva.TemDados);
        Assert.Equal("CPU a 95 °C", retrospectiva.Resumo);
        Assert.DoesNotContain("GPU", retrospectiva.Resumo);
        Assert.DoesNotContain("RAM", retrospectiva.Resumo);
        Assert.DoesNotContain("disco", retrospectiva.Resumo);
        Assert.DoesNotContain("clock", retrospectiva.Resumo);
        Assert.DoesNotContain("?", retrospectiva.Resumo);
    }

    [Fact]
    public void VariosTravamentos_VoltamNaMesmaOrdem()
    {
        var primeiro = Travamento(Crash);
        var segundo = Travamento(Crash.AddHours(-3));
        var amostras = new List<AmostraSensor>
        {
            Amostra(Crash, cpuTemp: 95f),
            Amostra(Crash.AddHours(-3), cpuTemp: 80f),
        };

        var resultado = CorrelatorTravamento.Correlacionar(
            new List<TravamentoInfo> { primeiro, segundo }, amostras);

        Assert.Equal(2, resultado.Count);
        Assert.Equal(primeiro.Data, resultado[0].Data);
        Assert.Equal(segundo.Data, resultado[1].Data);
        Assert.Contains("CPU a 95 °C", resultado[0].Resumo);
        Assert.Contains("CPU a 80 °C", resultado[1].Resumo);
    }

    [Fact]
    public void NumeroNoResumo_UsaVirgulaDecimal()
    {
        var travamento = Travamento(Crash.AddSeconds(10));
        var amostras = new List<AmostraSensor> { Amostra(Crash, cpuTemp: 95.5f, cpuCarga: 87.25f, cpuClock: 3200f) };

        var retrospectiva = Assert.Single(
            CorrelatorTravamento.Correlacionar(new List<TravamentoInfo> { travamento }, amostras));

        Assert.Contains("CPU a 95,5 °C", retrospectiva.Resumo);
        Assert.Contains("carga da CPU 87,25 %", retrospectiva.Resumo);
        Assert.Contains("clock da CPU 3,2 GHz", retrospectiva.Resumo);
    }

    [Fact]
    public void TravamentoComRetrospectiva_EComRetrospectivaVazia_SaoDistintos()
    {
        // A interface esconde a linha vazia: é este booleano que decide.
        Assert.False(new TravamentoInfo().TemRetrospectiva);
        Assert.False(new TravamentoInfo { Retrospectiva = "   " }.TemRetrospectiva);
        Assert.True(new TravamentoInfo { Retrospectiva = "CPU a 95 °C" }.TemRetrospectiva);
    }
}
