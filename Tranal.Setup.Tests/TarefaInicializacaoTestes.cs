using System.Xml.Linq;
using TranalSetup.Dominio;
using Xunit;

namespace TranalSetup.Tests;

/// <summary>
/// Testes da tarefa agendada de inicialização.
///
/// O XML é gerado por uma função pura justamente para poder ser testado sem
/// mexer no Agendador de Tarefas do Windows.
/// </summary>
public class TarefaInicializacaoTestes
{
    private const string Executavel = @"C:\Program Files\Tranal Sentinel\TranalSentinel.exe";
    private const string Usuario = @"PC\matheus";

    private static XDocument Documento(string xml) => XDocument.Parse(xml);

    [Fact]
    public void OXmlEValido()
    {
        string xml = TarefaInicializacao.GerarXml(Executavel, "--iniciar-como-sistema", Usuario);
        var documento = Documento(xml);

        Assert.Equal("Task", documento.Root!.Name.LocalName);
    }

    [Fact]
    public void RodaNoLoginDoUsuario()
    {
        string xml = TarefaInicializacao.GerarXml(Executavel, "--iniciar-como-sistema", Usuario);
        var documento = Documento(xml);

        var gatilho = documento.Descendants().First(e => e.Name.LocalName == "LogonTrigger");
        var usuario = gatilho.Descendants().First(e => e.Name.LocalName == "UserId").Value;
        Assert.Equal(Usuario, usuario);
        Assert.Equal("true", gatilho.Descendants().First(e => e.Name.LocalName == "Enabled").Value);
    }

    [Fact]
    public void SobeComPrivilegioMaximoParaNaoPedirUacNoLogin()
    {
        string xml = TarefaInicializacao.GerarXml(Executavel, "--iniciar-como-sistema", Usuario);
        var documento = Documento(xml);

        var principal = documento.Descendants().First(e => e.Name.LocalName == "Principal");
        Assert.Equal("HighestAvailable", principal.Descendants().First(e => e.Name.LocalName == "RunLevel").Value);
        Assert.Equal("InteractiveToken", principal.Descendants().First(e => e.Name.LocalName == "LogonType").Value);
    }

    [Fact]
    public void AExecucaoChamaOExecutavelComOArgumento()
    {
        string xml = TarefaInicializacao.GerarXml(Executavel, "--iniciar-como-sistema", Usuario);
        var documento = Documento(xml);

        var acao = documento.Descendants().First(e => e.Name.LocalName == "Exec");
        Assert.Equal($"\"{Executavel}\"", acao.Descendants().First(e => e.Name.LocalName == "Command").Value);
        Assert.Equal("--iniciar-como-sistema", acao.Descendants().First(e => e.Name.LocalName == "Arguments").Value);
        Assert.Equal(@"C:\Program Files\Tranal Sentinel",
            acao.Descendants().First(e => e.Name.LocalName == "WorkingDirectory").Value);
    }

    [Fact]
    public void OXmlEscapaCaracteresQueEleNaoAceita()
    {
        string xml = TarefaInicializacao.GerarXml(@"C:\A&B\C.exe", "--x", @"PC\a&b");

        // precisa continuar sendo um XML válido mesmo com & no caminho
        var documento = Documento(xml);
        var comando = documento.Descendants().First(e => e.Name.LocalName == "Command").Value;
        Assert.Contains("&amp;", xml, StringComparison.Ordinal);
        Assert.Equal("\"C:\\A&B\\C.exe\"", comando);
    }

    [Fact]
    public void ADescricaoDizQueNaoHaUacNoLogin()
    {
        string descricao = TarefaInicializacao.Descrever(Executavel, "--iniciar-como-sistema");

        Assert.Contains(TarefaInicializacao.NomeTarefa, descricao, StringComparison.Ordinal);
        Assert.Contains(Executavel, descricao, StringComparison.Ordinal);
        Assert.Contains("sem UAC", descricao, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void OUsuarioAtualTemDominioEConta()
    {
        string usuario = TarefaInicializacao.UsuarioAtual();

        Assert.Contains(Environment.UserName, usuario, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\\", usuario, StringComparison.Ordinal);
    }
}
