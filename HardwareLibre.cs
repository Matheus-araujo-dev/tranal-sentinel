using System.Linq;
using LibreHardwareMonitor.Hardware;

namespace TranalSentinel;

/// <summary>
/// Um hardware do LibreHardwareMonitor visto pela coleta.
///
/// <para>
/// É o único lugar do app que conhece o LibreHardwareMonitor na leitura: a
/// regra da volta (<see cref="SensorColeta.MontarVolta"/>) só conversa com
/// <see cref="HardwareColeta"/>.
/// </para>
/// </summary>
internal sealed class HardwareLibre : HardwareColeta
{
    private readonly IHardware _hw;

    public HardwareLibre(IHardware hw) => _hw = hw;

    public string Id => _hw.Identifier.ToString();

    public string Nome => _hw.Name;

    public HardwareType Tipo => _hw.HardwareType;

    public IReadOnlyList<HardwareColeta> Subs =>
        _hw.SubHardware.Select(h => (HardwareColeta)new HardwareLibre(h)).ToList();

    /// <summary>
    /// Pergunta ao driver e lê os sensores. O erro thrown aqui (acesso
    /// negado, driver ocupado) é o motivo da falha do hardware.
    /// </summary>
    public IReadOnlyList<SensorReading> Ler()
    {
        _hw.Update();

        var leituras = new List<SensorReading>();
        foreach (var s in _hw.Sensors)
        {
            try
            {
                leituras.Add(new SensorReading(
                    s.Identifier.ToString(),
                    _hw.Name,
                    SensorRow.CategoryName(s.SensorType),
                    s.Name,
                    s.SensorType,
                    _hw.HardwareType,
                    SensorValor.Finito(s.Value),
                    SensorValor.Finito(s.Min),
                    SensorValor.Finito(s.Max)));
            }
            catch
            {
                // Sensor individual que falha: pula e segue (os outros
                // sensores do mesmo hardware continuam válidos).
            }
        }
        return leituras;
    }
}
