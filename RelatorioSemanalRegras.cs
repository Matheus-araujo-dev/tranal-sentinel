namespace TranalSentinel;

/// <summary>
/// O relatório semanal: um resumo de tudo que aconteceu no PC na última
/// semana. Compilado toda segunda-feira a partir dos dados que o banco
/// já guarda (agregados, uso, tráfego, ventoinhas, alertas).
/// </summary>
public sealed record RelatorioSemanal(
    DateTime Inicio,
    DateTime Fim,
    ResumoSensoresSemanal Sensores,
    ResumoUsoSemanal Uso,
    ResumoTrafegoSemanal Trafego,
    IReadOnlyList<string> Alertas,
    IReadOnlyList<Tendencia> Tendencias,
    IReadOnlyList<DesvioBaseline> Desvios);

/// <summary>Resumo dos sensores na semana.</summary>
public sealed record ResumoSensoresSemanal(
    float? CpuTempMedia, float? CpuTempMax,
    float? GpuTempMedia, float? GpuTempMax,
    float? RamUsoMedia, float? RamUsoMax,
    int DiasComDado);

/// <summary>Resumo do uso do PC na semana.</summary>
public sealed record ResumoUsoSemanal(
    int TotalHoras,
    string? ProgramaMaisUsado,
    int ProgramasDistintos);

/// <summary>Resumo do tráfego de rede na semana.</summary>
public sealed record ResumoTrafegoSemanal(
    long TotalBytes,
    string? ProgramaMaisUsou,
    int DiasComDado);

/// <summary>
/// Regras puras do relatório semanal: compila dados de várias fontes
/// num resumo legível. Nenhum I/O aqui — quem busca os dados é o
/// orquestrador que chama estas funções.
///
/// <para>
/// A ideia é do Sysoptimizer ("weekly Monday report"), mas o relatório
/// é do TRANAL SENTINEL: em português, com os dados que o app já coleta,
/// sem dependência nova.
/// </para>
/// </summary>
public static class RelatorioSemanalRegras
{
    /// <summary>
    /// Dia da semana em que o relatório é gerado.
    /// </summary>
    public const DayOfWeek DiaDoRelatorio = DayOfWeek.Monday;

    /// <summary>
    /// Calcula o resumo dos sensores da semana a partir dos agregados diários.
    /// </summary>
    public static ResumoSensoresSemanal ResumoSensores(IReadOnlyList<AgregadoDiario> agregados)
    {
        if (agregados.Count == 0)
            return new ResumoSensoresSemanal(null, null, null, null, null, null, 0);

        float cpuTSum = 0; int cpuTN = 0; float cpuTMax = float.MinValue;
        float gpuTSum = 0; int gpuTN = 0; float gpuTMax = float.MinValue;
        float ramSum = 0; int ramN = 0; float ramMax = float.MinValue;

        foreach (var a in agregados)
        {
            if (a.CpuTempMedia is float ct) { cpuTSum += ct; cpuTN++; }
            if (a.CpuTempMax is float ctm && ctm > cpuTMax) cpuTMax = ctm;
            if (a.GpuTempMedia is float gt) { gpuTSum += gt; gpuTN++; }
            if (a.GpuTempMax is float gtm && gtm > gpuTMax) gpuTMax = gtm;
            if (a.RamUsoMedia is float rm) { ramSum += rm; ramN++; }
            if (a.RamUsoMax is float rmm && rmm > ramMax) ramMax = rmm;
        }

        return new ResumoSensoresSemanal(
            cpuTN > 0 ? cpuTSum / cpuTN : null,
            cpuTN > 0 ? cpuTMax : null,
            gpuTN > 0 ? gpuTSum / gpuTN : null,
            gpuTN > 0 ? gpuTMax : null,
            ramN > 0 ? ramSum / ramN : null,
            ramN > 0 ? ramMax : null,
            agregados.Count);
    }

    /// <summary>
    /// Monta o texto completo do relatório semanal.
    /// </summary>
    public static string Texto(RelatorioSemanal r)
    {
        var linhas = new List<string>();

        linhas.Add($"RELATÓRIO SEMANAL — {NumeroTexto.Data(r.Inicio)} a {NumeroTexto.Data(r.Fim)}");
        linhas.Add("");

        // Sensores
        linhas.Add("SENSORES");
        if (r.Sensores.DiasComDado == 0)
        {
            linhas.Add("  Sem dados de sensores nesta semana.");
        }
        else
        {
            if (r.Sensores.CpuTempMedia is float ct)
                linhas.Add($"  CPU: média {NumeroTexto.Numero(ct, 1)} °C, pico {NumeroTexto.Numero(r.Sensores.CpuTempMax!.Value, 1)} °C");
            if (r.Sensores.GpuTempMedia is float gt)
                linhas.Add($"  GPU: média {NumeroTexto.Numero(gt, 1)} °C, pico {NumeroTexto.Numero(r.Sensores.GpuTempMax!.Value, 1)} °C");
            if (r.Sensores.RamUsoMedia is float rm)
                linhas.Add($"  RAM: média {NumeroTexto.Numero(rm, 1)}%, pico {NumeroTexto.Numero(r.Sensores.RamUsoMax!.Value, 1)}%");
            linhas.Add($"  ({r.Sensores.DiasComDado} dias com dados)");
        }
        linhas.Add("");

        // Uso
        linhas.Add("USO DO PC");
        if (r.Uso.TotalHoras > 0)
        {
            linhas.Add($"  Tempo total: ~{r.Uso.TotalHoras} horas");
            if (r.Uso.ProgramaMaisUsado is string prog)
                linhas.Add($"  Programa mais usado: {prog}");
            linhas.Add($"  Programas distintos: {r.Uso.ProgramasDistintos}");
        }
        else
        {
            linhas.Add("  Sem dados de uso nesta semana.");
        }
        linhas.Add("");

        // Tráfego
        linhas.Add("TRÁFEGO DE REDE");
        if (r.Trafego.TotalBytes > 0)
        {
            linhas.Add($"  Total: {FormatarBytes(r.Trafego.TotalBytes)}");
            if (r.Trafego.ProgramaMaisUsou is string prog)
                linhas.Add($"  Programa que mais usou: {prog}");
        }
        else
        {
            linhas.Add("  Sem dados de tráfego nesta semana.");
        }
        linhas.Add("");

        // Tendências
        if (r.Tendencias.Count > 0)
        {
            linhas.Add("TENDÊNCIAS");
            foreach (var t in r.Tendencias)
                linhas.Add($"  {TendenciaRegras.TextoTendencia(t)}");
            linhas.Add("");
        }

        // Desvios da baseline
        if (r.Desvios.Count > 0)
        {
            linhas.Add("DESVIOS DA BASELINE");
            foreach (var d in r.Desvios)
                linhas.Add($"  {BaselineRegras.TextoDesvio(d)}");
            linhas.Add("");
        }

        // Alertas
        if (r.Alertas.Count > 0)
        {
            linhas.Add("ALERTAS DA SEMANA");
            foreach (var a in r.Alertas)
                linhas.Add($"  • {a}");
            linhas.Add("");
        }

        return string.Join(Environment.NewLine, linhas);
    }

    /// <summary>
    /// Devolve true se hoje é dia de gerar o relatório e ele ainda não
    /// foi gerado nesta semana (a data do último relatório é anterior
    /// ao início desta semana).
    /// </summary>
    public static bool DeveGerar(DateTime agora, DateTime? ultimoRelatorio)
    {
        if (agora.DayOfWeek != DiaDoRelatorio) return false;

        if (ultimoRelatorio is null) return true;

        DateTime inicioSemana = agora.Date.AddDays(-(int)agora.DayOfWeek + (int)DayOfWeek.Monday);
        return ultimoRelatorio.Value.Date < inicioSemana;
    }

    /// <summary>
    /// Calcula o início e o fim da semana anterior (segunda a domingo).
    /// </summary>
    public static (DateTime Inicio, DateTime Fim) SemanaAnterior(DateTime agora)
    {
        DateTime inicioEstaSemana = agora.Date.AddDays(-(int)agora.DayOfWeek + (int)DayOfWeek.Monday);
        DateTime inicioSemanaAnterior = inicioEstaSemana.AddDays(-7);
        DateTime fimSemanaAnterior = inicioEstaSemana.AddDays(-1);
        return (inicioSemanaAnterior, fimSemanaAnterior);
    }

    private static string FormatarBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{NumeroTexto.Numero(bytes / 1024.0, 1)} KB";
        if (bytes < 1024L * 1024 * 1024) return $"{NumeroTexto.Numero(bytes / (1024.0 * 1024), 1)} MB";
        return $"{NumeroTexto.Numero(bytes / (1024.0 * 1024 * 1024), 2)} GB";
    }
}
