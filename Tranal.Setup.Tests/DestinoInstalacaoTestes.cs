using System.IO;
using TranalSetup.Dominio;
using Xunit;

namespace TranalSetup.Tests;

/// <summary>
/// Testes do destino da instalação: onde o TRANAL SENTINEL vai ficar.
///
/// Este é o lugar único que decide isso, então os testes cobrem as três
/// entradas possíveis: o destino padrão, a escolha do usuário (com o caminho
/// como ele foi digitado) e o botão que escolhe pasta (que cria a subpasta
/// do produto dentro da pasta escolhida).
/// </summary>
public class DestinoInstalacaoTestes
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
    public void Padrao_EPastaDeProgramFilesComONomeDoProduto()
    {
        Assert.Equal(@"C:\Program Files\Tranal Sentinel",
            DestinoInstalacao.Padrao(ContextoDeTeste()).Pasta);
    }

    [Fact]
    public void DaEscolha_UsaOCaminhoComoFoiDigitado()
    {
        DestinoInstalacao destino = DestinoInstalacao.DaEscolha(@"D:\Meus Programas\Apps");

        Assert.Equal(@"D:\Meus Programas\Apps", destino.Pasta);
    }

    [Fact]
    public void DaEscolha_TiraABarraDoFinal()
    {
        DestinoInstalacao destino = DestinoInstalacao.DaEscolha(@"D:\Apps\");

        Assert.Equal(@"D:\Apps", destino.Pasta);
    }

    [Fact]
    public void DaEscolha_ComCaminhoInvalido_Recusa()
    {
        Assert.Throws<ArgumentException>(() => DestinoInstalacao.DaEscolha("Apps"));
        Assert.Throws<ArgumentException>(() => DestinoInstalacao.DaEscolha(""));
        Assert.Throws<ArgumentException>(() => DestinoInstalacao.DaEscolha(@"C:\"));
    }

    [Fact]
    public void Erro_ComCaminhoValido_NaoAchaNada()
    {
        Assert.Null(DestinoInstalacao.Erro(@"D:\Meus Programas\Apps"));
    }

    [Fact]
    public void Erro_ComCaminhoVazio_AvisaQueFaltaEscolher()
    {
        Assert.Contains("Escolha a pasta", DestinoInstalacao.Erro("") ?? "", StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Erro_ComCaminhoRelativo_AvisaQuePrecisaSerCompleto()
    {
        Assert.Contains("completo", DestinoInstalacao.Erro(@"Apps\Tranal") ?? "", StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Erro_ComRaizDaUnidade_AvisaQueNaoPodeSerARaiz()
    {
        Assert.Contains("raiz", DestinoInstalacao.Erro(@"C:\") ?? "", StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Erro_ComCaractereProibido_Avisa()
    {
        Assert.Contains("caracteres", DestinoInstalacao.Erro(@"C:\Apps\*") ?? "", StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PastaDentroDe_CriaASubpastaDoProduto()
    {
        Assert.Equal(@"D:\Meus Programas\Tranal Sentinel",
            DestinoInstalacao.PastaDentroDe(@"D:\Meus Programas"));
    }

    [Fact]
    public void PastaDentroDe_DeixandoAColherBarraNoFinal()
    {
        Assert.Equal(@"D:\Meus Programas\Tranal Sentinel",
            DestinoInstalacao.PastaDentroDe(@"D:\Meus Programas\"));
    }

    [Fact]
    public void PastaDentroDe_NaoCriaASubpastaDuasVezes()
    {
        // Escolher a pasta que já é a do produto não vira
        // "Tranal Sentinel\Tranal Sentinel".
        Assert.Equal(@"D:\Meus Programas\Tranal Sentinel",
            DestinoInstalacao.PastaDentroDe(@"D:\Meus Programas\Tranal Sentinel"));
    }

    [Fact]
    public void PastaDentroDe_NaRaizDaUnidade_CriaASubpastaDoProduto()
    {
        Assert.Equal(@"C:\Tranal Sentinel", DestinoInstalacao.PastaDentroDe(@"C:\"));
    }

    [Fact]
    public void DestinoValido_NuncaEhRelativo()
    {
        DestinoInstalacao destino = DestinoInstalacao.DaEscolha(@"D:\Apps");

        Assert.True(Path.IsPathFullyQualified(destino.Pasta));
    }
}
