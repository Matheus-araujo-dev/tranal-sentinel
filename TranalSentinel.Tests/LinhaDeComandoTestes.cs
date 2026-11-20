using Xunit;

namespace TranalSentinel.Tests;

/// <summary>
/// A leitura da linha de comando e o desvio do processo de coleta: o
/// <see cref="Programa"/> sobe o coletor antes do WPF, e qualquer outra linha
/// de comando precisa seguir para o app normal.
/// </summary>
public class LinhaDeComandoTestes
{
    [Fact]
    public void Tem_NaoDiferenciaMaiusculas()
    {
        Assert.True(LinhaDeComando.Tem(new[] { "--SEM-GPU" }, "--sem-gpu"));
        Assert.False(LinhaDeComando.Tem(new[] { "--saude" }, "--sem-gpu"));
    }

    [Theory]
    [InlineData(new[] { "--intervalo", "2000" }, 2000)]
    [InlineData(new[] { "--intervalo" }, 1000)]          // sem valor: o padrão
    [InlineData(new[] { "--intervalo", "abc" }, 1000)]   // valor inválido: o padrão
    [InlineData(new string[0], 1000)]
    public void Inteiro_LeOValorOuDevolveOPadrao(string[] args, int esperado)
    {
        Assert.Equal(esperado, LinhaDeComando.Inteiro(args, "--intervalo", 1000));
    }

    [Theory]
    [InlineData("")]
    [InlineData("--saude")]
    [InlineData("--iniciar-como-sistema")]
    public void SemOArgumentoDoColetor_OAppSegueNormal(string arg)
    {
        string[] args = arg.Length == 0 ? Array.Empty<string>() : new[] { arg };
        Assert.False(SensorWorker.TentarRodar(args));
    }

    [Fact]
    public void ColetorSemCaminho_NaoAbreAColetaEMesmoAssimNaoSobeOApp()
    {
        // A linha de comando é a do coletor, mas sem o arquivo de leituras:
        // não há o que coletar, e o processo também não pode virar o app
        // com janela.
        Assert.True(SensorWorker.TentarRodar(new[] { SensorWorker.Argumento }));
    }

    [Fact]
    public void OAjusteDoRuntimeDoColetorSoMexeNoRuntime()
    {
        // Só variáveis do runtime do .NET: o ambiente do coletor é herdado do
        // app pai, e o ajuste não pode trocar mais nada dele.
        Assert.NotEmpty(SensorWorker.AmbienteDoRuntime);
        Assert.All(SensorWorker.AmbienteDoRuntime.Keys, k => Assert.StartsWith("DOTNET_", k));
    }
}
