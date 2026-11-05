using System.IO;
using TranalSetup.Dominio;
using TranalSetup.Infra;

namespace TranalSetup.Servicos;

/// <summary>
/// Executa a instalação e a desinstalação do TRANAL SENTINEL.
///
/// A instalação é feita na ordem: arquivos, atalhos, registro. Cada etapa
/// Registers o que precisa ser desfeito; se alguma falha, tudo é desfeito e a
/// instalação devolve erro — nunca sobra um app quebrado no destino.
/// </summary>
public sealed class ServicoInstalacao
{
    private readonly ContextoSistema _contexto;
    private readonly IRegistro _registro;
    private readonly IAtalho _atalho;
    private readonly ITarefaAgendada _tarefa;
    private readonly ILog _log;
    private readonly string? _caminhoInstalador;

    /// <summary>
    /// Cria o serviço com o contexto do sistema e as dependências de I/O.
    /// O caminho do instalador pode ser informado para os testes (senão é o
    /// executável atual, que é o que será copiado para a pasta de instalação).
    /// </summary>
    public ServicoInstalacao(ContextoSistema contexto, IRegistro registro, IAtalho atalho,
        ITarefaAgendada tarefa, ILog log, string? caminhoInstalador = null)
    {
        _contexto = contexto;
        _registro = registro;
        _atalho = atalho;
        _tarefa = tarefa;
        _log = log;
        _caminhoInstalador = caminhoInstalador;
    }

    /// <summary>Instala o app do payload informado, com as opções escolhidas.</summary>
    public ResultadoOperacao Instalar(OpcoesInstalacao opcoes, string pastaPayload)
    {
        var plano = new ConstrutorPlano(_registro, _tarefa).MontarPlanoInstalacao(_contexto, opcoes, pastaPayload);
        return ExecutarInstalacao(plano);
    }

    /// <summary>Desinstala o app: arquivos, atalhos e as entradas de registro.</summary>
    public ResultadoOperacao Desinstalar()
    {
        var plano = new ConstrutorPlano(_registro, _tarefa).MontarPlanoDesinstalacao(_contexto);
        return ExecutarDesinstalacao(plano);
    }

    private ResultadoOperacao ExecutarInstalacao(PlanoInstalacao plano)
    {
        var detalhes = new List<string>();
        var reversao = new List<Action>();

        try
        {
            if (!plano.PayloadCompleto)
            {
                string faltando = string.Join(", ", plano.ArquivosAusentes);
                _log.Registrar($"Instalação recusada: payload incompleto (faltando {faltando}) em {plano.PastaPayload}.");
                return ResultadoOperacao.Falha(
                    $"O payload está incompleto: falta {faltando} em {plano.PastaPayload}.");
            }

            if (plano.InstalacaoExistente && !plano.Opcoes.ReinstalarSobreInstalacaoExistente)
            {
                _log.Registrar($"Instalação recusada: já existe uma instalação em {plano.Destino.Pasta}.");
                return ResultadoOperacao.Falha(
                    $"O TRANAL SENTINEL já está instalado em {plano.Destino.Pasta}. Marque “Já instalar” para substituir.");
            }

            _log.Registrar($"Iniciando instalação. Payload: {plano.PastaPayload}. Destino: {plano.Destino.Pasta}.");

            Directory.CreateDirectory(plano.Destino.Pasta);
            detalhes.Add($"pasta de destino: {plano.Destino.Pasta}");

            foreach (ArquivoPlano arquivo in plano.ArquivosParaGravar)
            {
                CopiarArquivo(plano, arquivo, reversao);
                detalhes.Add($"{arquivo.RotuloSituacao}: {arquivo.Nome} -> {arquivo.Destino}");
                _log.Registrar($"Arquivo {arquivo.RotuloSituacao}: {arquivo.Destino}");
            }

            CopiarInstalador(plano, reversao, detalhes);

            foreach (PlanoAtalho atalho in plano.Atalhos)
            {
                _atalho.Criar(atalho.Caminho, atalho.Dados);
                reversao.Add(() => _atalho.Remover(atalho.Caminho));
                detalhes.Add($"atalho criado ({atalho.Descricao}): {atalho.Caminho}");
                _log.Registrar($"Atalho criado ({atalho.Descricao}): {atalho.Caminho}");
            }

            GravarInicializacaoAutomatica(plano, reversao, detalhes);
            GravarEntradaDesinstalacao(plano, reversao, detalhes);
            LimparReferenciasAntigas(plano, detalhes);
            RemoverInstalacaoAnterior(plano, detalhes);

            _log.Registrar("Instalação concluída.");
            return ResultadoOperacao.Ok($"TRANAL SENTINEL instalado em {plano.Destino.Pasta}.", detalhes);
        }
        catch (Exception ex)
        {
            _log.Registrar($"Falha na instalação: {ex.Message}");
            Desfazer(reversao, detalhes);
            return ResultadoOperacao.Falha($"A instalação falhou e foi desfeita: {ex.Message}", detalhes);
        }
    }

    /// <summary>
    /// Limpa as referências que a instalação deixou para trás quando uma opção
    /// está desligada.
    ///
    /// A tarefa agendada e o atalho da área de trabalho são globais: com a
    /// opção desligada, a instalação nova não os recria, e eles ficariam
    /// apontando para um executável que não está mais lá (ou que não deveria
    /// mais abrir junto com o Windows).
    /// </summary>
    private void LimparReferenciasAntigas(PlanoInstalacao plano, List<string> detalhes)
    {
        if (!plano.Opcoes.IniciarComWindows && _tarefa.Existe(TarefaInicializacao.NomeTarefa))
        {
            _tarefa.Remover(TarefaInicializacao.NomeTarefa);
            _log.Registrar($"Tarefa agendada removida (a opção de iniciar com o Windows está desligada).");
            detalhes.Add($"tarefa agendada removida: {TarefaInicializacao.NomeTarefa}");
        }

        if (!plano.Opcoes.AtalhoAreaTrabalho)
        {
            string atalho = CaminhosInstalacao.AtalhoAreaTrabalho(plano.Contexto);
            if (_atalho.Existe(atalho))
            {
                _atalho.Remover(atalho);
                _log.Registrar($"Atalho da área de trabalho removido (a opção está desligada): {atalho}");
                detalhes.Add($"atalho da área de trabalho removido: {atalho}");
            }
        }
    }

    /// <summary>
    /// Trocar de pasta é um movimento, não uma cópia: a instalação anterior
    /// (que estava em outra pasta) é removida depois que a nova já está no
    /// lugar. Sem isso, o app antigo ficaria órfão, fora do alcance do botão
    /// Desinstalar.
    ///
    /// A remoção vem por último de propósito: se algo falhar antes, a
    /// instalação anterior continua intacta.
    /// </summary>
    private void RemoverInstalacaoAnterior(PlanoInstalacao plano, List<string> detalhes)
    {
        string pastaAntiga = plano.PastaInstalacaoAnterior ?? "";
        if (pastaAntiga.Length == 0) return;

        string[] arquivos =
        {
            CaminhosInstalacao.ExecutavelInstalado(pastaAntiga),
            Path.Combine(pastaAntiga, CaminhosInstalacao.NomeLeiaMe),
            CaminhosInstalacao.BackupExecutavel(pastaAntiga),
            CaminhosInstalacao.InstaladorInstalado(pastaAntiga),
        };

        var pendentes = new List<string>();

        foreach (string arquivo in arquivos)
        {
            if (!File.Exists(arquivo)) continue;

            try
            {
                File.Delete(arquivo);
                _log.Registrar($"Arquivo da instalação anterior removido: {arquivo}");
                detalhes.Add($"instalação anterior removida: {arquivo}");
            }
            catch (IOException ex)
            {
                // O app antigo pode estar aberto: a remoção fica para quando o
                // processo segurar o arquivo for encerrado.
                string? agendado = AgendarRemocao(arquivo, pastaAntiga);
                if (agendado is null)
                {
                    _log.Registrar($"Falha ao remover {arquivo}: {ex.Message}");
                    detalhes.Add($"não foi possível remover {arquivo}: {ex.Message}");
                    pendentes.Add(arquivo);
                }
                else
                {
                    _log.Registrar($"Remoção agendada (arquivo em uso): {arquivo}");
                    detalhes.Add($"removido quando o app for fechado (a remoção espera até 2 minutos): {arquivo}");
                    pendentes.Add(arquivo);
                }
            }
            catch (Exception ex)
            {
                _log.Registrar($"Falha ao remover {arquivo}: {ex.Message}");
                detalhes.Add($"não foi possível remover {arquivo}: {ex.Message}");
                pendentes.Add(arquivo);
            }
        }

        TentarRemoverPastaVazia(pastaAntiga);

        // O sumário só é escrito quando tudo saiu. Um arquivo que ficou para
        // trás (app aberto, sem permissão) é o que o pendente guarda.
        if (pendentes.Count == 0)
        {
            _log.Registrar($"Instalação anterior removida de {pastaAntiga}.");
            detalhes.Add($"instalação anterior removida de {pastaAntiga}");
        }
        else
        {
            _log.Registrar($"A instalação anterior em {pastaAntiga} ficou parcialmente: {string.Join(", ", pendentes)}");
        }
    }

    /// <summary>
    /// Copia um arquivo do payload para o destino. O executável anterior é salvo
    /// como .bak antes de ser sobrescrito (é o que garante poder voltar atrás).
    /// </summary>
    private void CopiarArquivo(PlanoInstalacao plano, ArquivoPlano arquivo, List<Action> reversao)
    {
        bool tinhaInstalado = File.Exists(arquivo.Destino);
        string? backup = arquivo.Nome == CaminhosInstalacao.NomeExecutavel ? plano.CaminhoBackup : null;
        string? conteudoAnterior = null;

        if (tinhaInstalado && backup is not null)
        {
            File.Copy(arquivo.Destino, backup, overwrite: true);
            _log.Registrar($"Backup do executável anterior: {backup}");
        }
        else if (tinhaInstalado && arquivo.Nome == CaminhosInstalacao.NomeLeiaMe)
        {
            // O LEIA-ME é texto pequeno: guardar o conteúdo em memória evita
            // deixar um arquivo .bak sobrando na pasta de instalação.
            conteudoAnterior = File.ReadAllText(arquivo.Destino);
        }

        File.Copy(arquivo.Origem, arquivo.Destino, overwrite: true);

        reversao.Add(() =>
        {
            if (backup is not null && File.Exists(backup))
            {
                File.Move(backup, arquivo.Destino, overwrite: true);
                return;
            }
            if (conteudoAnterior is not null)
            {
                File.WriteAllText(arquivo.Destino, conteudoAnterior);
                return;
            }
            if (File.Exists(arquivo.Destino)) File.Delete(arquivo.Destino);
        });
    }

    /// <summary>
    /// Copia o instalador para a pasta de instalação: a entrada de desinstalação
    /// aponta para ele, porque só o instalador sabe desfazer o que ele fez.
    /// </summary>
    private void CopiarInstalador(PlanoInstalacao plano, List<Action> reversao, List<string> detalhes)
    {
        string? origem = _caminhoInstalador ?? Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(origem)) return;

        if (string.Equals(Path.GetFullPath(origem), Path.GetFullPath(plano.CaminhoInstaladorInstalado),
                StringComparison.OrdinalIgnoreCase))
            return; // o instalador já está rodando a partir do destino

        File.Copy(origem, plano.CaminhoInstaladorInstalado, overwrite: true);
        reversao.Add(() =>
        {
            if (File.Exists(plano.CaminhoInstaladorInstalado)) File.Delete(plano.CaminhoInstaladorInstalado);
        });
        detalhes.Add($"instalador copiado: {plano.CaminhoInstaladorInstalado}");
        _log.Registrar($"Instalador copiado para a pasta de instalação: {plano.CaminhoInstaladorInstalado}");
    }

    private void GravarInicializacaoAutomatica(PlanoInstalacao plano, List<Action> reversao, List<string> detalhes)
    {
        if (plano.NomeTarefaIniciar is null || plano.DescricaoTarefaIniciar is null) return;

        // Tarefa agendada e não chave Run: o app exige administrador, e um
        // requireAdministrator na chave Run pediria UAC a cada login. A tarefa
        // sobe já elevada, sem prompt.
        string nome = plano.NomeTarefaIniciar;
        bool existia = _tarefa.Existe(nome);

        _tarefa.Criar(nome, plano.CaminhoExecutavel, CaminhosInstalacao.ArgumentoIniciarComoSistema);
        reversao.Add(() =>
        {
            if (!existia)
            {
                _tarefa.Remover(nome);
                return;
            }

            // A tarefa existia antes: ela pointed to the previous installation,
            // which pode estar em outra pasta. Repontar para o executável novo
            // (que o rollback apaga) deixaria a tarefa apontando para o nada.
            string? anterior = plano.PastaInstalacaoAnterior;
            string caminhoAnterior = anterior is null
                ? plano.CaminhoExecutavel
                : CaminhosInstalacao.ExecutavelInstalado(anterior);

            if (File.Exists(caminhoAnterior))
                _tarefa.Criar(nome, caminhoAnterior, CaminhosInstalacao.ArgumentoIniciarComoSistema);
            else
                _tarefa.Remover(nome);
        });

        detalhes.Add($"inicialização automática: {plano.DescricaoTarefaIniciar}");
        _log.Registrar($"Tarefa agendada criada: {plano.DescricaoTarefaIniciar}");
    }

    private void GravarEntradaDesinstalacao(PlanoInstalacao plano, List<Action> reversao, List<string> detalhes)
    {
        EntradaDesinstalacao entrada = plano.EntradaDesinstalacao;
        string chave = CaminhosInstalacao.ChaveDesinstalacao;
        bool existia = _registro.ExisteChave(ChaveRegistro.Maquina, chave);

        _registro.GravarTexto(ChaveRegistro.Maquina, chave, "DisplayName", entrada.DisplayName);
        _registro.GravarTexto(ChaveRegistro.Maquina, chave, "DisplayVersion", entrada.DisplayVersion);
        _registro.GravarTexto(ChaveRegistro.Maquina, chave, "Publisher", entrada.Publisher);
        _registro.GravarTexto(ChaveRegistro.Maquina, chave, "InstallLocation", entrada.InstallLocation);
        _registro.GravarTexto(ChaveRegistro.Maquina, chave, "DisplayIcon", entrada.DisplayIcon);
        _registro.GravarTexto(ChaveRegistro.Maquina, chave, "UninstallString", entrada.UninstallString);
        _registro.GravarNumero(ChaveRegistro.Maquina, chave, "NoModify", 1);
        _registro.GravarNumero(ChaveRegistro.Maquina, chave, "NoRepair", 1);

        reversao.Add(() =>
        {
            // Se a chave não existia antes, some com ela inteira.
            if (!existia) _registro.RemoverChave(ChaveRegistro.Maquina, chave);
        });

        detalhes.Add($"entrada de desinstalação: HKLM\\{chave}");
        _log.Registrar($"Entrada de desinstalação gravada: {entrada.UninstallString}");
    }

    private void Desfazer(List<Action> reversao, List<string> detalhes)
    {
        for (int i = reversao.Count - 1; i >= 0; i--)
        {
            try
            {
                reversao[i]();
            }
            catch (Exception ex)
            {
                _log.Registrar($"Falha ao desfazer uma etapa da instalação: {ex.Message}");
                detalhes.Add($"não foi possível desfazer uma etapa: {ex.Message}");
            }
        }
        reversao.Clear();
        _log.Registrar("Instalação desfeita.");
        detalhes.Add("instalação desfeita");
    }

    private ResultadoOperacao ExecutarDesinstalacao(PlanoDesinstalacao plano)
    {
        var detalhes = new List<string>();
        var erros = new List<string>();

        _log.Registrar($"Iniciando desinstalação. Destino: {plano.Destino.Pasta}.");

        foreach (string arquivo in plano.ArquivosRemovidos)
        {
            if (!File.Exists(arquivo))
            {
                detalhes.Add($"não existe (pulado): {arquivo}");
                continue;
            }

            try
            {
                File.Delete(arquivo);
                _log.Registrar($"Arquivo removido: {arquivo}");
                detalhes.Add($"removido: {arquivo}");
            }
            catch (IOException ex)
            {
                // Executável em uso: só acontece com o próprio instalador rodando
                // a partir da pasta de instalação. A removal fica para depois.
                string? agendado = AgendarRemocao(arquivo, plano.Destino.Pasta);
                if (agendado is null)
                {
                    erros.Add($"não foi possível remover {arquivo}: {ex.Message}");
                    _log.Registrar($"Falha ao remover {arquivo}: {ex.Message}");
                }
                else
                {
                    detalhes.Add($"removido assim que o instalador sair: {arquivo}");
                    _log.Registrar($"Remoção agendada (arquivo em uso): {arquivo}");
                }
            }
            catch (Exception ex)
            {
                erros.Add($"não foi possível remover {arquivo}: {ex.Message}");
                _log.Registrar($"Falha ao remover {arquivo}: {ex.Message}");
            }
        }

        foreach (string atalho in plano.AtalhosRemovidos)
        {
            try
            {
                if (!_atalho.Existe(atalho))
                {
                    detalhes.Add($"não existe (pulado): {atalho}");
                    continue;
                }
                _atalho.Remover(atalho);
                _log.Registrar($"Atalho removido: {atalho}");
                detalhes.Add($"atalho removido: {atalho}");
            }
            catch (Exception ex)
            {
                erros.Add($"não foi possível remover o atalho {atalho}: {ex.Message}");
                _log.Registrar($"Falha ao remover o atalho {atalho}: {ex.Message}");
            }
        }

        try
        {
            _tarefa.Remover(plano.NomeTarefaIniciar);
            _log.Registrar($"Tarefa agendada removida: {plano.NomeTarefaIniciar}");
            detalhes.Add($"inicialização automática removida: tarefa agendada \"{plano.NomeTarefaIniciar}\"");

            // Faxina: se sobrou um valor na chave Run apontando para o nosso
            // executável (instalação manual antiga), ele também sai.
            string? valorAntigo = _registro.LerTexto(
                ChaveRegistro.Usuario, CaminhosInstalacao.ChaveIniciar, CaminhosInstalacao.NomeValorIniciar);
            if (valorAntigo is not null
                && valorAntigo.Contains(CaminhosInstalacao.NomeExecutavel, StringComparison.OrdinalIgnoreCase))
            {
                _registro.RemoverValor(ChaveRegistro.Usuario, CaminhosInstalacao.ChaveIniciar, CaminhosInstalacao.NomeValorIniciar);
                _log.Registrar("Valor antigo da chave Run apontando para o TRANAL SENTINEL removido.");
                detalhes.Add($"valor antigo da chave Run removido: {CaminhosInstalacao.NomeValorIniciar}");
            }
        }
        catch (Exception ex)
        {
            erros.Add($"não foi possível remover o valor de inicialização automática: {ex.Message}");
            _log.Registrar($"Falha ao remover o valor de inicialização automática: {ex.Message}");
        }

        try
        {
            _registro.RemoverChave(ChaveRegistro.Maquina, plano.ChaveDesinstalacao);
            _log.Registrar($"Entrada de desinstalação removida: {plano.ChaveDesinstalacao}");
            detalhes.Add($"entrada de desinstalação removida: HKLM\\{plano.ChaveDesinstalacao}");
        }
        catch (Exception ex)
        {
            erros.Add($"não foi possível remover a entrada de desinstalação: {ex.Message}");
            _log.Registrar($"Falha ao remover a entrada de desinstalação: {ex.Message}");
        }

        _log.Registrar($"Configurações do usuário preservadas em {plano.PastaConfiguracoesPreservada}.");
        detalhes.Add($"configurações do usuário preservadas: {plano.PastaConfiguracoesPreservada}");

        // A pasta de instalação só some se sobrar alguma coisa que não é nossa.
        TentarRemoverPastaVazia(plano.Destino.Pasta);

        if (erros.Count > 0)
            return ResultadoOperacao.Falha("A desinstalação terminou com erros.", detalhes.Concat(erros));

        _log.Registrar("Desinstalação concluída.");
        return ResultadoOperacao.Ok("TRANAL SENTINEL desinstalado.", detalhes);
    }

    /// <summary>Agenda a remoção de um arquivo que está em uso (o próprio instalador).</summary>
    private string? AgendarRemocao(string arquivo, string pastaInstalacao)
    {
        bool sucesso = RemocaoAdiada.Agendar(arquivo, pastaInstalacao);
        return sucesso ? arquivo : null;
    }

    private void TentarRemoverPastaVazia(string pasta)
    {
        try
        {
            if (!Directory.Exists(pasta)) return;
            if (Directory.EnumerateFileSystemEntries(pasta).Any()) return;
            Directory.Delete(pasta);
            _log.Registrar($"Pasta de instalação removida: {pasta}");
        }
        catch (Exception ex)
        {
            // A pasta pode ficar para trás (arquivo em uso): não é erro da desinstalação.
            _log.Registrar($"A pasta de instalação ficou para trás ({pasta}): {ex.Message}");
        }
    }
}
