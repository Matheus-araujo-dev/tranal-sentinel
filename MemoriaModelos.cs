namespace TranalSentinel;

/// <summary>Uma linha da tabela de tempos de um módulo.</summary>
public sealed class MemoriaLinhaVm
{
    public string Nome { get; init; } = "";
    public string Valor { get; init; } = "";
}

/// <summary>Um módulo de memória pronto para a tela do TRANAL MEMÓRIA.</summary>
public sealed class MemoriaModuloVm
{
    /// <summary>Título do cartão (slot e endereço do barramento).</summary>
    public string Titulo { get; init; } = "";

    /// <summary>Identificação do módulo (fabricante, part number, capacidade).</summary>
    public string Identidade { get; init; } = "";

    /// <summary>Detalhes (tipo, ranks, serial, data).</summary>
    public string Detalhe { get; init; } = "";

    /// <summary>Linha do padrão JEDEC.</summary>
    public string Padrao { get; init; } = "";

    /// <summary>Linha do perfil de overclock.</summary>
    public string Perfil { get; init; } = "";

    /// <summary>Tabela de tempos.</summary>
    public List<MemoriaLinhaVm> Tempos { get; init; } = new();
}
