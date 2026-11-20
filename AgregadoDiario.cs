namespace TranalSentinel;

/// <summary>
/// Um agregado de um dia inteiro de amostras de sensor: mínima, máxima e
/// média de cada métrica. É o que permite guardar tendências de longo prazo
/// (365 dias) sem manter todas as amostras brutas (que ficam só 15 dias).
/// </summary>
public sealed record AgregadoDiario(
    DateTime Dia,
    float? CpuTempMin, float? CpuTempMax, float? CpuTempMedia,
    float? CpuCargaMin, float? CpuCargaMax, float? CpuCargaMedia,
    float? GpuTempMin, float? GpuTempMax, float? GpuTempMedia,
    float? GpuCargaMin, float? GpuCargaMax, float? GpuCargaMedia,
    float? RamUsoMin, float? RamUsoMax, float? RamUsoMedia,
    int Amostras);

/// <summary>
/// As regras puras do agregado diário: dado um conjunto de amostras brutas,
/// calcula o mínimo, o máximo e a média de cada sensor. Nenhum I/O aqui —
/// quem lê e grava é o <see cref="Banco"/>.
/// </summary>
public static class AgregadoDiarioRegras
{
    /// <summary>
    /// Calcula o agregado de um dia a partir das amostras brutas.
    /// </summary>
    public static AgregadoDiario Calcular(DateTime dia, IReadOnlyList<AmostraSensor> amostras)
    {
        if (amostras.Count == 0)
            return new AgregadoDiario(dia, null, null, null, null, null, null,
                null, null, null, null, null, null, null, null, null, 0);

        float cpuTMin = float.MaxValue, cpuTMax = float.MinValue, cpuTSum = 0; int cpuTN = 0;
        float cpuCMin = float.MaxValue, cpuCMax = float.MinValue, cpuCSum = 0; int cpuCN = 0;
        float gpuTMin = float.MaxValue, gpuTMax = float.MinValue, gpuTSum = 0; int gpuTN = 0;
        float gpuCMin = float.MaxValue, gpuCMax = float.MinValue, gpuCSum = 0; int gpuCN = 0;
        float ramMin = float.MaxValue, ramMax = float.MinValue, ramSum = 0; int ramN = 0;

        foreach (var a in amostras)
        {
            Acumular(a.CpuTemp, ref cpuTMin, ref cpuTMax, ref cpuTSum, ref cpuTN);
            Acumular(a.CpuCarga, ref cpuCMin, ref cpuCMax, ref cpuCSum, ref cpuCN);
            Acumular(a.GpuTemp, ref gpuTMin, ref gpuTMax, ref gpuTSum, ref gpuTN);
            Acumular(a.GpuCarga, ref gpuCMin, ref gpuCMax, ref gpuCSum, ref gpuCN);
            Acumular(a.RamUso, ref ramMin, ref ramMax, ref ramSum, ref ramN);
        }

        return new AgregadoDiario(dia,
            Fin(cpuTMin, cpuTN), Fin(cpuTMax, cpuTN), Media(cpuTSum, cpuTN),
            Fin(cpuCMin, cpuCN), Fin(cpuCMax, cpuCN), Media(cpuCSum, cpuCN),
            Fin(gpuTMin, gpuTN), Fin(gpuTMax, gpuTN), Media(gpuTSum, gpuTN),
            Fin(gpuCMin, gpuCN), Fin(gpuCMax, gpuCN), Media(gpuCSum, gpuCN),
            Fin(ramMin, ramN), Fin(ramMax, ramN), Media(ramSum, ramN),
            amostras.Count);
    }

    private static void Acumular(float? valor, ref float min, ref float max, ref float soma, ref int n)
    {
        if (valor is null) return;
        float v = valor.Value;
        if (v < min) min = v;
        if (v > max) max = v;
        soma += v;
        n++;
    }

    private static float? Fin(float valor, int n) => n > 0 ? valor : null;
    private static float? Media(float soma, int n) => n > 0 ? soma / n : null;
}
