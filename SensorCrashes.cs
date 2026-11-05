using System.Diagnostics.Eventing.Reader;

namespace TranalSentinel;

/// <summary>
/// Lê os crashes do próprio app que o Windows registrou no log "Application".
///
/// O Windows grava "Application Error" (evento 1000) com o nome do aplicativo,
/// o módulo que falhou e o código de exceção. É a mesma evidência que o
/// Reliability Monitor mostra — e a única forma de saber, numa execução
/// seguinte, que o app morreu por access violation dentro do driver de
/// sensores.
///
/// Só leitura: nada aqui altera o sistema.
/// </summary>
public static class SensorCrashes
{
    /// <summary>Quantos eventos do log são lidos no máximo (o log é lido de trás para frente).</summary>
    private const int MaxEventos = 6000;

    /// <summary>Evento "Application Error" do Windows.</summary>
    private const int IdApplicationError = 1000;

    /// <summary>
    /// Lê os crashes do app. Devolve uma lista (possivelmente vazia) — nunca
    /// lança: uma falha de leitura só significa "não há evidência".
    /// </summary>
    public static List<CrashDoApp> Ler(string nomeAplicativo, int maxEventos = MaxEventos)
    {
        var lista = new List<CrashDoApp>();
        try
        {
            var consulta = new EventLogQuery("Application", PathType.LogName)
            {
                ReverseDirection = true,
                TolerateQueryErrors = true
            };

            using var leitor = new EventLogReader(consulta);
            EventRecord? registro;
            int lidos = 0;
            while ((registro = leitor.ReadEvent()) != null)
            {
                using (registro)
                {
                    lidos++;
                    if (lidos > maxEventos) break;
                    if (registro.Id != IdApplicationError) continue;
                    if (!string.Equals(registro.ProviderName, "Application Error", StringComparison.OrdinalIgnoreCase)) continue;

                    var crash = Converter(SaudeEventos.Converter(registro), nomeAplicativo);
                    if (crash != null) lista.Add(crash);
                }
            }
        }
        catch
        {
            // Sem log legível é o mesmo que "sem evidência de crash".
        }
        return lista;
    }

    /// <summary>
    /// Converte o EventData do evento 1000 em um crash do app.
    /// Puro: devolve null quando o evento não é do aplicativo procurado.
    /// A ordem das propriedades é a padrão do Windows:
    /// 0 AppName, 3 ModuleName, 6 ExceptionCode.
    /// </summary>
    public static CrashDoApp? Converter(EventoWindows ev, string nomeAplicativo)
    {
        string app = ev.Valor(0) ?? "";
        if (app.Length == 0) return null;
        if (!app.Contains(nomeAplicativo, StringComparison.OrdinalIgnoreCase)) return null;

        return new CrashDoApp(
            ev.Data,
            SensorGuard.NormalizarCodigo(ev.Valor(6)),
            app,
            ev.Valor(3) ?? "");
    }
}
