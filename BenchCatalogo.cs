namespace TranalSentinel;

/// <summary>
/// O catálogo dos benchmarks: o nome de cada um e o que ele mede.
///
/// <para>
/// A regra fica em um lugar só: a tela do TRANAL BENCH, o relatório
/// exportável e a comparação entre PCs consomem estes nomes. Se cada um
/// tivesse o seu <c>switch</c>, os três passariam a mostrar coisas
/// diferentes na primeira edição em um deles.
/// </para>
/// </summary>
public static class BenchCatalogo
{
    /// <summary>Nome curto de um teste (para a tabela, o relatório e a comparação).</summary>
    public static string Nome(string teste)
    {
        string base_ = Base(teste);

        string nome = base_ switch
        {
            "render" => "Render (CPU)",
            "gpu" => "Cena de jogo (GPU)",
            "rt" => "Ray tracing (GPU)",
            "sim" => "Simulação de jogo (CPU)",
            "mem" => "Memória e carregamento",
            "disk" => "Disco",
            "oc" => "TRANAL OC (estabilidade)",
            _ => base_
        };

        // O resto da chave entra no nome: sem isso, o render de multi-core e o
        // de single-core (e os dois ray tracing de presets diferentes)
        // apareceriam com o mesmo rótulo, um embaixo do outro.
        string extra = Extra(teste);
        return extra.Length == 0 ? nome : $"{nome} · {extra}";
    }

    /// <summary>O que o teste mede (a frase do subtítulo da tela).</summary>
    public static string Descricao(string teste) => Base(teste) switch
    {
        "render" => "Benchmark de CPU por renderização 3D (path tracing), no mesmo princípio do Cinebench",
        "gpu" => "Cena de jogo em Direct3D 11 para medir a placa de vídeo, no estilo 3DMark / Unigine",
        "rt" => "Ray tracing por hardware (DXR 1.1, Direct3D 12): usa os núcleos de RT da placa, com 3 passadas e nota pela mediana",
        "sim" => "Quanto FPS a CPU aguenta numa simulação de jogo com milhares de unidades",
        "mem" => "Latência e banda da RAM e velocidade de carregamento do disco",
        "disk" => "Velocidade de leitura e escrita do disco: sequencial e acessos aleatórios de 4 KB, no estilo CrystalDiskMark",
        _ => "Benchmark de CPU por renderização 3D (path tracing), no mesmo princípio do Cinebench"
    };

    /// <summary>
    /// O nome do preset de ray tracing. Mora aqui para que a tela do RT e a
    /// comparação entre PCs chamem o preset do mesmo jeito.
    /// </summary>
    public static string PresetRt(string preset) => (preset ?? "").ToLowerInvariant() switch
    {
        "normal" => "Normal",
        "extrema" => "Extrema",
        "alta" => "Alta",
        _ => preset ?? ""
    };

    /// <summary>
    /// A parte do teste antes dos detalhes (no ray tracing, o preset e a
    /// resolução vêm depois dos dois-pontos).
    /// </summary>
    private static string Base(string teste) => (teste ?? "").Split(':')[0];

    /// <summary>
    /// O detalhe que vem depois da chave: multi-core, single-core, o preset e
    /// a resolução do ray tracing.
    /// </summary>
    private static string Extra(string teste)
    {
        if (string.IsNullOrEmpty(teste)) return "";

        int pos = teste.IndexOf(':');
        if (pos < 0) return "";

        string base_ = Base(teste);
        string[] partes = teste[(pos + 1)..].Split(':', StringSplitOptions.RemoveEmptyEntries);

        if (base_ == "render")
        {
            // render:multi | render:single | render:sustained:multi | render:sustained:single
            bool sustentado = Array.IndexOf(partes, "sustained") >= 0;
            string nucleo = Array.Find(partes, p => p is "multi" or "single") ?? "";

            string nome = nucleo switch
            {
                "multi" => "multi-core",
                "single" => "single-core",
                _ => ""
            };

            if (nome.Length == 0) return "";
            return sustentado ? $"{nome} · sustentado" : nome;
        }

        if (base_ == "rt")
        {
            // rt:preset:altura — o preset com o nome da tela e a altura em p.
            if (partes.Length < 2) return "";
            return $"{PresetRt(partes[0])} {partes[1]}p";
        }

        string resto = string.Join(' ', partes).Trim();
        if (resto.Length == 0) return "";

        return resto switch
        {
            "multi" => "multi-core",
            "single" => "single-core",
            _ => resto
        };
    }
}
