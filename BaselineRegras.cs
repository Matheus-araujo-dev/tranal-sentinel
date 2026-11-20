namespace TranalSentinel;

/// <summary>
/// O perfil do que é normal para este PC: a média e o desvio padrão de cada
/// sensor, calculados a partir dos agregados diários. É o que permite dizer
/// "a CPU está 15 °C acima do normal para esta hora" em vez de um limite fixo.
/// </summary>
public sealed record BaselineSensor(
    string Chave,
    float Media,
    float DesvioPadrao,
    int DiasComDado);

/// <summary>
/// Um desvio do valor atual em relação à baseline do PC: o que está fora do
/// normal, e por quanto.
/// </summary>
public sealed record DesvioBaseline(
    string Chave,
    string Rotulo,
    string Unidade,
    float ValorAtual,
    float MediaEsperada,
    float DesvioPadrao,
    float Desvios);

/// <summary>
/// Regras puras da baseline personalizada: aprende o que é normal para
/// ESTE PC e diz o que está fora do normal agora.
///
/// <para>
/// A ideia é do SysPerf ("learns what's normal for YOUR PC"), mas a
/// implementação é do TRANAL SENTINEL: a baseline é calculada a partir
/// dos agregados diários que o banco já guarda, e a comparação é com
/// a média e o desvio padrão — não com um modelo de aprendizado.
/// </para>
/// </summary>
public static class BaselineRegras
{
    /// <summary>
    /// Quantos dias de dados são necessários para a baseline valer. Com
    /// menos que isso, o app diz "aprendendo" em vez de "fora do normal".
    /// </summary>
    public const int DiasMinimos = 3;

    /// <summary>
    /// Quantos desvios padrão acima da média contam como "fora do normal".
    /// </summary>
    public const float LimiteDesvios = 2.0f;

    /// <summary>
    /// Calcula a baseline de cada sensor a partir dos agregados diários.
    /// </summary>
    public static IReadOnlyList<BaselineSensor> Calcular(IReadOnlyList<AgregadoDiario> agregados)
    {
        var resultado = new List<BaselineSensor>();
        if (agregados.Count == 0) return resultado;

        AdicionarSe(resultado, "cpu-temp", agregados, a => a.CpuTempMedia);
        AdicionarSe(resultado, "cpu-carga", agregados, a => a.CpuCargaMedia);
        AdicionarSe(resultado, "gpu-temp", agregados, a => a.GpuTempMedia);
        AdicionarSe(resultado, "gpu-carga", agregados, a => a.GpuCargaMedia);
        AdicionarSe(resultado, "ram-uso", agregados, a => a.RamUsoMedia);

        return resultado;
    }

    /// <summary>
    /// Compara o contexto atual de sensores com a baseline e devolve os
    /// desvios significativos (acima de <see cref="LimiteDesvios"/> desvios
    /// padrão da média). Lista vazia = tudo normal.
    /// </summary>
    public static IReadOnlyList<DesvioBaseline> Comparar(
        IReadOnlyList<BaselineSensor> baseline,
        ContextoSensores contexto)
    {
        var desvios = new List<DesvioBaseline>();

        foreach (var b in baseline)
        {
            if (b.DiasComDado < DiasMinimos) continue;
            if (b.DesvioPadrao < 0.01f) continue;

            float? valorAtual = LerValorAtual(b.Chave, contexto);
            if (valorAtual is null) continue;

            float d = (valorAtual.Value - b.Media) / b.DesvioPadrao;

            var item = SensorCatalogo.PorChave(b.Chave);
            if (item is null) continue;

            bool foraDoNormal = item.OperacaoPadrao == ">="
                ? d >= LimiteDesvios
                : d <= -LimiteDesvios;

            if (foraDoNormal)
            {
                desvios.Add(new DesvioBaseline(
                    b.Chave, item.Rotulo, item.Unidade,
                    valorAtual.Value, b.Media, b.DesvioPadrao, d));
            }
        }

        return desvios;
    }

    /// <summary>
    /// Texto para a tela: descreve o desvio em linguagem natural.
    /// </summary>
    public static string TextoDesvio(DesvioBaseline d)
    {
        float diferenca = d.ValorAtual - d.MediaEsperada;
        string sinal = diferenca >= 0 ? "acima" : "abaixo";
        float abs = Math.Abs(diferenca);

        return $"{d.Rotulo}: {NumeroTexto.Numero(d.ValorAtual, 1)} {d.Unidade} " +
               $"({NumeroTexto.Numero(abs, 1)} {d.Unidade} {sinal} do normal de " +
               $"{NumeroTexto.Numero(d.MediaEsperada, 1)} {d.Unidade})";
    }

    /// <summary>
    /// Texto curto para o painel flutuante ou aviso: o estado da baseline.
    /// </summary>
    public static string TextoEstado(IReadOnlyList<BaselineSensor> baseline, int diasColetados)
    {
        if (diasColetados < DiasMinimos)
            return $"Aprendendo o que é normal para o seu PC ({diasColetados}/{DiasMinimos} dias)";

        return "Baseline pronta";
    }

    private static float? LerValorAtual(string chave, ContextoSensores ctx) => chave switch
    {
        "cpu-temp" => ctx.TemperaturaCpu,
        "cpu-carga" => ctx.CargaCpu,
        "gpu-temp" => ctx.TemperaturaGpu,
        "gpu-carga" => ctx.CargaGpu,
        "ram-uso" => ctx.UsoRam,
        _ => null,
    };

    private static void AdicionarSe(
        List<BaselineSensor> resultado,
        string chave,
        IReadOnlyList<AgregadoDiario> agregados,
        Func<AgregadoDiario, float?> seletor)
    {
        float soma = 0;
        float somaQuad = 0;
        int n = 0;

        foreach (var a in agregados)
        {
            float? v = seletor(a);
            if (v is null) continue;
            soma += v.Value;
            somaQuad += v.Value * v.Value;
            n++;
        }

        if (n == 0) return;

        float media = soma / n;
        float variancia = (somaQuad / n) - (media * media);
        float desvio = variancia > 0 ? MathF.Sqrt(variancia) : 0;

        resultado.Add(new BaselineSensor(chave, media, desvio, n));
    }
}
