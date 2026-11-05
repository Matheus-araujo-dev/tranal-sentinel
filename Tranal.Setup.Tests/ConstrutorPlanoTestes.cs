using System.IO;
using TranalSetup.Dominio;
using TranalSetup.Infra;
using TranalSetup.Servicos;
using Xunit;

namespace TranalSetup.Tests;

/// <summary>
/// Testes da regra que descobre onde o TRANAL SENTINEL está instalado e do
/// comando que adia a remoção de um arquivo que está em uso.
///
/// O registro é o dublê em memória: nada aqui toca no Windows de verdade.
/// </summary>
public class ConstrutorPlanoTestes
{
    private static (ConstrutorPlano Construtor, RegistroFalso Registro) Montar()
    {
        var registro = new RegistroFalso();
        return (new ConstrutorPlano(registro, new TarefaFalso()), registro);
    }

    [Fact]
    public void PastaInstalada_SemRegistro_NaoTemInstalacao()
    {
        (ConstrutorPlano construtor, _) = Montar();

        Assert.Null(construtor.PastaInstalada());
    }

    [Fact]
    public void PastaInstalada_ComRegistroEExecutavel_AchaAPasta()
    {
        using var ambiente = new AmbienteTeste();
        (ConstrutorPlano construtor, RegistroFalso registro) = Montar();

        string pasta = Path.Combine(ambiente.Raiz, "Instalado", "Tranal Sentinel");
        Directory.CreateDirectory(pasta);
        File.WriteAllText(Path.Combine(pasta, "TranalSentinel.exe"), "app");

        registro.GravarTexto(
            ChaveRegistro.Maquina, CaminhosInstalacao.ChaveDesinstalacao, "InstallLocation", pasta);

        Assert.Equal(pasta, construtor.PastaInstalada());
    }

    [Fact]
    public void PastaInstalada_ComRegistroMasSemExecutavel_NaoTemInstalacao()
    {
        using var ambiente = new AmbienteTeste();
        (ConstrutorPlano construtor, RegistroFalso registro) = Montar();

        // uma pasta que com certeza não tem o app instalado
        registro.GravarTexto(
            ChaveRegistro.Maquina, CaminhosInstalacao.ChaveDesinstalacao,
            "InstallLocation", Path.Combine(ambiente.Raiz, "NuncaInstalado", "Tranal Sentinel"));

        Assert.Null(construtor.PastaInstalada());
    }

    [Fact]
    public void PastaDeInstalacaoAnterior_NaMesmaPasta_NaoHaAnterior()
    {
        using var ambiente = new AmbienteTeste();
        (ConstrutorPlano construtor, RegistroFalso registro) = Montar();

        string pasta = Path.Combine(ambiente.Raiz, "Instalado", "Tranal Sentinel");
        Directory.CreateDirectory(pasta);
        File.WriteAllText(Path.Combine(pasta, "TranalSentinel.exe"), "app");

        registro.GravarTexto(
            ChaveRegistro.Maquina, CaminhosInstalacao.ChaveDesinstalacao, "InstallLocation", pasta);

        Assert.Null(construtor.PastaDeInstalacaoAnterior(pasta));
    }

    [Fact]
    public void PastaDeInstalacaoAnterior_EmOutraPasta_Acha()
    {
        using var ambiente = new AmbienteTeste();
        (ConstrutorPlano construtor, RegistroFalso registro) = Montar();

        string antiga = Path.Combine(ambiente.Raiz, "Antigo", "Tranal Sentinel");
        Directory.CreateDirectory(antiga);
        File.WriteAllText(Path.Combine(antiga, "TranalSentinel.exe"), "app");

        registro.GravarTexto(
            ChaveRegistro.Maquina, CaminhosInstalacao.ChaveDesinstalacao, "InstallLocation", antiga);

        string nova = Path.Combine(ambiente.Raiz, "Novo", "Tranal Sentinel");

        Assert.Equal(antiga, construtor.PastaDeInstalacaoAnterior(nova));
    }

    [Fact]
    public void RemocaoAdiada_OComandoSoApagaOArquivoAgendado()
    {
        // Se o lugar for preenchido de novo (reinstalação) antes do laço
        // rodar, o arquivo novo NÃO pode ser apagado: por isso a comparação
        // de data e tamanho.
        using var ambiente = new AmbienteTeste();
        string arquivo = Path.Combine(ambiente.Raiz, "app.exe");
        File.WriteAllText(arquivo, "conteudo");

        string comando = RemocaoAdiada.GerarComando(arquivo);

        Assert.Contains("LastWriteTimeUtc.Ticks", comando, StringComparison.Ordinal);
        Assert.Contains("-ne $ticks", comando, StringComparison.Ordinal);
        Assert.Contains("-ne $tamanho", comando, StringComparison.Ordinal);
        Assert.Contains(arquivo, comando, StringComparison.Ordinal);
    }

    [Fact]
    public void RemocaoAdiada_ComApostrofoNoCaminho_NAoFechaAAspa()
    {
        // O comando do PowerShell usa aspas simples: um apóstrofo no caminho
        // fecharia a string, então é duplicado.
        string comando = RemocaoAdiada.GerarComando(@"C:\O'Brien e Cia\app.exe");

        Assert.Contains("O''Brien", comando, StringComparison.Ordinal);
    }
}
