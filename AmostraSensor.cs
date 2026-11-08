namespace TranalSentinel;

/// <summary>
/// Uma amostra do estado dos sensores, como fica no histórico.
///
/// <para>
/// Tudo é anulável de propósito: um sensor que não existe naquele PC (ou que
/// o driver não deixou ler) fica vazio, em vez de virar zero — zero é um
/// valor válido e não pode servir de "não li".
/// </para>
/// </summary>
/// <param name="Momento">Quando a amostra foi tirada (hora local).</param>
/// <param name="CpuTemp">Temperatura da CPU, em °C.</param>
/// <param name="CpuCarga">Carga total da CPU, em %.</param>
/// <param name="CpuConsumo">Consumo do pacote da CPU, em W.</param>
/// <param name="CpuClock">Clock médio dos núcleos da CPU, em MHz.</param>
/// <param name="GpuTemp">Temperatura da GPU, em °C.</param>
/// <param name="GpuCarga">Carga da GPU, em %.</param>
/// <param name="GpuClock">Clock do núcleo da GPU, em MHz.</param>
/// <param name="GpuVram">Memória de vídeo em uso, em MB.</param>
/// <param name="RamUso">Memória RAM em uso, em %.</param>
/// <param name="DiscoLeitura">Taxa de leitura do disco, em MB/s.</param>
/// <param name="DiscoEscrita">Taxa de escrita do disco, em MB/s.</param>
public sealed record AmostraSensor(
    DateTime Momento,
    float? CpuTemp, float? CpuCarga, float? CpuConsumo, float? CpuClock,
    float? GpuTemp, float? GpuCarga, float? GpuClock, float? GpuVram,
    float? RamUso, float? DiscoLeitura, float? DiscoEscrita);
