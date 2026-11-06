using Xunit;

namespace TranalSentinel.Tests;

/// <summary>
/// Testes do catálogo de sensores: o que o usuário pode vigiar.
///
/// <para>
/// O catálogo é a fonte única: cada item sabe a chave, o rótulo, a unidade,
/// o limite padrão e — o mais importante — como ler o valor. Um item que
/// lê o valor errado (ou uma chave que não existe) quebra a promessa do
/// produto: a regra que o usuário define tem que vigiar a coisa certa.
/// </para>
/// </summary>
public class SensorCatalogoTestes
{
    private static readonly ContextoSensores Contexto = new()
    {
        TemperaturaCpu = 61.5f,
        TemperaturaGpu = 72.5f,
        TemperaturaDisco = 43.5f,
        CargaCpu = 33.5f,
        CargaGpu = 44.5f,
        ClockCpu = 3600.5f,
        ClockGpu = 1800.5f,
        UsoRam = 55.5f,
        Bateria = 77,
        Carregando = false,
    };

    [Fact]
    public void OCatalogoTemOsOitoSensores()
    {
        Assert.Equal(8, SensorCatalogo.Itens.Count);
    }

    [Fact]
    public void OsSensoresVemNaOrdemCerta()
    {
        var chaves = SensorCatalogo.Itens.Select(i => i.Chave).ToArray();

        Assert.Equal(
            new[] { "cpu-temp", "gpu-temp", "disco-temp", "ram-uso", "cpu-carga", "gpu-carga", "cpu-clock", "gpu-clock" },
            chaves);
    }

    [Theory]
    [InlineData("cpu-temp", "Temperatura da CPU", "°C", ">=", 85, Severidade.Alto)]
    [InlineData("gpu-temp", "Temperatura da GPU", "°C", ">=", 85, Severidade.Alto)]
    [InlineData("disco-temp", "Temperatura do disco", "°C", ">=", 60, Severidade.Medio)]
    [InlineData("ram-uso", "Uso da memória RAM", "%", ">=", 90, Severidade.Medio)]
    [InlineData("cpu-carga", "Carga da CPU", "%", ">=", 95, Severidade.Baixo)]
    [InlineData("gpu-carga", "Carga da GPU", "%", ">=", 95, Severidade.Baixo)]
    [InlineData("cpu-clock", "Clock da CPU", "MHz", "<=", 2000, Severidade.Medio)]
    [InlineData("gpu-clock", "Clock da GPU", "MHz", "<=", 200, Severidade.Medio)]
    public void CadaSensorTemOSeuPadrao(string chave, string rotulo, string unidade, string operacao, double limite, Severidade severidade)
    {
        var item = SensorCatalogo.PorChave(chave);

        Assert.NotNull(item);
        Assert.Equal(rotulo, item!.Rotulo);
        Assert.Equal(unidade, item.Unidade);
        Assert.Equal(operacao, item.OperacaoPadrao);
        Assert.Equal(limite, item.LimitePadrao);
        Assert.Equal(severidade, item.SeveridadePadrao);
        Assert.False(string.IsNullOrWhiteSpace(item.Dica));
    }

    [Fact]
    public void CadaSensorLeOSeuValorDoContexto()
    {
        Assert.Equal(61.5f, SensorCatalogo.PorChave("cpu-temp")!.Ler(Contexto));
        Assert.Equal(72.5f, SensorCatalogo.PorChave("gpu-temp")!.Ler(Contexto));
        Assert.Equal(43.5f, SensorCatalogo.PorChave("disco-temp")!.Ler(Contexto));
        Assert.Equal(55.5f, SensorCatalogo.PorChave("ram-uso")!.Ler(Contexto));
        Assert.Equal(33.5f, SensorCatalogo.PorChave("cpu-carga")!.Ler(Contexto));
        Assert.Equal(44.5f, SensorCatalogo.PorChave("gpu-carga")!.Ler(Contexto));
        Assert.Equal(3600.5f, SensorCatalogo.PorChave("cpu-clock")!.Ler(Contexto));
        Assert.Equal(1800.5f, SensorCatalogo.PorChave("gpu-clock")!.Ler(Contexto));
    }

    [Fact]
    public void SensorQueNaoExisteNoContextoDevolveNulo()
    {
        var contexto = new ContextoSensores();

        Assert.Null(SensorCatalogo.PorChave("cpu-temp")!.Ler(contexto));
        Assert.Null(SensorCatalogo.PorChave("gpu-temp")!.Ler(contexto));
        Assert.Null(SensorCatalogo.PorChave("gpu-clock")!.Ler(contexto));
    }

    [Fact]
    public void ChaveDesconhecidaDevolveNulo()
    {
        Assert.Null(SensorCatalogo.PorChave("sensor-que-nao-existe"));
        Assert.Null(SensorCatalogo.PorChave(""));
    }

    [Fact]
    public void ABateriaNaoEstaNoCatalogo()
    {
        // A bateria é lida por um caminho próprio (só vale fora da tomada),
        // então ela não entra na lista do catálogo.
        Assert.Null(SensorCatalogo.PorChave("bateria"));
        Assert.DoesNotContain(SensorCatalogo.Itens, i => i.Chave == "bateria");
    }
}
