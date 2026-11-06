using System.Globalization;

namespace TranalSentinel;

/// <summary>
/// Os textos da aba "Uso": como o tempo, o percentual e a temperatura são
/// mostrados na tela.
///
/// <para>
/// A regra mora aqui, e não na janela, para o mesmo número sair sempre do
/// mesmo jeito: "2 h 15 min", "42 %", "81,5 °C". É função pura — não lê
/// arquivo, não olha o Windows, não mexe no PC.
/// </para>
/// </summary>
public static class UsoTexto
{
    /// <summary>Números no padrão brasileiro (vírgula decimal).</summary>
    private static readonly CultureInfo Br = CultureBrasil();

    /// <summary>
    /// Uma duração no padrão brasileiro: "2 h 15 min", "45 min", "30 s".
    ///
    /// <para>
    /// Tempo negativo ou zero é "0 s": relógio que andou para trás não vira
    /// "-5 min" na tela.
    /// </para>
    /// </summary>
    public static string Duracao(TimeSpan duracao)
    {
        if (duracao <= TimeSpan.Zero) return "0 s";

        if (duracao.TotalHours >= 1)
        {
            int horas = (int)duracao.TotalHours;
            int minutos = duracao.Minutes;
            return minutos == 0 ? $"{horas} h" : $"{horas} h {minutos} min";
        }

        if (duracao.TotalMinutes >= 1) return $"{duracao.Minutes} min";
        return $"{duracao.Seconds} s";
    }

    /// <summary>
    /// Um percentual no padrão brasileiro: "42 %", "42,4 %".
    ///
    /// <para>
    /// Percentual que não é número vira "—": o app não mostra número
    /// quebrado.
    /// </para>
    /// </summary>
    public static string Percentual(double percentual)
    {
        if (double.IsNaN(percentual) || double.IsInfinity(percentual)) return "—";
        return percentual.ToString("0.#", Br) + " %";
    }

    /// <summary>
    /// Uma temperatura no padrão brasileiro: "81,5 °C".
    ///
    /// <para>
    /// Sem valor não há temperatura: devolve "—" em vez de zero graus, que
    /// seria mentira.
    /// </para>
    /// </summary>
    public static string Temperatura(float? celsius)
    {
        if (celsius is null || !float.IsFinite(celsius.Value)) return "—";
        return celsius.Value.ToString("0.#", Br) + " °C";
    }

    /// <summary>
    /// O rótulo do dia no seletor: "hoje", "ontem" ou a data.
    /// </summary>
    public static string RotuloDia(DateTime dia, DateTime hoje)
    {
        DateTime alvo = dia.Date;
        if (alvo == hoje.Date) return "hoje";
        if (alvo == hoje.Date.AddDays(-1)) return "ontem";
        return alvo.ToString("dd/MM/yyyy", Br);
    }

    /// <summary>A hora no padrão brasileiro: "14:31".</summary>
    public static string Hora(DateTime momento) => momento.ToString("HH:mm", Br);

    /// <summary>
    /// A cultura brasileira. Se o Windows vier sem ela (modo invariante),
    /// o texto sai com ponto decimal em vez de quebrar a tela.
    /// </summary>
    private static CultureInfo CultureBrasil()
    {
        try { return CultureInfo.GetCultureInfo("pt-BR"); }
        catch
        {
            // Windows sem a cultura pt-BR: a invariante mostra a vírgula
            // como ponto, o que é feio mas não quebra nada.
            return CultureInfo.InvariantCulture;
        }
    }
}
