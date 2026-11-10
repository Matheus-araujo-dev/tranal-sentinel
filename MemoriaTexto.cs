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

    /// <summary>Um tempo em nanossegundos.</summary>
    public static string Ns(double nanossegundos) => NumeroTexto.Numero(nanossegundos) + " ns";

    /// <summary>Um tempo em picossegundos.</summary>
    public static string Ps(double picossegundos) => NumeroTexto.Numero(picossegundos) + " ps";

    /// <summary>Uma tensão em volts.</summary>
    public static string Voltagem(double volts) => SensorTexto.Volts(volts, 2);

    /// <summary>Uma quantidade de bytes em gigabytes.</summary>
    public static string Bytes(long bytes) =>
        SensorTexto.Gigabytes(Math.Round(bytes / 1024.0 / 1024.0 / 1024.0, 1));
}
