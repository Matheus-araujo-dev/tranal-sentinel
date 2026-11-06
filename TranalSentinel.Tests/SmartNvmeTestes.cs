using Xunit;

namespace TranalSentinel.Tests;

/// <summary>
/// A leitura da página SMART/Health do NVMe.
///
/// <para>
/// A página é montada à mão aqui, com o layout da especificação do NVMe
/// (o mesmo do Windows): o aviso crítico no byte 0, a temperatura
/// composta nos bytes 1 e 2 (em Kelvin), a reserva disponível no 3, a
/// reserva mínima no 4, o percentual de uso no 5 e as 8 temperaturas nos
/// bytes 17 a 32.
///
/// </para>
/// <para>
/// O teste trava a semântica do "percento de uso": é quanto o disco já foi
/// usado, e a vida que resta é o complemento. A versão errada (ler a
/// capacidade do disco como se fosse a vida útil) faria o app mostrar
/// "100 % de vida" em todo disco e nunca acusar desgaste.
/// </para>
/// </summary>
public class SmartNvmeTestes
{
    private static byte[] Pagina(byte aviso = 0, int kelvin = 313, byte reserva = 100, byte reservaMinima = 10, byte uso = 7)
    {
        var p = new byte[512];
        p[0] = aviso;
        p[1] = (byte)(kelvin & 0xFF);
        p[2] = (byte)(kelvin >> 8);
        p[3] = reserva;
        p[4] = reservaMinima;
        p[5] = uso;
        return p;
    }

    [Fact]
    public void AVidaQueRestaEoComplementoDoPercentoDeUso()
    {
        var contadores = SmartLeitor.DecodificarNvme(Pagina(uso: 7), out int? vida);

        Assert.NotNull(vida);
        Assert.Equal(93, vida!.Value);
        Assert.Contains(contadores, c => c.Nome == "Vida útil restante" && c.Valor.Contains("93"));
    }

    [Fact]
    public void APercentoDeUsoZeroEUmDiscoNovo()
    {
        SmartLeitor.DecodificarNvme(Pagina(uso: 0), out int? vida);

        Assert.Equal(100, vida!.Value);
    }

    [Fact]
    public void APercentoDeUsoCemNaoDeixaVidaNegativa()
    {
        SmartLeitor.DecodificarNvme(Pagina(uso: 100), out int? vida);

        Assert.Equal(0, vida!.Value);
    }

    [Fact]
    public void ATemperaturaEEmKelvin()
    {
        var contadores = SmartLeitor.DecodificarNvme(Pagina(kelvin: 313), out _);

        var temperatura = contadores.Single(c => c.Nome == "Temperatura");
        Assert.Contains("40", temperatura.Valor);
        Assert.Contains("313", temperatura.Valor);
    }

    [Fact]
    public void ATemperaturaForaDaFaixaMostraOBruto()
    {
        // 2550 K não é uma temperatura plausível: o app mostra o número cru
        // em vez de inventar uma conversão.
        var contadores = SmartLeitor.DecodificarNvme(Pagina(kelvin: 2550), out _);

        var temperatura = contadores.Single(c => c.Nome == "Temperatura");
        Assert.Contains("2550", temperatura.Valor);
        Assert.DoesNotContain("°C", temperatura.Valor);
    }

    [Fact]
    public void OAvisoDoDiscoEOLidoDoPrimeiroByte()
    {
        var contadores = SmartLeitor.DecodificarNvme(Pagina(aviso: 0x02), out _);

        var aviso = contadores.Single(c => c.Nome == "Aviso do disco");
        Assert.Contains("temperatura", aviso.Valor);
    }

    [Fact]
    public void AsTemperaturasDosSensoresVemDosBytesCertos()
    {
        var pagina = Pagina();
        // Sensor 1 nos bytes 17 e 18 (0x0131 = 305 K = 32 °C).
        pagina[17] = 0x31;
        pagina[18] = 0x01;

        var contadores = SmartLeitor.DecodificarNvme(pagina, out _);

        var sensor = contadores.Single(c => c.Nome == "Temperatura do sensor 1");
        Assert.Contains("32", sensor.Valor);
        Assert.Contains("305", sensor.Valor);
    }

    [Fact]
    public void SensorDeTemperaturaZeroNaoAparece()
    {
        var contadores = SmartLeitor.DecodificarNvme(Pagina(), out _);

        Assert.DoesNotContain(contadores, c => c.Nome.StartsWith("Temperatura do sensor"));
    }
}
