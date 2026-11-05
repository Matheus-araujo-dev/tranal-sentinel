namespace TranalSetup.Infra;

/// <summary>
/// Criação e remoção da tarefa agendada de inicialização, atrás de uma
/// interface, para que o instalador possa ser testado sem mexer no
/// Agendador de Tarefas do Windows.
/// </summary>
public interface ITarefaAgendada
{
    /// <summary>Cria (ou sobrescreve) a tarefa que roda o executavel informado.</summary>
    void Criar(string nome, string caminhoExecutavel, string argumento);

    /// <summary>Se a tarefa existe.</summary>
    bool Existe(string nome);

    /// <summary>Remove a tarefa (sem falhar se ela não existir).</summary>
    void Remover(string nome);
}
