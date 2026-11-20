namespace TranalSentinel;

/// <summary>
/// Uma tendência de longo prazo de um sensor: a direção (subindo, descendo,
/// estável) e a inclinação por dia, calculadas por regressão linear sobre
/// os agregados diários.
/// </summary>
public sealed record Tendencia(
    string Chave,
    string Rotulo,
    string Unidade,
    float InclinacaoPorDia,
    float R2,
    int Dias,
    Direcao Direcao);

/// <summary>
/// A direção de uma tendência: subindo, descendo ou estável.
/// </summary>
public enum Direcao { Estavel, Subindo, Descendo }

/// <summary>
/// Regras puras das tendências de longo prazo: dado um conjunto de agregados
/// diários (até 365 dias), calcula a tendência de cada sensor por regressão
/// linear simples. Nenhum I/O aqui.
///
/// <para>
/// A ideia é do SysPerf ("long-term trends"), mas a implementação é do
/// TRANAL SENTINEL: regressão linear simples sobre as médias diárias,
/// sem modelo de aprendizado e sem dependência nova.
/// </para>
/// </summary>
public static class TendenciaRegras
{
    /// <summary>
    /// Quantos dias de dados são necessários para uma tendência valer.
    /// </summary>
    public const int DiasMinimos = 7;

    /// <summary>
    /// R² mínimo para a tendência ser considerada significativa.
    /// </summary>
    public const float R2Minimo = 0.3f;

    /// <summary>
    /// Inclinação mínima por dia para não ser "estável".
    /// </summary>
    public const float InclinacaoMinima = 0.1f;

    /// <summary>
    /// Calcula as tendências de cada sensor a partir dos agregados diários.
    /// Lista vazia = nenhuma tendência significativa ou dados insuficientes.
    /// </summary>
    public static IReadOnlyList<Tendencia> Calcular(IReadOnlyList<AgregadoDiario> agregados)
    {
        var resultado = new List<Tendencia>();
        if (agregados.Count < DiasMinimos) return resultado;

        AdicionarSe(resultado, "cpu-temp", agregados, a => a.CpuTempMedia);
        AdicionarSe(resultado, "cpu-carga", agregados, a => a.CpuCargaMedia);
        AdicionarSe(resultado, "gpu-temp", agregados, a => a.GpuTempMedia);
        AdicionarSe(resultado, "gpu-carga", agregados, a => a.GpuCargaMedia);
        AdicionarSe(resultado, "ram-uso", agregados, a => a.RamUsoMedia);

        return resultado;
    }

    /// <summary>
    /// Texto para a tela: descreve a tendência em linguagem natural.
    /// </summary>
    public static string TextoTendencia(Tendencia t)
    {
        string direcao = t.Direcao switch
        {
            Direcao.Subindo => "subindo",
            Direcao.Descendo => "descendo",
            _ => "estável"
        };

        float absPorDia = Math.Abs(t.InclinacaoPorDia);
        float porSemana = absPorDia * 7;

        return $"{t.Rotulo}: {direcao} " +
               $"(~{NumeroTexto.Numero(porSemana, 1)} {t.Unidade}/semana nos últimos {t.Dias} dias)";
    }

    /// <summary>
    /// Texto curto para o resumo: quantas tendências significativas existem.
    /// </summary>
    public static string TextoResumo(IReadOnlyList<Tendencia> tendencias)
    {
        int subindo = 0, descendo = 0;
        foreach (var t in tendencias)
        {
            if (t.Direcao == Direcao.Subindo) subindo++;
            else if (t.Direcao == Direcao.Descendo) descendo++;
        }

        if (subindo == 0 && descendo == 0)
            return "Sem tendências significativas";

        var partes = new List<string>();
        if (subindo > 0) partes.Add($"{subindo} subindo");
        if (descendo > 0) partes.Add($"{descendo} descendo");
        return string.Join(", ", partes);
    }

    private static void AdicionarSe(
        List<Tendencia> resultado,
        string chave,
        IReadOnlyList<AgregadoDiario> agregados,
        Func<AgregadoDiario, float?> seletor)
    {
        var item = SensorCatalogo.PorChave(chave);
        if (item is null) return;

        float inclinacao = 0;
        float r2 = 0;
        int n = 0;

        RegressaoLinear(agregados, seletor, ref inclinacao, ref r2, ref n);

        if (n < DiasMinimos) return;
        if (r2 < R2Minimo) return;

        float absInc = Math.Abs(inclinacao);
        if (absInc < InclinacaoMinima) return;

        var direcao = inclinacao > 0 ? Direcao.Subindo : Direcao.Descendo;

        resultado.Add(new Tendencia(
            chave, item.Rotulo, item.Unidade,
            inclinacao, r2, n, direcao));
    }

    private static void RegressaoLinear(
        IReadOnlyList<AgregadoDiario> agregados,
        Func<AgregadoDiario, float?> seletor,
        ref float inclinacao,
        ref float r2,
        ref int n)
    {
        // x = dia em sequência (0, 1, 2, ...), y = valor do sensor.
        // Regressão linear simples: y = a + b*x
        float somaX = 0, somaY = 0, somaXY = 0, somaX2 = 0, somaY2 = 0;
        int idx = 0;
        n = 0;

        foreach (var a in agregados)
        {
            float? v = seletor(a);
            if (v is null) { idx++; continue; }

            float x = idx;
            float y = v.Value;
            somaX += x;
            somaY += y;
            somaXY += x * y;
            somaX2 += x * x;
            somaY2 += y * y;
            n++;
            idx++;
        }

        if (n < DiasMinimos) return;

        float mediaX = somaX / n;
        float mediaY = somaY / n;

        float sxx = somaX2 - n * mediaX * mediaX;
        float sxy = somaXY - n * mediaX * mediaY;
        float syy = somaY2 - n * mediaY * mediaY;

        if (sxx < 0.001f || syy < 0.001f) return;

        inclinacao = sxy / sxx;
        r2 = (sxy * sxy) / (sxx * syy);
    }
}
