using TranalSetup.Dominio;
using Xunit;

namespace TranalSetup.Tests;

/// <summary>
/// Testes dos caminhos e dos valores que o instalador grava.
/// São funções puras: dependem só do contexto informado, nada do disco.
/// </summary>
public class CaminhosTestes
{
    private static ContextoSistema ContextoDeTeste() => new()
    {
        PastaProgramFiles = @"C:\Program Files",
        PastaDadosAplicacao = @"C:\Users\Teste\AppData\Roaming",
        PastaAreaTrabalho = @"C:\Users\Teste\Desktop",
        PastaDadosComuns = @"C:\ProgramData",
        PastaInstalador = @"C:\Temp\Instalador",
    };

    [Fact]
    public void PastaDestino_UsarProgramFilesEONomeDaPasta()
    {
        Assert.Equal(@"C:\Program Files\Tranal Sentinel",
            CaminhosInstalacao.PastaDestino(ContextoDeTeste()));
    }

    [Fact]
    public void ExecutavelInstalado_FicaNaPastaDeDestino()
    {
        Assert.Equal(@"C:\Program Files\Tranal Sentinel\TranalSentinel.exe",
            CaminhosInstalacao.ExecutavelInstalado(ContextoDeTeste()));
    }

    [Fact]
    public void BackupExecutavel_TemExtensaoBak()
    {
        Assert.Equal(@"C:\Program Files\Tranal Sentinel\TranalSentinel.exe.bak",
            CaminhosInstalacao.BackupExecutavel(ContextoDeTeste()));
    }

    [Fact]
    public void AtalhoMenuIniciar_FicaNaPastaDeProgramasDoUsuario()
    {
        Assert.Equal(
            @"C:\Users\Teste\AppData\Roaming\Microsoft\Windows\Start Menu\Programs\TRANAL SENTINEL.lnk",
            CaminhosInstalacao.AtalhoMenuIniciar(ContextoDeTeste()));
    }

    [Fact]
    public void AtalhoAreaTrabalho_FicaNaAreaDeTrabalhoDoUsuario()
    {
        Assert.Equal(@"C:\Users\Teste\Desktop\TRANAL SENTINEL.lnk",
            CaminhosInstalacao.AtalhoAreaTrabalho(ContextoDeTeste()));
    }

    [Fact]
    public void ArquivoLog_FicaEmProgramData()
    {
        Assert.Equal(@"C:\ProgramData\Tranal Sentinel\instalador.log",
            CaminhosInstalacao.ArquivoLog(ContextoDeTeste()));
    }

    [Fact]
    public void PastaConfiguracoes_FicaEmAppDataEEPreservadaNaDesinstalacao()
    {
        Assert.Equal(@"C:\Users\Teste\AppData\Roaming\TranalSentinel",
            CaminhosInstalacao.PastaConfiguracoes(ContextoDeTeste()));
    }

    [Fact]
    public void OArgumentoDeInicializacaoEoQueOAppEntende()
    {
        // A tarefa agendada passa este argumento: é o app que entra escondido
        // na bandeja quando o Windows inicia.
        Assert.Equal("--iniciar-como-sistema", CaminhosInstalacao.ArgumentoIniciarComoSistema);
    }

    [Fact]
    public void DadosDoAtalho_ApontamParaOExecutavelComOIconeDoApp()
    {
        string executavel = @"C:\Program Files\Tranal Sentinel\TranalSentinel.exe";

        DadosAtalho dados = CaminhosInstalacao.DadosDoAtalho(executavel);

        Assert.Equal(executavel, dados.Alvo);
        Assert.Equal(executavel, dados.Icone);
        Assert.Equal(@"C:\Program Files\Tranal Sentinel", dados.DiretorioTrabalho);
        Assert.Equal("TRANAL SENTINEL", dados.Descricao);
    }

    [Fact]
    public void EntradaDesinstalacao_ApontaParaOInstaladorEODesinstalar()
    {
        EntradaDesinstalacao entrada = CaminhosInstalacao.CriarEntradaDesinstalacao(
            @"C:\Program Files\Tranal Sentinel\TranalSentinel.exe",
            @"C:\Program Files\Tranal Sentinel\TranalSetup.exe",
            "0.16.0");

        Assert.Equal("TRANAL SENTINEL", entrada.DisplayName);
        Assert.Equal("0.16.0", entrada.DisplayVersion);
        Assert.Equal("TRANAL", entrada.Publisher);
        Assert.Equal(@"C:\Program Files\Tranal Sentinel", entrada.InstallLocation);
        Assert.Equal(@"C:\Program Files\Tranal Sentinel\TranalSentinel.exe", entrada.DisplayIcon);
        Assert.Equal("\"C:\\Program Files\\Tranal Sentinel\\TranalSetup.exe\" --desinstalar", entrada.UninstallString);
    }

    [Fact]
    public void EntradaDesinstalacao_SemVersaoUsaUmValorPadrao()
    {
        EntradaDesinstalacao entrada = CaminhosInstalacao.CriarEntradaDesinstalacao(
            @"C:\x\TranalSentinel.exe", @"C:\x\TranalSetup.exe", "");

        Assert.Equal("0.0.0", entrada.DisplayVersion);
    }
}
