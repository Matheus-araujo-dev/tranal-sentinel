using System.Diagnostics.Eventing.Reader;

namespace ResetExplode;

/// <summary>Um evento lido do log do Windows.</summary>
public sealed class EventoWindows
{
    public DateTime Data { get; init; }
    public int Id { get; init; }
    public string Provider { get; init; } = "";

    /// <summary>Valores do EventData, na mesma ordem das propriedades do evento.</summary>
    public IReadOnlyList<string> Dados { get; init; } = Array.Empty<string>();

    /// <summary>Valor da propriedade na posição indicada (null se não existir).</summary>
    public string? Valor(int indice) => indice >= 0 && indice < Dados.Count ? Dados[indice] : null;
}

/// <summary>
/// Leitura do log de eventos do Windows.
///
/// O EventLogReader do .NET não aceita filtro XPath para canal comum (só para
/// sessão exportada, o que gravaria arquivo temporário). Como o filtro nativo
/// do Windows seria uma chamada P/Invoke arriscada, a leitura é feita inteira e
/// a triagem acontece aqui: são ~20 mil eventos, lidos em cerca de 1 segundo.
///
/// Tudo é somente leitura.
/// </summary>
internal static class SaudeEventos
{
    /// <summary>Teto de eventos lidos por log (protege contra logs gigantes).</summary>
    private const int LimiteEventos = 300_000;

    /// <summary>
    /// Lê o log inteiro e entrega cada registro ao filtro.
    /// Devolve quantos eventos foram lidos.
    /// </summary>
    public static int Ler(string log, Action<EventRecord> aoEncontrar)
    {
        var total = 0;
        try
        {
            var consulta = new EventLogQuery(log, PathType.LogName)
            {
                ReverseDirection = true,
                TolerateQueryErrors = true
            };

            using var leitor = new EventLogReader(consulta);
            EventRecord? registro;
            while ((registro = leitor.ReadEvent()) != null)
            {
                using (registro)
                {
                    total++;
                    if (total > LimiteEventos) break;
                    try { aoEncontrar(registro); }
                    catch { }
                }
            }
        }
        catch { }
        return total;
    }

    /// <summary>Converte o registro em dados simples (data, id, provider, EventData).</summary>
    public static EventoWindows Converter(EventRecord registro)
    {
        var dados = new List<string>();
        try
        {
            foreach (var propriedade in registro.Properties)
                dados.Add(propriedade.Value?.ToString() ?? "");
        }
        catch { }

        return new EventoWindows
        {
            Data = registro.TimeCreated ?? DateTime.MinValue,
            Id = registro.Id,
            Provider = registro.ProviderName ?? "",
            Dados = dados
        };
    }
}
