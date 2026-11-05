using TranalSetup.Dominio;

namespace TranalSetup.Infra;

/// <summary>
/// Criação e remoção de atalhos (.lnk) atrás de uma interface,
/// para que o instalador possa ser testado sem mexer no menu Iniciar.
/// </summary>
public interface IAtalho
{
    /// <summary>Cria (ou sobrescreve) o atalho no caminho informado.</summary>
    void Criar(string caminho, DadosAtalho dados);

    /// <summary>Se o atalho existe.</summary>
    bool Existe(string caminho);

    /// <summary>Remove o atalho (sem falhar se ele não existir).</summary>
    void Remover(string caminho);
}
