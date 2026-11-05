using System.Text.Json;
using TranalSentinel;
using Xunit;

namespace TranalSentinel.Tests;

/// <summary>
/// Testes do valor das leituras de sensores.
///
/// O defeito que estes testes cobrem: o LibreHardwareMonitor devolve infinito
/// para sensores que não sabem o valor. O JSON não aceita infinito, então a
/// serialização da leitura estourava — e o processo de coleta, que engolia o
/// erro, nunca gravava o arquivo. O app ficava sem sensor nenhum e mostrava
/// "o PawnIO está instalado, mas a leitura falhou".
/// </summary>
public class SensorValorTestes
{
    [Fact]
    public void Finito_ComValorNormal_ContinuaOValor()
    {
        Assert.Equal(53.4f, SensorValor.Finito(53.4f));
        Assert.Equal(0f, SensorValor.Finito(0f));
        Assert.Equal(-12.5f, SensorValor.Finito(-12.5f));
    }

    [Fact]
    public void Finito_ComInfinito_ViraNulo()
    {
        Assert.Null(SensorValor.Finito(float.PositiveInfinity));
        Assert.Null(SensorValor.Finito(float.NegativeInfinity));
    }

    [Fact]
    public void Finito_ComNan_ViraNulo()
    {
        Assert.Null(SensorValor.Finito(float.NaN));
    }

    [Fact]
    public void Finito_ComNulo_ContinuaNulo()
    {
        Assert.Null(SensorValor.Finito(null));
    }

    [Fact]
    public void LeituraComInfinito_SerializaSemEstourar()
    {
        // A regressão que quebrou o app: serializar a leitura com infinito
        // estourava e o arquivo nunca era gravado.
        var leitura = new SensorReading(
            "/amdcpu/0/clock/1", "AMD Ryzen 7 5700X", "Clock", "Core #1",
            LibreHardwareMonitor.Hardware.SensorType.Clock,
            LibreHardwareMonitor.Hardware.HardwareType.Cpu,
            SensorValor.Finito(float.PositiveInfinity),
            SensorValor.Finito(float.NegativeInfinity),
            SensorValor.Finito(float.NaN));

        var foto = new SensorSnapshot(new[] { leitura }, Array.Empty<string>(), DateTime.Now);

        string json = JsonSerializer.Serialize(foto, SensorValor.OpcoesJson);

        Assert.Contains("\"Value\":null", json, StringComparison.Ordinal);
    }

    [Fact]
    public void LeituraLidaDeVolta_TemOsMesmosValores()
    {
        var leitura = new SensorReading(
            "/amdcpu/0/temperature/0", "AMD Ryzen 7 5700X", "Temperatura", "Core (Tctl/Tdie)",
            LibreHardwareMonitor.Hardware.SensorType.Temperature,
            LibreHardwareMonitor.Hardware.HardwareType.Cpu,
            53.4f, 30f, 80f);

        var foto = new SensorSnapshot(new[] { leitura }, Array.Empty<string>(), DateTime.Now);

        string json = JsonSerializer.Serialize(foto, SensorValor.OpcoesJson);
        var lida = JsonSerializer.Deserialize<SensorSnapshot>(json, SensorValor.OpcoesJson);

        Assert.NotNull(lida);
        Assert.Single(lida!.Leituras);
        Assert.Equal(53.4f, lida.Leituras[0].Value);
        Assert.Equal("Core (Tctl/Tdie)", lida.Leituras[0].Name);
    }
}
