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
    public void Erro_ComPastaDeRede_Recusa()
    {
        // Instalar o app numa pasta de rede quebraria a tarefa agendada e o
        // desinstalador (dependeriam da rede estar montada).
        Assert.Contains("rede", DestinoInstalacao.Erro(@"\\servidor\pasta") ?? "", StringComparison.OrdinalIgnoreCase);
        Assert.Contains("rede", DestinoInstalacao.Erro(@"\\servidor\pasta\sub") ?? "", StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Erro_ComPastaDeRedeComBarraNormal_Recusa()
    {
        // "//servidor\pasta\sub" vira "\\servidor\pasta\sub" quando normalizado:
        // a checagem tem que ser feita depois de normalizar, senão escapa.
        Assert.Contains("rede", DestinoInstalacao.Erro("//servidor/pasta/sub") ?? "", StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Erro_ComCaminhoDoWindows_Recusa()
    {
        // "\\?\C:\..." e "\\.\C:\..." não são pastas de rede, mas também não
        // são pastas onde se instala um app.
        Assert.NotNull(DestinoInstalacao.Erro(@"\\?\C:\Apps"));
        Assert.NotNull(DestinoInstalacao.Erro(@"\\.\C:\Apps"));
    }

    [Fact]
    public void Erro_ComCaminhoLongoDemais_Recusa()
    {
        // O Windows ainda tem o limite de 260 caracteres: um caminho mais longo
        // passa na tela e só falha na hora de criar a pasta.
        string longo = @"C:\" + new string('p', 250);

        Assert.Contains("longo", DestinoInstalacao.Erro(longo) ?? "", StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Erro_ComCaminhoEnorme_NaoEstoura()
    {
        // O defeito: o PathTooLongException escapava da validação e derrubava a
        // tela (o campo de texto dispara a validação a cada tecla).
        string enorme = @"C:\" + new string('a', 32_766);

        Exception? falha = null;
        try
        {
            DestinoInstalacao.Erro(enorme);
            DestinoInstalacao.DaEscolha(enorme);
        }
        catch (PathTooLongException ex)
        {
            falha = ex;
        }
        catch (ArgumentException)
        {
            // Recusa normal: o caminho não serve. Tudo bem.
        }

        Assert.Null(falha);
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
