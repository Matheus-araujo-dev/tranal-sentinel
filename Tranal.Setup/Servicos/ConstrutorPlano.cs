using System.Diagnostics;
using System.IO;
using TranalSetup.Dominio;
using TranalSetup.Infra;

namespace TranalSetup.Servicos;

/// <summary>
/// Monta o plano da instalação e da desinstalação.
///
/// O plano é calculado olhando o disco e o registro (só leitura) e é a mesma
/// informação usada tanto pela instalação real quanto pelo modo --simular:
/// o que o usuário vê no plano é exatamente o que o instalador vai fazer.
/// </summary>
public sealed class ConstrutorPlano
{
    private readonly IRegistro _registro;
    private readonly ITarefaAgendada _tarefa;

    /// <summary>Cria o construtor com o acesso ao registro e ao Agendador de Tarefas.</summary>
    public ConstrutorPlano(IRegistro registro, ITarefaAgendada tarefa)
    {
        _registro = registro;
        _tarefa = tarefa;
    }

    /// <summary>Calcula o plano da instalação para o payload e as opções informados.</summary>
    public PlanoInstalacao MontarPlanoInstalacao(ContextoSistema contexto, OpcoesInstalacao opcoes, string pastaPayload)
    {
        DestinoInstalacao destino = opcoes.Destino;
        string pasta = destino.Pasta;
        string executavel = CaminhosInstalacao.ExecutavelInstalado(pasta);
        string instalador = CaminhosInstalacao.InstaladorInstalado(pasta);

        var arquivos = new List<ArquivoPlano>();
        foreach (string nome in new[] { CaminhosInstalacao.NomeExecutavel, CaminhosInstalacao.NomeLeiaMe })
        {
            string origem = Path.Combine(pastaPayload, nome);
            string alvo = Path.Combine(pasta, nome);

            FileInfo? infoOrigem = Existe(origem);
            FileInfo? infoDestino = Existe(alvo);

            SituacaoArquivo situacao = ComparadorPayload.Comparar(
                infoOrigem is not null, infoOrigem?.Length ?? 0, infoOrigem?.LastWriteTime ?? default,
                infoDestino is not null, infoDestino?.Length ?? 0, infoDestino?.LastWriteTime ?? default);

            arquivos.Add(new ArquivoPlano(
                nome, origem, alvo, situacao,
                infoOrigem?.Length ?? 0, infoOrigem?.LastWriteTime ?? default,
                infoDestino?.Length ?? 0, infoDestino?.LastWriteTime ?? default));
        }

        var atalhos = new List<PlanoAtalho>
        {
            new(CaminhosInstalacao.AtalhoMenuIniciar(contexto), "Menu Iniciar",
                CaminhosInstalacao.DadosDoAtalho(executavel), true),
        };
        if (opcoes.AtalhoAreaTrabalho)
        {
            atalhos.Add(new PlanoAtalho(CaminhosInstalacao.AtalhoAreaTrabalho(contexto), "Área de Trabalho",
                CaminhosInstalacao.DadosDoAtalho(executavel), true));
        }

        string versao = VersaoDoPayload(Path.Combine(pastaPayload, CaminhosInstalacao.NomeExecutavel));

        var plano = new PlanoInstalacao
        {
            Contexto = contexto,
            PastaPayload = pastaPayload,
            Opcoes = opcoes,
            Destino = destino,
            CaminhoExecutavel = executavel,
            CaminhoBackup = CaminhosInstalacao.BackupExecutavel(pasta),
            CaminhoInstaladorInstalado = instalador,
            Arquivos = arquivos,
            Atalhos = atalhos,
            NomeTarefaIniciar = opcoes.IniciarComWindows ? TarefaInicializacao.NomeTarefa : null,
            DescricaoTarefaIniciar = opcoes.IniciarComWindows
                ? TarefaInicializacao.Descrever(executavel, CaminhosInstalacao.ArgumentoIniciarComoSistema)
                : null,
            EntradaDesinstalacao = CaminhosInstalacao.CriarEntradaDesinstalacao(executavel, instalador, versao),
            InstalacaoExistente = File.Exists(executavel),
            PayloadCompleto = arquivos.All(a => a.Situacao != SituacaoArquivo.AusenteNoPayload),
            VersaoPayload = versao,
        };
        return plano;
    }

    /// <summary>Calcula o plano da desinstalação.</summary>
    public PlanoDesinstalacao MontarPlanoDesinstalacao(ContextoSistema contexto)
    {
        DestinoInstalacao destino = DescobrirDestinoInstalado(contexto);
        string pasta = destino.Pasta;

        var arquivos = new List<string>
        {
            CaminhosInstalacao.ExecutavelInstalado(pasta),
            Path.Combine(pasta, CaminhosInstalacao.NomeLeiaMe),
            CaminhosInstalacao.BackupExecutavel(pasta),
            CaminhosInstalacao.InstaladorInstalado(pasta),
        };

        var atalhos = new List<string>
        {
            CaminhosInstalacao.AtalhoMenuIniciar(contexto),
            CaminhosInstalacao.AtalhoAreaTrabalho(contexto),
        };

        string? valorAtual = _registro.LerTexto(
            ChaveRegistro.Usuario, CaminhosInstalacao.ChaveIniciar, CaminhosInstalacao.NomeValorIniciar);

        return new PlanoDesinstalacao
        {
            Contexto = contexto,
            Destino = destino,
            ArquivosRemovidos = arquivos,
            AtalhosRemovidos = atalhos,
            NomeTarefaIniciar = TarefaInicializacao.NomeTarefa,
            TarefaIniciarExiste = _tarefa.Existe(TarefaInicializacao.NomeTarefa),
            ChaveDesinstalacao = CaminhosInstalacao.ChaveDesinstalacao,
            ChaveDesinstalacaoExiste = _registro.ExisteChave(ChaveRegistro.Maquina, CaminhosInstalacao.ChaveDesinstalacao),
            PastaConfiguracoesPreservada = CaminhosInstalacao.PastaConfiguracoes(contexto),
        };
    }

    /// <summary>
    /// Descobre onde o app está instalado.
    ///
    /// A ordem é: primeiro a entrada de desinstalação (HKLM), que guarda o
    /// InstallLocation de verdade; depois a pasta de onde este instalador está
    /// rodando (é de lá que o Windows chama a desinstalação); sem nenhum dos
    /// dois, vale o destino padrão.
    /// </summary>
    private DestinoInstalacao DescobrirDestinoInstalado(ContextoSistema contexto)
    {
        string? registrado = _registro.LerTexto(
            ChaveRegistro.Maquina, CaminhosInstalacao.ChaveDesinstalacao, "InstallLocation");

        DestinoInstalacao? peloRegistro = TentarDestino(registrado);
        if (peloRegistro is not null) return peloRegistro;

        string? processo = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(processo))
        {
            string pastaDoProcesso = Path.GetDirectoryName(processo) ?? string.Empty;
            if (!string.IsNullOrEmpty(pastaDoProcesso)
                && File.Exists(Path.Combine(pastaDoProcesso, CaminhosInstalacao.NomeExecutavel)))
            {
                DestinoInstalacao? pelaPasta = TentarDestino(pastaDoProcesso);
                if (pelaPasta is not null) return pelaPasta;
            }
        }

        return DestinoInstalacao.Padrao(contexto);
    }

    /// <summary>Tenta transformar um caminho em destino; devolve null se o caminho não serve.</summary>
    private static DestinoInstalacao? TentarDestino(string? caminho)
    {
        if (string.IsNullOrWhiteSpace(caminho)) return null;

        try
        {
            return DestinoInstalacao.DaEscolha(caminho);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static FileInfo? Existe(string caminho)
        => File.Exists(caminho) ? new FileInfo(caminho) : null;

    /// <summary>
    /// Versão do app no payload, lida do executável. Serve para a entrada de
    /// desinstalação ("Apps e recursos") mostrar a versão certa.
    /// </summary>
    private static string VersaoDoPayload(string caminhoExecutavel)
    {
        try
        {
            if (!File.Exists(caminhoExecutavel)) return "";
            var versao = FileVersionInfo.GetVersionInfo(caminhoExecutavel).FileVersion;
            return string.IsNullOrWhiteSpace(versao) ? "" : versao.Trim();
        }
        catch (Exception ex)
        {
            // Versão é só informação: se não der para ler, a instalação continua.
            Debug.WriteLine($"[instalador] não foi possível ler a versão do payload: {ex.Message}");
            return "";
        }
    }
}
