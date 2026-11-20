namespace TranalSentinel;

/// <summary>
/// Um alerta contextual: o cruzamento de um sensor elevado com o programa
/// que estava em frente naquele momento. É o que permite dizer "a CPU subiu
/// quando o Chrome estava em frente" em vez de só "a CPU subiu".
/// </summary>
public sealed record AlertaContextual(
    string Sensor,
    string Rotulo,
    string Unidade,
    float Valor,
    double Limite,
    string Programa,
    DateTime Momento);

/// <summary>
/// Regras puras dos alertas contextuais por programa: cruza o programa
/// em foco com o valor do sensor e diz o que estava causando a carga.
/// Nenhum I/O aqui.
///
/// <para>
/// A ideia é de concorrentes que cruzam uso com sensores ("which app is
/// causing the heat"), mas a implementação é do TRANAL SENTINEL: o uso
/// já é gravado no banco, e os sensores também — é só cruzar.
/// </para>
/// </summary>
public static class AlertaContextoRegras
{
    /// <summary>
    /// Cruza o programa em foco com os sensores e devolve os alertas
    /// contextuais (onde a carga coincide com o programa).
    /// Lista vazia = nada fora do normal, ou sem informação de uso.
    /// </summary>
    public static IReadOnlyList<AlertaContextual> Verificar(
        ContextoSensores sensores,
        string? programaEmFoco,
        IReadOnlyList<RegraAlerta> regras)
    {
        var alertas = new List<AlertaContextual>();
        if (string.IsNullOrEmpty(programaEmFoco)) return alertas;

        DateTime agora = DateTime.Now;

        foreach (var regra in regras)
        {
            var item = SensorCatalogo.PorChave(regra.Sensor);
            if (item is null) continue;

            float? valor = LerValor(regra.Sensor, sensores);
            if (valor is null) continue;

            bool disparou = regra.Operacao == ">="
                ? valor.Value >= regra.Limite
                : valor.Value <= regra.Limite;

            if (disparou)
            {
                alertas.Add(new AlertaContextual(
                    regra.Sensor, item.Rotulo, item.Unidade,
                    valor.Value, regra.Limite,
                    programaEmFoco, agora));
            }
        }

        return alertas;
    }

    /// <summary>
    /// Texto para a tela: descreve o alerta contextual.
    /// </summary>
    public static string TextoAlerta(AlertaContextual a) =>
        $"{a.Rotulo} em {NumeroTexto.Numero(a.Valor, 1)} {a.Unidade} " +
        $"(limite: {NumeroTexto.Numero(a.Limite, 1)} {a.Unidade}) " +
        $"enquanto {a.Programa} estava em frente";

    /// <summary>
    /// Agrupa alertas contextuais por programa: quais programas
    /// coincidem mais com cargas elevadas.
    /// </summary>
    public static IReadOnlyList<(string Programa, int Contagem)> RankingPorPrograma(
        IReadOnlyList<AlertaContextual> historico)
    {
        var contagem = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var a in historico)
        {
            if (contagem.TryGetValue(a.Programa, out int n))
                contagem[a.Programa] = n + 1;
            else
                contagem[a.Programa] = 1;
        }

        var ranking = new List<(string, int)>(contagem.Count);
        foreach (var kv in contagem)
            ranking.Add((kv.Key, kv.Value));

        ranking.Sort((a, b) => b.Item2.CompareTo(a.Item2));
        return ranking;
    }

    /// <summary>
    /// Texto para o relatório: resume quais programas coincidem com
    /// cargas elevadas.
    /// </summary>
    public static string TextoRanking(IReadOnlyList<(string Programa, int Contagem)> ranking)
    {
        if (ranking.Count == 0)
            return "Nenhum programa identificado como causador de carga.";

        var partes = new List<string>();
        int limite = Math.Min(ranking.Count, 5);
        for (int i = 0; i < limite; i++)
        {
            var (prog, cnt) = ranking[i];
            partes.Add($"{prog} ({cnt}×)");
        }

        return "Programas que mais coincidem com cargas elevadas: " +
               string.Join(", ", partes);
    }

    private static float? LerValor(string chave, ContextoSensores ctx) => chave switch
    {
        "cpu-temp" => ctx.TemperaturaCpu,
        "cpu-carga" => ctx.CargaCpu,
        "gpu-temp" => ctx.TemperaturaGpu,
        "gpu-carga" => ctx.CargaGpu,
        "ram-uso" => ctx.UsoRam,
        "disco-temp" => ctx.TemperaturaDisco,
        "bateria" => ctx.Bateria,
        "cpu-clock" => ctx.ClockCpu,
        _ => null,
    };
}
