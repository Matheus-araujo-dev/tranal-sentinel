using System.Globalization;
using LibreHardwareMonitor.Hardware;
using Xunit;

namespace TranalSentinel.Tests;

/// <summary>
/// Testes do texto da linha de sensor.
///
/// <para>
/// A lista de sensores e o overlay mostram o mesmo numero: ele tem que
/// sair do mesmo jeito nos dois.
/// </para>
/// </summary>
public class SensorRowTestes
{
    private static SensorRow Linha(SensorType tipo, double valor)
    {
        var linha = new SensorRow { Type = tipo };
        linha.Update((float)valor, null, null);
        return linha;
    }

    [Fact]
    public void OClockSaiComOPontoDeMilharDaCasa()
    {
        Assert.Equal("2.790 MHz", Linha(SensorType.Clock, 2790).Value);
    }

    [Fact]
    public void OFanSaiComOPontoDeMilharDaCasa()
    {
        Assert.Equal("1.407 RPM", Linha(SensorType.Fan, 1407).Value);
    }

    [Fact]
    public void APercentualEInteiro()
    {
        Assert.Equal("47%", Linha(SensorType.Load, 47.4).Value);
    }

    [Fact]
    public void ADataSaiComUmaCasa()
    {
        Assert.Equal("8,5 GB", Linha(SensorType.Data, 8.5).Value);
    }

    [Fact]
    public void AVazaoSaiEmMegabytesPorSegundo()
    {
        // O sensor entrega bytes por segundo; a lista mostra MB/s.
        Assert.Equal("150,0 MB/s", Linha(SensorType.Throughput, 150 * 1048576f).Value);
    }

    [Fact]
    public void SemValorViraEmDash()
    {
        var linha = new SensorRow { Type = SensorType.Temperature };

        Assert.Equal("—", linha.Value);
    }

    [Fact]
    public void AFrequenciaSaiComOPontoDeMilharDaCasa()
    {
        Assert.Equal("1.000 Hz", Linha(SensorType.Frequency, 1000).Value);
    }

    [Fact]
    public void AListaNaoDependeDaCulturaDoWindows()
    {
        // Este era o defeito original: a lista formatava na mao, com a
        // cultura corrente. Em en-US saia "51.4 °C" contra "51,4 °C" no
        // overlay.
        var cultura = CultureInfo.CurrentCulture;

        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("en-US");

            Assert.Equal("51,4 °C", Linha(SensorType.Temperature, 51.4).Value);
            Assert.Equal("47%", Linha(SensorType.Load, 47.4).Value);
            Assert.Equal("2.790 MHz", Linha(SensorType.Clock, 2790).Value);
            Assert.Equal("1.407 RPM", Linha(SensorType.Fan, 1407).Value);
            Assert.Equal("1.000 Hz", Linha(SensorType.Frequency, 1000).Value);
            Assert.Equal("8,5 GB", Linha(SensorType.Data, 8.5).Value);
            Assert.Equal("150,0 MB/s", Linha(SensorType.Throughput, 150 * 1048576f).Value);
        }
        finally
        {
            CultureInfo.CurrentCulture = cultura;
        }
    }
}
