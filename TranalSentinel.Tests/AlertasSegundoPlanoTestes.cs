using LibreHardwareMonitor.Hardware;
using Xunit;

namespace TranalSentinel.Tests;

/// <summary>
/// Testes do vigia em segundo plano: temperatura do disco e alertas.
///
/// <para>
/// O que estes testes cobrem: o alerta de disco tem que vir do disco
/// (e não da GPU ou da placa-mãe), e o vigia não pode repetir o aviso nem
/// ficar preso com ele depois que a temperatura volta ao normal.
/// </para>
/// </summary>
public class AlertasSegundoPlanoTestes
{
    private static SensorRow Linha(HardwareType hardware, SensorType tipo, string nome, float? valor)
    {
        var linha = new SensorRow
        {
            Hardware = hardware.ToString(),
            Type = tipo,
            HardwareType = hardware,
            Name = nome,
        };
        linha.Update(valor, null, null);
        return linha;
    }

    [Fact]
    public void TemperaturaDoDiscoPEscolhidaDoDisco()
    {
        var linhas = new[]
        {
            Linha(HardwareType.Storage, SensorType.Temperature, "Temperatura", 55f),
            Linha(HardwareType.Storage, SensorType.Temperature, "Temperatura 2", 48f),
        };

        Assert.Equal(55f, MonitorAlertas.TemperaturaMaximaDisco(linhas));
    }

    [Fact]
    public void TemperaturaDaGpuNaoViraTemperaturaDeDisco()
    {
        // Sensor de GPU ou de CPU não é disco: se contasse, o alerta de
        // disco dispararia por causa da GPU.
        var linhas = new[]
        {
            Linha(HardwareType.GpuAmd, SensorType.Temperature, "Core", 90f),
            Linha(HardwareType.Cpu, SensorType.Temperature, "Core Max", 70f),
            Linha(HardwareType.Storage, SensorType.Temperature, "Temperatura", 40f),
        };

        Assert.Equal(40f, MonitorAlertas.TemperaturaMaximaDisco(linhas));
    }

    [Fact]
    public void SemSensorDeDiscoNaoHaTemperatura()
    {
        var linhas = new[]
        {
            Linha(HardwareType.GpuAmd, SensorType.Temperature, "Core", 90f),
        };

        Assert.Null(MonitorAlertas.TemperaturaMaximaDisco(linhas));
    }

    [Fact]
    public void SensorDeDiscoQueNaoETemperaturaNaoConta()
    {
        // Carga de leitura do disco não é temperatura.
        var linhas = new[]
        {
            Linha(HardwareType.Storage, SensorType.Load, "Leitura", 90f),
        };

        Assert.Null(MonitorAlertas.TemperaturaMaximaDisco(linhas));
    }

    [Fact]
    public void ListaVaziaNaoQuebra()
    {
        Assert.Null(MonitorAlertas.TemperaturaMaximaDisco(Array.Empty<SensorRow>()));
    }

    [Fact]
    public void AlertaDeDiscoSomeQuandoODiscoEsfria()
    {
        var monitor = new MonitorAlertas();
        var avisos = new List<Alerta>();
        monitor.Alertar += a => avisos.Add(a);

        monitor.Verificar(new MonitorAlertas.Estado(50, 65, null, true));
        Assert.Single(avisos);
        Assert.Equal("temperatura-disco", avisos[0].Id);

        monitor.Verificar(new MonitorAlertas.Estado(50, 40, null, true));
        Assert.Empty(monitor.Ativos);
    }
}
