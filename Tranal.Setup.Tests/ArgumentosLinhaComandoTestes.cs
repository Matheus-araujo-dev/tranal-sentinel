using TranalSetup.Dominio;
using Xunit;

namespace TranalSetup.Tests;

/// <summary>
/// Testes da linha de comando do instalador.
/// O contrato é: sem argumento abre a janela, --instalar instala em modo
/// silencioso, --desinstalar desinstala e --simular mostra o plano.
/// </summary>
public class ArgumentosLinhaComandoTestes
{
    [Fact]
    public void SemArgumento_AbreAInterface()
    {
        ArgumentosLinhaComando argumentos = ArgumentosLinhaComando.Analisar(Array.Empty<string>());

        Assert.True(argumentos.Valido);
        Assert.Equal(ModoExecucao.Interface, argumentos.Modo);
        Assert.False(argumentos.Simular);
    }

    [Fact]
    public void Instalar_ComPasta_InstalaEmModoSilencioso()
    {
        ArgumentosLinhaComando argumentos = ArgumentosLinhaComando.Analisar(new[] { "--instalar", @"C:\Payload" });

        Assert.True(argumentos.Valido);
        Assert.Equal(ModoExecucao.Instalar, argumentos.Modo);
        Assert.Equal(@"C:\Payload", argumentos.PastaPayload);
        Assert.False(argumentos.Simular);
    }

    [Fact]
    public void Instalar_AceitaOIgualComoSeparador()
    {
        ArgumentosLinhaComando argumentos = ArgumentosLinhaComando.Analisar(new[] { "--instalar=C:\\Payload" });

        Assert.True(argumentos.Valido);
        Assert.Equal(ModoExecucao.Instalar, argumentos.Modo);
        Assert.Equal(@"C:\Payload", argumentos.PastaPayload);
    }

    [Fact]
    public void Desinstalar_Desinstalar()
    {
        ArgumentosLinhaComando argumentos = ArgumentosLinhaComando.Analisar(new[] { "--desinstalar" });

        Assert.True(argumentos.Valido);
        Assert.Equal(ModoExecucao.Desinstalar, argumentos.Modo);
    }

    [Fact]
    public void Simular_Sozinho_NaoMudaAOperacao()
    {
        ArgumentosLinhaComando argumentos = ArgumentosLinhaComando.Analisar(new[] { "--simular" });

        Assert.True(argumentos.Valido);
        Assert.True(argumentos.Simular);
        Assert.Equal(ModoExecucao.Interface, argumentos.Modo);
    }

    [Fact]
    public void Simular_ComInstalar_MostraOPlanoDaInstalacao()
    {
        ArgumentosLinhaComando argumentos =
            ArgumentosLinhaComando.Analisar(new[] { "--simular", "--instalar", @"C:\Payload" });

        Assert.True(argumentos.Valido);
        Assert.True(argumentos.Simular);
        Assert.Equal(ModoExecucao.Instalar, argumentos.Modo);
        Assert.Equal(@"C:\Payload", argumentos.PastaPayload);
    }

    [Fact]
    public void Simular_ComDesinstalar_MostraOPlanoDaDesinstalacao()
    {
        ArgumentosLinhaComando argumentos =
            ArgumentosLinhaComando.Analisar(new[] { "--desinstalar", "--simular" });

        Assert.True(argumentos.Valido);
        Assert.True(argumentos.Simular);
        Assert.Equal(ModoExecucao.Desinstalar, argumentos.Modo);
    }

    [Fact]
    public void InstalarSemPasta_EInvalido()
    {
        ArgumentosLinhaComando argumentos = ArgumentosLinhaComando.Analisar(new[] { "--instalar" });

        Assert.False(argumentos.Valido);
        Assert.False(string.IsNullOrWhiteSpace(argumentos.Erro));
    }

    [Fact]
    public void InstalareDesinstalar_Juntos_EInvalido()
    {
        ArgumentosLinhaComando argumentos =
            ArgumentosLinhaComando.Analisar(new[] { "--instalar", @"C:\Payload", "--desinstalar" });

        Assert.False(argumentos.Valido);
        Assert.Contains("apenas uma operação", argumentos.Erro ?? "", StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void OpcaoDesconhecida_EInvalida()
    {
        ArgumentosLinhaComando argumentos = ArgumentosLinhaComando.Analisar(new[] { "--inventado" });

        Assert.False(argumentos.Valido);
        Assert.Contains("--inventado", argumentos.Erro ?? "", StringComparison.Ordinal);
    }

    [Fact]
    public void SemDestino_ValemODestinoPadrao()
    {
        ArgumentosLinhaComando argumentos =
            ArgumentosLinhaComando.Analisar(new[] { "--instalar", @"C:\Payload" });

        Assert.True(argumentos.Valido);
        Assert.Null(argumentos.PastaDestino);
    }

    [Fact]
    public void Destino_ComInstalar_EscolheAPastaDeInstalacao()
    {
        ArgumentosLinhaComando argumentos = ArgumentosLinhaComando.Analisar(
            new[] { "--instalar", @"C:\Payload", "--destino", @"D:\Apps" });

        Assert.True(argumentos.Valido);
        Assert.Equal(@"D:\Apps", argumentos.PastaDestino);
    }

    [Fact]
    public void Destino_AceitaOIgualComoSeparador()
    {
        ArgumentosLinhaComando argumentos = ArgumentosLinhaComando.Analisar(
            new[] { "--instalar", @"C:\Payload", @"--destino=D:\Apps" });

        Assert.True(argumentos.Valido);
        Assert.Equal(@"D:\Apps", argumentos.PastaDestino);
    }

    [Fact]
    public void Destino_SemInstalar_EInvalido()
    {
        ArgumentosLinhaComando argumentos = ArgumentosLinhaComando.Analisar(new[] { "--destino", @"D:\Apps" });

        Assert.False(argumentos.Valido);
        Assert.Contains("--destino", argumentos.Erro ?? "", StringComparison.Ordinal);
    }

    [Fact]
    public void Destino_ComCaminhoInvalido_EInvalido()
    {
        ArgumentosLinhaComando argumentos = ArgumentosLinhaComando.Analisar(
            new[] { "--instalar", @"C:\Payload", "--destino", "Apps" });

        Assert.False(argumentos.Valido);
    }

    [Fact]
    public void Destino_SemPasta_EInvalido()
    {
        ArgumentosLinhaComando argumentos =
            ArgumentosLinhaComando.Analisar(new[] { "--instalar", @"C:\Payload", "--destino" });

        Assert.False(argumentos.Valido);
    }
}
