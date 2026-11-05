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
        string destino = CaminhosInstalacao.PastaDestino(contexto);
        string executavel = CaminhosInstalacao.ExecutavelInstalado(contexto);
        string instalador = CaminhosInstalacao.InstaladorInstalado(contexto);

        var arquivos = new List<ArquivoPlano>();
        foreach (string nome in new[] { CaminhosInstalacao.NomeExecutavel, CaminhosInstalacao.NomeLeiaMe })
        {
            string origem = Path.Combine(pastaPayload, nome);
            string alvo = Path.Combine(destino, nome);

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
            PastaDestino = destino,
            CaminhoExecutavel = executavel,
            CaminhoBackup = CaminhosInstalacao.BackupExecutavel(contexto),
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
        string destino = CaminhosInstalacao.PastaDestino(contexto);

        var arquivos = new List<string>
        {
            Path.Combine(destino, CaminhosInstalacao.NomeExecutavel),
            Path.Combine(destino, CaminhosInstalacao.NomeLeiaMe),
            CaminhosInstalacao.BackupExecutavel(contexto),
            Path.Combine(destino, CaminhosInstalacao.NomeInstalador),
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
            ArquivosRemovidos = arquivos,
            AtalhosRemovidos = atalhos,
            NomeTarefaIniciar = TarefaInicializacao.NomeTarefa,
            TarefaIniciarExiste = _tarefa.Existe(TarefaInicializacao.NomeTarefa),
            ChaveDesinstalacao = CaminhosInstalacao.ChaveDesinstalacao,
            ChaveDesinstalacaoExiste = _registro.ExisteChave(ChaveRegistro.Maquina, CaminhosInstalacao.ChaveDesinstalacao),
            PastaConfiguracoesPreservada = CaminhosInstalacao.PastaConfiguracoes(contexto),
        };
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
