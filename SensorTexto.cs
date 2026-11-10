namespace TranalSentinel;

/// <summary>
/// O texto das unidades dos sensores — o único lugar onde "temperatura",
/// "porcentagem", "gigabytes" etc. viram texto.
///
/// <para>
/// O número sai sempre do <see cref="NumeroTexto"/> (vírgula decimal, ponto de
/// milhar, independente da cultura do Windows). A unidade mora aqui, e não
/// espalhada pelo overlay, pelo painel e pela aba de ventoinhas: o mesmo
/// número tem que sair do mesmo jeito em todo o app.
///
/// <para>
/// Valor ausente vira "—" (nunca zero e nunca um valor velho): número errado
/// é pior que número ausente.
/// </para>
/// </summary>
public static class SensorTexto
{
    /// <summary>O texto de um valor que não deu para ler.</summary>
    public const string Ausente = "—";

    /// <summary>Uma temperatura em °C (uma casa decimal).</summary>
    public static string Temperatura(double? valor) =>
        valor is double v && CabeNaTela(v)
            ? NumeroTexto.Numero(Math.Round(v, 1, MidpointRounding.AwayFromZero)) + " °C"
            : Ausente;

    /// <summary>Uma porcentagem (inteiro, arredondado).</summary>
    public static string Porcentual(double? valor) =>
        valor is double v && CabeNaTela(v)
            ? NumeroTexto.Inteiro((long)Math.Round(v, MidpointRounding.AwayFromZero)) + "%"
            : Ausente;

    /// <summary>Um tamanho em gigabytes (uma casa decimal, sempre presente).</summary>
    public static string Gigabytes(double? valor) =>
        valor is double v && CabeNaTela(v)
            ? NumeroTexto.Numero(Math.Round(v, 1, MidpointRounding.AwayFromZero), 1) + " GB"
            : Ausente;

    /// <summary>Uma frequência em MHz.</summary>
    public static string Mhz(double? valor) =>
        valor is double v && CabeNaTela(v)
            ? NumeroTexto.Inteiro((long)Math.Round(v, MidpointRounding.AwayFromZero)) + " MHz"
            : Ausente;

    /// <summary>Uma frequência em GHz (duas casas decimais).</summary>
    public static string Gigahertz(double? valor) =>
        valor is double v && CabeNaTela(v)
            ? NumeroTexto.Numero(Math.Round(v, 2, MidpointRounding.AwayFromZero), 2) + " GHz"
            : Ausente;

    /// <summary>Uma potência em watts.</summary>
    public static string Watts(double? valor) =>
        valor is double v && CabeNaTela(v)
            ? NumeroTexto.Inteiro((long)Math.Round(v, MidpointRounding.AwayFromZero)) + " W"
            : Ausente;

    /// <summary>Uma tensão em volts (três casas decimais).</summary>
    public static string Volts(double? valor) =>
        valor is double v && CabeNaTela(v)
            ? NumeroTexto.Numero(Math.Round(v, 3, MidpointRounding.AwayFromZero), 3) + " V"
            : Ausente;

    /// <summary>Um número de rotações por minuto.</summary>
    public static string Rpm(double? valor) =>
        valor is double v && CabeNaTela(v)
            ? NumeroTexto.Inteiro((long)Math.Round(v, MidpointRounding.AwayFromZero)) + " RPM"
            : Ausente;

    /// <summary>
    /// O valor é um número que cabe na tela: um inteiro exato dentro do
    /// limite do <see cref="double"/> (2^53) e, portanto, dentro do
    /// <see cref="long"/>.
    ///
    /// <para>
    /// Um sensor que devolvesse 1e30 faria o cast saturar em
    /// <see cref="long.MaxValue"/> e a tela mostraria
    /// "9.223.372.036.854.775.807 RPM" — 19 dígitos. Número errado é pior
    /// que número ausente: fora do limite, o texto é "—".
    /// </para>
    /// </summary>
    private static bool CabeNaTela(double valor) =>
        double.IsFinite(valor) && Math.Abs(valor) <= 9.007199254740992E15;
}
