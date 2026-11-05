namespace TranalSetup.Infra;

/// <summary>
/// Registro de operações do instalador. Uma linha por operação, com data e hora.
///
/// fica atrás de uma interface para que os testes não precisem gravar no
/// %ProgramData% da máquina.
/// </summary>
public interface ILog
{
    /// <summary>Grava uma linha no log.</summary>
    void Registrar(string mensagem);
}
