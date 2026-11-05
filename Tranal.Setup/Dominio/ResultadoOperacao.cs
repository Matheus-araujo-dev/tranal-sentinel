namespace TranalSetup.Dominio;

/// <summary>Resultado de uma operação do instalador (instalar ou desinstalar).</summary>
/// <param name="Sucesso">Se a operação foi concluída.</param>
/// <param name="Mensagem">Resumo em uma linha, para a tela ou para o console.</param>
/// <param name="Detalhes">Linha por linha do que aconteceu (vai para o log e para a tela).</param>
public sealed record ResultadoOperacao(bool Sucesso, string Mensagem, IReadOnlyList<string> Detalhes)
{
    /// <summary>Cria um resultado de sucesso.</summary>
    public static ResultadoOperacao Ok(string mensagem, IEnumerable<string>? detalhes = null)
        => new(true, mensagem, (detalhes ?? Enumerable.Empty<string>()).ToArray());

    /// <summary>Cria um resultado de falha.</summary>
    public static ResultadoOperacao Falha(string mensagem, IEnumerable<string>? detalhes = null)
        => new(false, mensagem, (detalhes ?? Enumerable.Empty<string>()).ToArray());
}
