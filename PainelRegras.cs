namespace TranalSentinel;

/// <summary>
/// Uma ventoinha como o painel a mostra: o nome legível e o RPM de agora
/// (null quando ela não está dando leitura).
/// </summary>
/// <param name="Nome">O nome do sensor ("Fan #1"), nunca o id interno.</param>
/// <param name="Rpm">As rotações por minuto (null quando não deu para ler).</param>
public sealed record PainelVentoinha(string Nome, double? Rpm);

/// <summary>
/// O retrato do PC que o painel mostra agora.
/// </summary>
/// <param name="TemperaturaCpu">A temperatura da CPU em °C (null se não deu para ler).</param>
/// <param name="CargaCpu">A carga total da CPU em % (null se não deu para ler).</param>
/// <param name="TemperaturaGpu">A temperatura da GPU em °C (null se não deu para ler).</param>
/// <param name="CargaGpu">A carga da GPU em % (null se não deu para ler).</param>
/// <param name="RamPct">A memória RAM em uso em % (null se não deu para ler).</param>
/// <param name="Ventoinhas">As ventoinhas que o app conhece agora.</param>
public sealed record PainelRetrato(
    double? TemperaturaCpu,
    double? CargaCpu,
    double? TemperaturaGpu,
    double? CargaGpu,
    double? RamPct,
    IReadOnlyList<PainelVentoinha> Ventoinhas);

/// <summary>Uma linha do painel: o rótulo e o valor (prontos para a tela).</summary>
public sealed record PainelLinha(string Rotulo, string Valor);

/// <summary>
/// As regras do painel flutuante — toda ela mora aqui.
///
/// <para>
/// O painel mostra quatro blocos, sempre na mesma ordem: CPU, GPU, RAM e
/// ventoinha. O que não deu para ler vira "—" (nunca zero e nunca um valor
/// velho) e os números saem do <see cref="SensorTexto"/>.
///
/// <para>
/// A ventoinha mostrada é a de <b>maior RPM entre as que estão dando
/// leitura</b> (a que está trabalhando). Sem isso, o painel mostraria a
/// ventoinha parada e o dono pensaria que o PC está com ela morta.
/// </para>
/// </summary>
public static class PainelRegras
{
    /// <summary>As quatro linhas do painel.</summary>
    public static IReadOnlyList<PainelLinha> Linhas(PainelRetrato retrato)
    {
        return new[]
        {
            new PainelLinha("CPU", Juntar(SensorTexto.Temperatura(retrato.TemperaturaCpu), SensorTexto.Porcentual(retrato.CargaCpu))),
            new PainelLinha("GPU", Juntar(SensorTexto.Temperatura(retrato.TemperaturaGpu), SensorTexto.Porcentual(retrato.CargaGpu))),
            new PainelLinha("RAM", SensorTexto.Porcentual(retrato.RamPct)),
            new PainelLinha("VENT", ValorDaVentoinha(EscolherVentoinha(retrato.Ventoinhas))),
        };
    }

    /// <summary>
    /// A ventoinha que o painel mostra: a de maior RPM entre as que estão
    /// dando leitura. Se nenhuma estiver dando leitura, a primeira que o app
    /// conhece (sem RPM — o número que não existe não é inventado).
    /// </summary>
    public static PainelVentoinha? EscolherVentoinha(IReadOnlyList<PainelVentoinha> ventoinhas)
    {
        if (ventoinhas.Count == 0) return null;

        var comLeitura = ventoinhas.Where(v => v.Rpm is not null).ToList();

        if (comLeitura.Count > 0)
        {
            // O desempate é pelo nome: duas ventoinhas com o mesmo RPM têm
            // que dar sempre o mesmo painel.
            return comLeitura
                .OrderByDescending(v => v.Rpm!.Value)
                .ThenBy(v => v.Nome, StringComparer.Ordinal)
                .First();
        }

        return ventoinhas.OrderBy(v => v.Nome, StringComparer.Ordinal).First();
    }

    /// <summary>O valor da linha da ventoinha ("Fan #1 · 3.409 RPM").</summary>
    private static string ValorDaVentoinha(PainelVentoinha? ventoinha)
    {
        if (ventoinha is null) return SensorTexto.Ausente;

        string rpm = SensorTexto.Rpm(ventoinha.Rpm);
        if (string.IsNullOrWhiteSpace(ventoinha.Nome)) return rpm;

        return ventoinha.Nome + " · " + rpm;
    }

    /// <summary>
    /// Junta as partes de uma linha: o que não deu para ler some (e não
    /// vira "— · —").
    /// </summary>
    private static string Juntar(params string[] partes)
    {
        var uteis = partes.Where(p => p != SensorTexto.Ausente).ToList();
        if (uteis.Count == 0) return SensorTexto.Ausente;
        return string.Join(" · ", uteis);
    }
}
