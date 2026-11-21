using Xunit;

namespace TranalSentinel.Tests;

/// <summary>
/// A espera do aviso "sem temperatura da CPU" enquanto a coleta sobe.
///
/// <para>
/// A regressão (20/11/2026, v0.37.1): a carência só começava depois que a
/// abertura da coleta terminava — e a abertura espera até 30 s pela primeira
/// leitura. Nesse meio tempo o app acusava "o driver não respondeu" com a tela
/// inteira em "—", só porque a primeira leitura ainda não tinha chegado.
/// </para>
/// </summary>
public class AvisoCpuEsperaTestes
{
    private static readonly DateTime Abertura = new(2026, 11, 20, 22, 54, 41, DateTimeKind.Utc);

    [Fact]
    public void AntesDaPrimeiraLeitura_EsperaEmVezDeAcusarODriver()
    {
        Assert.True(SensorMonitor.AvisoCpuEmEspera(Abertura.AddSeconds(20), Abertura, null, temTemperatura: false));
    }

    [Fact]
    public void PrimeiraLeituraQueNuncaChega_AvisaDepoisDoPrazo()
    {
        var agora = Abertura + SensorMonitor.EsperaDaPrimeiraColeta;
        Assert.False(SensorMonitor.AvisoCpuEmEspera(agora, Abertura, null, temTemperatura: false));
    }

    [Fact]
    public void LogoDepoisDaPrimeiraLeitura_AindaDaTempoParaACpu()
    {
        var primeira = Abertura.AddSeconds(25);
        Assert.True(SensorMonitor.AvisoCpuEmEspera(primeira.AddSeconds(5), Abertura, primeira, temTemperatura: false));
    }

    [Fact]
    public void CarenciaAcabouSemTemperatura_Avisa()
    {
        var primeira = Abertura.AddSeconds(25);
        var agora = primeira + SensorMonitor.CarenciaDepoisDaPrimeiraColeta;
        Assert.False(SensorMonitor.AvisoCpuEmEspera(agora, Abertura, primeira, temTemperatura: false));
    }

    [Fact]
    public void ComTemperatura_NaoEspera()
    {
        Assert.False(SensorMonitor.AvisoCpuEmEspera(Abertura.AddSeconds(1), Abertura, null, temTemperatura: true));
    }

    [Fact]
    public void ColetaAindaNaoAberta_Espera()
    {
        Assert.True(SensorMonitor.AvisoCpuEmEspera(Abertura, null, null, temTemperatura: false));
    }
}
