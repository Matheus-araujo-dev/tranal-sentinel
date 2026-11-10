using System.Globalization;

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
        valor is double v && double.IsFinite(v)
            ? NumeroTexto.Numero(Math.Round(v, 1, MidpointRounding.AwayFromZero)) + " °C"
            : Ausente;

    /// <summary>Uma porcentagem (inteiro, arredondado).</summary>
    public static string Porcentual(double? valor) =>
        valor is double v && double.IsFinite(v)
            ? NumeroTexto.Inteiro((long)Math.Round(v, MidpointRounding.AwayFromZero)) + "%"
            : Ausente;

    /// <summary>Um tamanho em gigabytes (uma casa decimal, sempre presente).</summary>
    public static string Gigabytes(double? valor) =>
        valor is double v && double.IsFinite(v)
            ? Decimal(Math.Round(v, 1, MidpointRounding.AwayFromZero), 1) + " GB"
            : Ausente;

    /// <summary>Uma frequência em MHz.</summary>
    public static string Mhz(double? valor) =>
        valor is double v && double.IsFinite(v)
            ? NumeroTexto.Inteiro((long)Math.Round(v, MidpointRounding.AwayFromZero)) + " MHz"
            : Ausente;

    /// <summary>Uma potência em watts.</summary>
    public static string Watts(double? valor) =>
        valor is double v && double.IsFinite(v)
            ? NumeroTexto.Inteiro((long)Math.Round(v, MidpointRounding.AwayFromZero)) + " W"
            : Ausente;

    /// <summary>Uma tensão em volts (três casas decimais).</summary>
    public static string Volts(double? valor) =>
        valor is double v && double.IsFinite(v)
            ? Decimal(Math.Round(v, 3, MidpointRounding.AwayFromZero), 3) + " V"
            : Ausente;

    /// <summary>Um número de rotações por minuto.</summary>
    public static string Rpm(double? valor) =>
        valor is double v && double.IsFinite(v)
            ? NumeroTexto.Inteiro((long)Math.Round(v, MidpointRounding.AwayFromZero)) + " RPM"
            : Ausente;

    /// <summary>
    /// Um número decimal com as casas sempre presentes ("8,0", "1,234").
    ///
    /// <para>
    /// O <see cref="NumeroTexto"/> corta os zeros à direita (formato "0.##"),
    /// e isso não serve para as unidades que pedem casas fixas: gigabytes e
    /// volts. Aqui o número sai com as casas garantidas, no mesmo padrão do
    /// app — vírgula decimal — e sem depender da cultura do Windows.
    /// </para>
    /// </summary>
    private static string Decimal(double valor, int casas) =>
        valor.ToString("F" + casas, CultureInfo.InvariantCulture).Replace('.', ',');
}
