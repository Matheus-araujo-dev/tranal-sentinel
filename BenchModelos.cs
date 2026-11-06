namespace TranalSentinel;

/// <summary>Uma linha da tabela de comparação entre PCs, pronta para a tela.</summary>
public sealed class BenchComparacaoLinhaVm
{
    /// <summary>O nome do teste.</summary>
    public string Teste { get; init; } = "";

    /// <summary>O detalhe: o resumo de cada lado e as datas.</summary>
    public string Detalhe { get; init; } = "";

    /// <summary>A nota desta máquina.</summary>
    public string NotaLocal { get; init; } = "";

    /// <summary>A nota do PC importado.</summary>
    public string NotaImportada { get; init; } = "";

    /// <summary>A diferença: "mais rápido (12,3%)".</summary>
    public string Diferenca { get; init; } = "";
}
