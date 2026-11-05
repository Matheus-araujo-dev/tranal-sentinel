using LibreHardwareMonitor.Hardware;

namespace TranalSentinel;

/// <summary>
/// O mínimo que a coleta precisa saber de um hardware.
///
/// <para>
/// Existe para que a regra da volta (o que é falha, o que desliga o
/// hardware, o que é o motivo da CPU) possa ser testada sem depender do
/// driver — e para que o LibreHardwareMonitor fique num lugar só, o
/// adaptador <see cref="HardwareLibre"/>.
/// </para>
/// </summary>
internal interface HardwareColeta
{
    /// <summary>Identificador estável do hardware (o do driver).</summary>
    string Id { get; }

    /// <summary>Nome que aparece na tela.</summary>
    string Nome { get; }

    /// <summary>Tipo do hardware (CPU, GPU, disco...).</summary>
    HardwareType Tipo { get; }

    /// <summary>
    /// Lê os sensores deste hardware. Pode lançar: o erro thrown é o motivo
    /// da falha (e o hardware é desligado).
    /// </summary>
    IReadOnlyList<SensorReading> Ler();

    /// <summary>Sub-hardwares (a CPU tem os núcleos; a placa, os sensores).</summary>
    IReadOnlyList<HardwareColeta> Subs { get; }
}
