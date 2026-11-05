using System.IO;
using TranalSetup.Dominio;
using TranalSetup.Infra;
using TranalSetup.Servicos;
using Xunit;

namespace TranalSetup.Tests;

/// <summary>
/// Testes da instalação e da desinstalação.
///
/// O registro e os atalhos são dublês em memória e o disco é uma pasta
/// temporária: os testes provam o comportamento (inclusive a idempotência e o
/// caminho de erro) sem escrever no Windows de verdade.
/// </summary>
public class ServicoInstalacaoTestes
{
    private static (ServicoInstalacao Servico, RegistroFalso Registro, AtalhoFalso Atalho, LogFalso Log, TarefaFalso Tarefa)
        Montar(AmbienteTeste ambiente)
    {
        var registro = new RegistroFalso();
        var atalho = new AtalhoFalso();
        var log = new LogFalso();
        var tarefa = new TarefaFalso();
        var servico = new ServicoInstalacao(
            ambiente.Contexto, registro, atalho, tarefa, log, ambiente.CaminhoInstaladorOrigem);
        return (servico, registro, atalho, log, tarefa);
    }

    [Fact]
    public void Instalar_CriaArquivosAtalhosERegistro()
    {
        using var ambiente = new AmbienteTeste();
        ambiente.CriarPayload();
        var (servico, registro, atalho, _, tarefa) = Montar(ambiente);

        ResultadoOperacao resultado = servico.Instalar(ambiente.Opcoes(), ambiente.PastaPayload);

        Assert.True(resultado.Sucesso, resultado.Mensagem);
        Assert.True(File.Exists(ambiente.CaminhoExecutavelInstalado));
        Assert.True(File.Exists(Path.Combine(ambiente.PastaDestino, "LEIA-ME.txt")));
        Assert.True(File.Exists(Path.Combine(ambiente.PastaDestino, "TranalSetup.exe")));

        // atalhos: o do menu Iniciar e o da Área de Trabalho
        Assert.Equal(2, atalho.Quantidade);
        Assert.True(atalho.Existe(CaminhosInstalacao.AtalhoMenuIniciar(ambiente.Contexto)));
        Assert.True(atalho.Existe(CaminhosInstalacao.AtalhoAreaTrabalho(ambiente.Contexto)));

        // inicialização automática: tarefa agendada (não chave Run: o app exige
        // administrador e a chave Run pediria UAC a cada login)
        Assert.True(tarefa.Existe(TarefaInicializacao.NomeTarefa));
        Assert.Equal(ambiente.CaminhoExecutavelInstalado, tarefa.Tarefas[TarefaInicializacao.NomeTarefa].Caminho);
        Assert.Equal(CaminhosInstalacao.ArgumentoIniciarComoSistema,
            tarefa.Tarefas[TarefaInicializacao.NomeTarefa].Argumento);

        // entrada de desinstalação
        Assert.Equal("TRANAL SENTINEL",
            registro.LerTexto(ChaveRegistro.Maquina, CaminhosInstalacao.ChaveDesinstalacao, "DisplayName"));
        string desinstalar = $"\"{Path.Combine(ambiente.PastaDestino, "TranalSetup.exe")}\" --desinstalar";
        Assert.Equal(desinstalar,
            registro.LerTexto(ChaveRegistro.Maquina, CaminhosInstalacao.ChaveDesinstalacao, "UninstallString"));
    }

    [Fact]
    public void Instalar_DuasVezes_NaoDuplicaAtalhosNemValoresDeRegistro()
    {
        using var ambiente = new AmbienteTeste();
        ambiente.CriarPayload();
        var (servico, registro, atalho, _, tarefa) = Montar(ambiente);

        ResultadoOperacao primeiro = servico.Instalar(ambiente.Opcoes(), ambiente.PastaPayload);
        DateTime instaladoEm = File.GetLastWriteTime(ambiente.CaminhoExecutavelInstalado);
        ResultadoOperacao segundo = servico.Instalar(ambiente.Opcoes(), ambiente.PastaPayload);

        Assert.True(primeiro.Sucesso, primeiro.Mensagem);
        Assert.True(segundo.Sucesso, segundo.Mensagem);

        // os atalhos continuam sendo dois (não foram duplicados)
        Assert.Equal(2, atalho.Quantidade);

        // a tarefa agendada continua sendo uma só
        Assert.Single(tarefa.Tarefas);

        // o app continua instalado e não foi reescrito (o payload é igual ao que está lá)
        Assert.True(File.Exists(ambiente.CaminhoExecutavelInstalado));
        Assert.Equal(instaladoEm, File.GetLastWriteTime(ambiente.CaminhoExecutavelInstalado));
        Assert.False(File.Exists(ambiente.CaminhoExecutavelInstalado + ".bak"));
    }

    [Fact]
    public void Instalar_ComPayloadDiferente_SalvaOBackupDoExecutavelAnterior()
    {
        using var ambiente = new AmbienteTeste();
        ambiente.CriarPayload();
        var (servico, _, _, _, _) = Montar(ambiente);

        servico.Instalar(ambiente.Opcoes(), ambiente.PastaPayload);

        // o payload mudou (versão nova)
        File.WriteAllText(Path.Combine(ambiente.PastaPayload, "TranalSentinel.exe"), "conteudo novo do app");
        ResultadoOperacao resultado = servico.Instalar(ambiente.Opcoes(), ambiente.PastaPayload);

        Assert.True(resultado.Sucesso, resultado.Mensagem);
        Assert.Equal("conteudo novo do app", File.ReadAllText(ambiente.CaminhoExecutavelInstalado));
        Assert.Equal("conteudo do app", File.ReadAllText(ambiente.CaminhoExecutavelInstalado + ".bak"));
    }

    [Fact]
    public void Instalar_SemIniciarComWindows_NaoGravaOValorNaChaveRun()
    {
        using var ambiente = new AmbienteTeste();
        ambiente.CriarPayload();
        var (servico, registro, _, _, tarefa) = Montar(ambiente);

        ResultadoOperacao resultado = servico.Instalar(
            new OpcoesInstalacao
            {
                Destino = DestinoInstalacao.Padrao(ambiente.Contexto),
                IniciarComWindows = false,
            },
            ambiente.PastaPayload);

        Assert.True(resultado.Sucesso, resultado.Mensagem);
        Assert.Equal(0, registro.QuantosValores(ChaveRegistro.Usuario, CaminhosInstalacao.ChaveIniciar));
        Assert.Empty(tarefa.Tarefas);
    }

    [Fact]
    public void Instalar_ComPayloadIncompleto_FalhaSemMexerNoDestino()
    {
        using var ambiente = new AmbienteTeste();
        ambiente.CriarPayload(comExecutavel: false);
        var (servico, registro, atalho, _, _) = Montar(ambiente);

        ResultadoOperacao resultado = servico.Instalar(ambiente.Opcoes(), ambiente.PastaPayload);

        Assert.False(resultado.Sucesso);
        Assert.Contains("TranalSentinel.exe", resultado.Mensagem, StringComparison.Ordinal);
        Assert.False(File.Exists(ambiente.CaminhoExecutavelInstalado));
        Assert.Equal(0, atalho.Quantidade);
        Assert.Equal(0, registro.QuantosValores(ChaveRegistro.Usuario, CaminhosInstalacao.ChaveIniciar));
    }

    [Fact]
    public void Instalar_QuandoOAtalhoFalha_RestauraOExecutavelAnterior()
    {
        using var ambiente = new AmbienteTeste();
        ambiente.CriarPayload();

        // já existe uma versão instalada
        Directory.CreateDirectory(ambiente.PastaDestino);
        File.WriteAllText(ambiente.CaminhoExecutavelInstalado, "versao antiga");

        var (servico, registro, atalho, _, tarefa) = Montar(ambiente);
        atalho.FalharAoCriarEsteCaminho = "Desktop";

        ResultadoOperacao resultado = servico.Instalar(ambiente.Opcoes(), ambiente.PastaPayload);

        Assert.False(resultado.Sucesso);

        // o executável anterior voltou intacto
        Assert.Equal("versao antiga", File.ReadAllText(ambiente.CaminhoExecutavelInstalado));

        // o atalho do menu Iniciar (criado antes da falha) foi desfeito
        Assert.Equal(0, atalho.Quantidade);

        // nada ficou no registro
        Assert.Equal(0, registro.QuantosValores(ChaveRegistro.Usuario, CaminhosInstalacao.ChaveIniciar));
        Assert.False(registro.ExisteChave(ChaveRegistro.Maquina, CaminhosInstalacao.ChaveDesinstalacao));
    }

    [Fact]
    public void Instalar_QuandoAEntradaDeDesinstalacaoFalha_DesfazOValorDeInicializacao()
    {
        using var ambiente = new AmbienteTeste();
        ambiente.CriarPayload();
        var (servico, registro, _, _, tarefa) = Montar(ambiente);

        // a gravação em HKLM (entrada de desinstalação) falha
        registro.FalharAoGravarNestaChave = "Uninstall";

        ResultadoOperacao resultado = servico.Instalar(ambiente.Opcoes(), ambiente.PastaPayload);

        Assert.False(resultado.Sucesso);

        // a tarefa agendada foi desfeita
        Assert.Empty(tarefa.Tarefas);
    }

    [Fact]
    public void Desinstalar_RemoveArquivosAtalhosEEntradasDeRegistro()
    {
        using var ambiente = new AmbienteTeste();
        ambiente.CriarPayload();
        var (servico, registro, atalho, _, tarefa) = Montar(ambiente);

        servico.Instalar(ambiente.Opcoes(), ambiente.PastaPayload);
        ResultadoOperacao resultado = servico.Desinstalar();

        Assert.True(resultado.Sucesso, resultado.Mensagem);
        Assert.False(File.Exists(ambiente.CaminhoExecutavelInstalado));
        Assert.False(File.Exists(Path.Combine(ambiente.PastaDestino, "LEIA-ME.txt")));
        Assert.False(File.Exists(Path.Combine(ambiente.PastaDestino, "TranalSetup.exe")));
        Assert.Equal(0, atalho.Quantidade);
        Assert.Equal(0, registro.QuantosValores(ChaveRegistro.Usuario, CaminhosInstalacao.ChaveIniciar));
        Assert.False(registro.ExisteChave(ChaveRegistro.Maquina, CaminhosInstalacao.ChaveDesinstalacao));

        // a tarefa agendada também some
        Assert.Empty(tarefa.Tarefas);
    }

    [Fact]
    public void Desinstalar_PreservaAsConfiguracoesDoUsuario()
    {
        using var ambiente = new AmbienteTeste();
        ambiente.CriarPayload();
        var (servico, _, _, log, _) = Montar(ambiente);

        // as configurações do usuário estão no %AppData%
        string pastaConfig = CaminhosInstalacao.PastaConfiguracoes(ambiente.Contexto);
        Directory.CreateDirectory(pastaConfig);
        string arquivo = Path.Combine(pastaConfig, "settings.json");
        File.WriteAllText(arquivo, "{}");

        servico.Instalar(ambiente.Opcoes(), ambiente.PastaPayload);
        ResultadoOperacao resultado = servico.Desinstalar();

        Assert.True(resultado.Sucesso, resultado.Mensagem);
        Assert.True(File.Exists(arquivo));
        Assert.Contains(log.Linhas, l => l.Contains("preservadas", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Instalar_EmPastaNova_TrocaDePastaERemoveAInstalacaoAntiga()
    {
        using var ambiente = new AmbienteTeste();
        ambiente.CriarPayload();
        var (servico, registro, _, _, _) = Montar(ambiente);

        string pastaAntiga = Path.Combine(ambiente.Raiz, "Lugar antigo", "Tranal Sentinel");
        string pastaNova = Path.Combine(ambiente.Raiz, "Lugar novo", "Tranal Sentinel");

        // primeira instalação no lugar antigo
        Assert.True(servico.Instalar(ambiente.Opcoes(pastaAntiga), ambiente.PastaPayload).Sucesso);
        Assert.True(File.Exists(Path.Combine(pastaAntiga, "TranalSentinel.exe")));

        // segunda instalação no lugar novo
        ResultadoOperacao resultado = servico.Instalar(ambiente.Opcoes(pastaNova), ambiente.PastaPayload);

        Assert.True(resultado.Sucesso, resultado.Mensagem);
        Assert.True(File.Exists(Path.Combine(pastaNova, "TranalSentinel.exe")));

        // a instalação antiga foi removida: trocar de pasta é um movimento,
        // não uma cópia (o app antigo não pode ficar órfão).
        Assert.False(File.Exists(Path.Combine(pastaAntiga, "TranalSentinel.exe")));
        Assert.False(File.Exists(Path.Combine(pastaAntiga, "LEIA-ME.txt")));
        Assert.False(File.Exists(Path.Combine(pastaAntiga, "TranalSetup.exe")));
        Assert.False(Directory.Exists(pastaAntiga));

        // e o registro aponta para o lugar novo
        Assert.Equal(pastaNova,
            registro.LerTexto(ChaveRegistro.Maquina, CaminhosInstalacao.ChaveDesinstalacao, "InstallLocation"));
    }

    [Fact]
    public void Instalar_NaMesmaPasta_NaoRemoveNemCriaDuplicado()
    {
        using var ambiente = new AmbienteTeste();
        ambiente.CriarPayload();
        var (servico, _, _, _, _) = Montar(ambiente);

        string pasta = Path.Combine(ambiente.Raiz, "Um lugar", "Tranal Sentinel");

        Assert.True(servico.Instalar(ambiente.Opcoes(pasta), ambiente.PastaPayload).Sucesso);
        ResultadoOperacao segunda = servico.Instalar(ambiente.Opcoes(pasta), ambiente.PastaPayload);

        Assert.True(segunda.Sucesso, segunda.Mensagem);
        Assert.True(File.Exists(Path.Combine(pasta, "TranalSentinel.exe")));
        Assert.True(File.Exists(Path.Combine(pasta, "LEIA-ME.txt")));
    }

    [Fact]
    public void Desinstalar_SemInstalacaoNaoFalha()
    {
        using var ambiente = new AmbienteTeste();
        var (servico, _, _, _, _) = Montar(ambiente);

        ResultadoOperacao resultado = servico.Desinstalar();

        Assert.True(resultado.Sucesso, resultado.Mensagem);
    }

    [Fact]
    public void Instalar_EmPastaEscolhida_ColocaOsArquivosLá()
    {
        using var ambiente = new AmbienteTeste();
        ambiente.CriarPayload();
        var (servico, registro, atalho, _, tarefa) = Montar(ambiente);

        string pastaEscolhida = Path.Combine(ambiente.Raiz, "Outro lugar", "Tranal Sentinel");
        ResultadoOperacao resultado = servico.Instalar(ambiente.Opcoes(pastaEscolhida), ambiente.PastaPayload);

        Assert.True(resultado.Sucesso, resultado.Mensagem);

        // os arquivos foram para a pasta escolhida...
        Assert.True(File.Exists(Path.Combine(pastaEscolhida, "TranalSentinel.exe")));
        Assert.True(File.Exists(Path.Combine(pastaEscolhida, "LEIA-ME.txt")));
        Assert.True(File.Exists(Path.Combine(pastaEscolhida, "TranalSetup.exe")));

        // ...e nada foi para a pasta padrão
        Assert.False(File.Exists(ambiente.CaminhoExecutavelInstalado));
        Assert.False(Directory.Exists(ambiente.PastaDestino));

        // o atalho e a tarefa agendada apontam para o executável novo
        string executavel = Path.Combine(pastaEscolhida, "TranalSentinel.exe");
        Assert.Equal(executavel, tarefa.Tarefas[TarefaInicializacao.NomeTarefa].Caminho);
        Assert.Equal(executavel,
            atalho.Atalhos[CaminhosInstalacao.AtalhoMenuIniciar(ambiente.Contexto)].Alvo);

        // o registro guarda onde a instalação ficou
        Assert.Equal(pastaEscolhida,
            registro.LerTexto(ChaveRegistro.Maquina, CaminhosInstalacao.ChaveDesinstalacao, "InstallLocation"));
    }

    [Fact]
    public void Desinstalar_AchaOLocalInstaladoPelaEntradaDeDesinstalacao()
    {
        using var ambiente = new AmbienteTeste();
        ambiente.CriarPayload();
        var (servico, registro, atalho, _, tarefa) = Montar(ambiente);

        string pastaEscolhida = Path.Combine(ambiente.Raiz, "Outro lugar", "Tranal Sentinel");
        servico.Instalar(ambiente.Opcoes(pastaEscolhida), ambiente.PastaPayload);

        ResultadoOperacao resultado = servico.Desinstalar();

        Assert.True(resultado.Sucesso, resultado.Mensagem);

        // os arquivos da pasta escolhida sumiram
        Assert.False(File.Exists(Path.Combine(pastaEscolhida, "TranalSentinel.exe")));
        Assert.False(File.Exists(Path.Combine(pastaEscolhida, "LEIA-ME.txt")));
        Assert.False(File.Exists(Path.Combine(pastaEscolhida, "TranalSetup.exe")));
        Assert.False(Directory.Exists(pastaEscolhida));

        // atalhos, tarefa agendada e registro também
        Assert.Equal(0, atalho.Quantidade);
        Assert.Empty(tarefa.Tarefas);
        Assert.False(registro.ExisteChave(ChaveRegistro.Maquina, CaminhosInstalacao.ChaveDesinstalacao));
    }

    [Fact]
    public void Instalar_EmPastaEscolhida_DuasVezes_NaoDuplicaNada()
    {
        using var ambiente = new AmbienteTeste();
        ambiente.CriarPayload();
        var (servico, registro, atalho, _, tarefa) = Montar(ambiente);

        string pastaEscolhida = Path.Combine(ambiente.Raiz, "Outro lugar", "Tranal Sentinel");

        ResultadoOperacao primeiro = servico.Instalar(ambiente.Opcoes(pastaEscolhida), ambiente.PastaPayload);
        ResultadoOperacao segundo = servico.Instalar(ambiente.Opcoes(pastaEscolhida), ambiente.PastaPayload);

        Assert.True(primeiro.Sucesso, primeiro.Mensagem);
        Assert.True(segundo.Sucesso, segundo.Mensagem);
        Assert.Equal(2, atalho.Quantidade);
        Assert.Single(tarefa.Tarefas);
        Assert.False(File.Exists(Path.Combine(pastaEscolhida, "TranalSentinel.exe.bak")));
    }
}
