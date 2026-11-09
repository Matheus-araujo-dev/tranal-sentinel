namespace TranalSentinel;

/// <summary>
/// A frase que resume o que os sensores fizeram numa janela de tempo.
///
/// <para>
/// A regra mora aqui e é usada em dois lugares: a retrospectiva do
/// travamento (TRANAL SAÚDE) e a explicação do alerta (TRANAL ALERTAS).
/// Antes cada um tinha a sua cópia — e a primeira vez que uma delas mudou,
/// a outra passou a mentir. Agora há uma só.
/// </para>
/// </summary>
public static class ResumoAmostras
{
    /// <summary>
    /// Descreve a janela de amostras em uma frase: "CPU a 78,5 °C · carga
    /// da CPU 43 % · ...".
    ///
    /// <para>
    /// Sensor que nunca apareceu não entra — o resumo não inventa valor.
    /// </para>
    /// </summary>
    public static string Descrever(List<AmostraSensor> janela)
    {
        var itens = new List<string>();

        var tempCpu = Maximo(janela, a => a.CpuTemp);
        if (tempCpu != null) itens.Add($"CPU a {NumeroTexto.Numero(tempCpu.Value)} °C");

        var tempGpu = Maximo(janela, a => a.GpuTemp);
        if (tempGpu != null) itens.Add($"GPU a {NumeroTexto.Numero(tempGpu.Value)} °C");

        var cargaCpu = Media(janela, a => a.CpuCarga);
        if (cargaCpu != null) itens.Add($"carga da CPU {NumeroTexto.Numero(cargaCpu.Value)} %");

        var cargaGpu = Maximo(janela, a => a.GpuCarga);
        if (cargaGpu != null) itens.Add($"GPU {NumeroTexto.Numero(cargaGpu.Value)} %");

        var ram = Ultimo(janela, a => a.RamUso);
        if (ram != null) itens.Add($"RAM {NumeroTexto.Numero(ram.Value)} %");

        var leitura = Ultimo(janela, a => a.DiscoLeitura);
        var escrita = Ultimo(janela, a => a.DiscoEscrita);
        if (leitura != null || escrita != null)
            itens.Add($"disco {NumeroTexto.Numero(Math.Max(leitura ?? 0, escrita ?? 0))} MB/s");

        var clock = Ultimo(janela, a => a.CpuClock);
        if (clock != null) itens.Add($"clock da CPU {NumeroTexto.Numero(clock.Value / 1000f)} GHz");

        return string.Join(" · ", itens);
    }

    /// <summary>O maior valor da janela (null quando o sensor nunca apareceu).</summary>
    private static float? Maximo(List<AmostraSensor> janela, Func<AmostraSensor, float?> seletor)
    {
        var valores = janela.Select(seletor).Where(v => v != null).Select(v => v!.Value).ToList();
        return valores.Count > 0 ? valores.Max() : null;
    }

    /// <summary>A média da janela (null quando o sensor nunca apareceu).</summary>
    private static float? Media(List<AmostraSensor> janela, Func<AmostraSensor, float?> seletor)
    {
        var valores = janela.Select(seletor).Where(v => v != null).Select(v => v!.Value).ToList();
        return valores.Count > 0 ? valores.Average() : null;
    }

    /// <summary>O último valor da janela (null quando o sensor nunca apareceu).</summary>
    private static float? Ultimo(List<AmostraSensor> janela, Func<AmostraSensor, float?> seletor)
    {
        for (int i = janela.Count - 1; i >= 0; i--)
        {
            var valor = seletor(janela[i]);
            if (valor != null) return valor;
        }
        return null;
    }

}
