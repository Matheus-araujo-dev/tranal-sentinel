using System.Globalization;

namespace TranalSentinel;

/// <summary>
/// Uma linha de uma consulta ao banco.
///
/// <para>
/// Os valores vêm com o tipo que o SQLite guardou: inteiro, real, texto ou
/// nulo. Os atalhos (<see cref="Inteiro"/>, <see cref="Real"/>,
/// <see cref="Texto"/>, <see cref="Momento"/>) convertem com cuidado —
/// número ausente vira "não li", nunca zero.
/// </para>
/// </summary>
public sealed class SqliteLinha
{
    private readonly string[] _nomes;
    private readonly Dictionary<string, object?> _valores;

    internal SqliteLinha(string[] nomes, Dictionary<string, object?> valores)
    {
        _nomes = nomes;
        _valores = valores;
    }

    /// <summary>Os nomes das colunas, na ordem.</summary>
    public IReadOnlyList<string> Colunas => _nomes;

    /// <summary>O valor cru de uma coluna (null quando não existe).</summary>
    public object? Valor(string coluna) =>
        _valores.TryGetValue(coluna, out object? valor) ? valor : null;

    /// <summary>True quando a coluna existe e não é nula.</summary>
    public bool TemValor(string coluna) =>
        _valores.TryGetValue(coluna, out object? valor) && valor is not null;

    /// <summary>
    /// O valor de uma coluna numérica (0 quando não há). Para contadores,
    /// onde "não li" e "zero" são a mesma coisa.
    /// </summary>
    public long Inteiro(string coluna)
    {
        object? valor = Valor(coluna);
        return valor switch
        {
            long numero => numero,
            double real => (long)real,
            int numero => numero,
            float real => (long)real,
            string texto when long.TryParse(texto, NumberStyles.Integer, CultureInfo.InvariantCulture, out long convertido) => convertido,
            _ => 0,
        };
    }

    /// <summary>O valor de uma coluna numérica real (0 quando não há).</summary>
    public double Real(string coluna)
    {
        object? valor = Valor(coluna);
        return valor switch
        {
            double numero => numero,
            long numero => numero,
            int numero => numero,
            float numero => numero,
            string texto when double.TryParse(texto, NumberStyles.Float, CultureInfo.InvariantCulture, out double convertido) => convertido,
            _ => 0,
        };
    }

    /// <summary>O valor de uma coluna de texto ("" quando não há).</summary>
    public string Texto(string coluna)
    {
        object? valor = Valor(coluna);
        return valor switch
        {
            null => "",
            string texto => texto,
            _ => Convert.ToString(valor, CultureInfo.InvariantCulture) ?? "",
        };
    }

    /// <summary>
    /// O instante de uma coluna de tempo (epoch em milissegundos). O
    /// Sqlite guarda a hora como epoch, então volta com hora local e sem
    /// surpresa de horário de verão.
    /// </summary>
    public DateTime Momento(string coluna) => Sqlite.UnixParaMomento(Inteiro(coluna));
}
