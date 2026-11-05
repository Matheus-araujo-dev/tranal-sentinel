using System.Globalization;

namespace TranalSentinel;

/// <summary>
/// A formatação dos números da memória, em um lugar só.
///
/// <para>
/// A tela TRANAL MEMÓRIA e o relatório de linha de comando mostram as mesmas
/// grandezas (tCK em ps, tempos em ns, tensão do perfil). Se cada um tivesse
/// o seu próprio <c>FormatarNumero</c>, os dois passariam a mostrar coisas
/// diferentes na primeira edição em um deles.
/// </para>
/// </summary>
public static class MemoriaTexto
{
    /// <summary>Número no padrão brasileiro (vírgula decimal), sem casas demais.</summary>
    public static string Numero(double valor) =>
        valor.ToString("0.##", CultureInfo.InvariantCulture).Replace('.', ',');

    /// <summary>Um tempo em nanossegundos.</summary>
    public static string Ns(double nanossegundos) => Numero(nanossegundos) + " ns";

    /// <summary>Um tempo em picossegundos.</summary>
    public static string Ps(double picossegundos) => Numero(picossegundos) + " ps";

    /// <summary>Uma tensão em volts.</summary>
    public static string Voltagem(double volts) => Numero(volts) + " V";

    /// <summary>Uma quantidade de bytes em gigabytes.</summary>
    public static string Bytes(long bytes) =>
        Numero(Math.Round(bytes / 1024.0 / 1024.0 / 1024.0, 1)) + " GB";
}
