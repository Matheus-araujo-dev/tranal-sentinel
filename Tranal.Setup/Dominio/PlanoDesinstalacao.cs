namespace TranalSetup.Dominio;

/// <summary>
/// Plano completo da desinstalação: o que será removido e o que será preservado.
///
/// O plano existe para deixar explícito o limite da desinstalação: some com o que
/// o instalador criou, mas preserva as configurações do usuário.
/// </summary>
public sealed class PlanoDesinstalacao
{
    /// <summary>Contexto (pastas do sistema) usado no plano.</summary>
    public required ContextoSistema Contexto { get; init; }

    /// <summary>Arquivos que serão removidos da pasta de instalação.</summary>
    public required IReadOnlyList<string> ArquivosRemovidos { get; init; }

    /// <summary>Atalhos que serão removidos.</summary>
    public required IReadOnlyList<string> AtalhosRemovidos { get; init; }

    /// <summary>Nome da tarefa agendada que será removida.</summary>
    public required string NomeTarefaIniciar { get; init; }

    /// <summary>Se a tarefa agendada existe (e portanto precisa ser removida).</summary>
    public required bool TarefaIniciarExiste { get; init; }

    /// <summary>Chave de desinstalação que será removida.</summary>
    public required string ChaveDesinstalacao { get; init; }

    /// <summary>Se a chave de desinstalação existe.</summary>
    public required bool ChaveDesinstalacaoExiste { get; init; }

    /// <summary>Pasta de configurações do usuário que fica preservada.</summary>
    public required string PastaConfiguracoesPreservada { get; init; }
}
