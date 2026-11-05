namespace TranalSetup.Infra;

/// <summary>Em qual raiz do registro a operação acontece.</summary>
public enum ChaveRegistro
{
    /// <summary>HKCU: só o usuário atual.</summary>
    Usuario,

    /// <summary>HKLM: a máquina inteira (precisa de administrador).</summary>
    Maquina,
}

/// <summary>
/// Acesso ao registro do Windows atrás de uma interface.
///
/// O instalador só conhece esta interface: assim os testes conseguem simular o
/// registro (e provar a idempotência) sem escrever nada no Windows de verdade.
/// </summary>
public interface IRegistro
{
    /// <summary>Se a chave existe.</summary>
    bool ExisteChave(ChaveRegistro chave, string caminho);

    /// <summary>Lê um valor de texto (null se a chave ou o valor não existirem).</summary>
    string? LerTexto(ChaveRegistro chave, string caminho, string nome);

    /// <summary>Grava (ou substitui) um valor de texto.</summary>
    void GravarTexto(ChaveRegistro chave, string caminho, string nome, string valor);

    /// <summary>Grava (ou substitui) um valor numérico de 32 bits.</summary>
    void GravarNumero(ChaveRegistro chave, string caminho, string nome, int valor);

    /// <summary>Remove um valor (sem falhar se ele não existir).</summary>
    void RemoverValor(ChaveRegistro chave, string caminho, string nome);

    /// <summary>Remove a chave e tudo que está dentro dela.</summary>
    void RemoverChave(ChaveRegistro chave, string caminho);
}
