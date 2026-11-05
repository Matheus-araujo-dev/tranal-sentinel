using System.Collections.Concurrent;
using LibreHardwareMonitor.Hardware;

namespace TranalSentinel;

/// <summary>
/// A coleta de verdade: é isto que roda dentro do processo filho.
///
/// <para>
/// Fica num processo separado de propósito. O LibreHardwareMonitor chama código
/// nativo do driver (a ADL da AMD, no caso da GPU) e, quando esse driver
/// devolve ponteiro inválido, o .NET leva um access violation dentro de
/// <c>Marshal.PtrToStructure</c> — que não dá para pegar em C# e derruba o
/// processo inteiro. Num processo isolado, o crash mata só o filho, que o app
/// reinicia sozinho (e, se foi a GPU, reinicia sem ela).
/// </para>
/// </summary>
internal sealed class SensorColeta : IDisposable
{
    private static readonly TimeSpan Intervalo = TimeSpan.FromSeconds(1);

    private readonly Computer _computer;
    private readonly HashSet<string> _desligados = new();
    private readonly CancellationTokenSource _cts = new();
    private SensorSnapshot? _snapshot;
    private Thread? _worker;

    /// <summary>Última falha da coleta (texto da exceção).</summary>
    public string? Erro { get; private set; }

    /// <summary>Hardware que falhou e foi desligado.</summary>
    public IReadOnlyList<string> Falhas { get; private set; } = Array.Empty<string>();

    /// <summary>A última leva de leituras.</summary>
    public SensorSnapshot? UltimoSnapshot => Volatile.Read(ref _snapshot);

    public SensorColeta(bool permitirGpuAdl)
    {
        _computer = new Computer
        {
            IsCpuEnabled = true,
            IsGpuEnabled = permitirGpuAdl,
            IsMemoryEnabled = true,
            IsMotherboardEnabled = true,
            IsStorageEnabled = true,
            IsNetworkEnabled = true,
            IsControllerEnabled = true
        };
    }

    /// <summary>Detecta o hardware e começa a coletar.</summary>
    public void Iniciar()
    {
        _computer.Open();
        _worker = new Thread(Loop) { IsBackground = true, Name = "Coleta" };
        _worker.Start();
    }

    private void Loop()
    {
        while (!_cts.IsCancellationRequested)
        {
            try
            {
                Coletar();
            }
            catch (Exception ex)
            {
                // A coleta nunca derruba o filho: ela só para de atualizar.
                Erro = ex.ToString();
            }

            try { _cts.Token.WaitHandle.WaitOne(Intervalo); }
            catch { break; }
        }
    }

    private void Coletar()
    {
        var leituras = new List<SensorReading>();
        var falhas = new List<string>();

        foreach (var hw in _computer.Hardware)
        {
            if (_cts.IsCancellationRequested) return;
            if (_desligados.Contains(hw.Identifier.ToString())) continue;

            try
            {
                hw.Update();
                Percorrer(hw, leituras, falhas);
            }
            catch (Exception ex)
            {
                // Um hardware problemático é desligado; os outros continuam.
                _desligados.Add(hw.Identifier.ToString());
                falhas.Add($"{hw.Name}: {ex.Message}");
            }
        }

        Falhas = falhas;
        Volatile.Write(ref _snapshot, new SensorSnapshot(leituras, falhas, DateTime.Now));
    }

    private void Percorrer(IHardware hw, List<SensorReading> leituras, List<string> falhas)
    {
        foreach (var s in hw.Sensors)
        {
            try
            {
                leituras.Add(new SensorReading(
                    s.Identifier.ToString(),
                    hw.Name,
                    SensorRow.CategoryName(s.SensorType),
                    s.Name,
                    s.SensorType,
                    hw.HardwareType,
                    s.Value, s.Min, s.Max));
            }
            catch
            {
                // Sensor individual que falha: pula e segue.
            }
        }

        foreach (var sub in hw.SubHardware)
        {
            if (_desligados.Contains(sub.Identifier.ToString())) continue;
            try
            {
                sub.Update();
                Percorrer(sub, leituras, falhas);
            }
            catch (Exception ex)
            {
                _desligados.Add(sub.Identifier.ToString());
                falhas.Add($"{sub.Name}: {ex.Message}");
            }
        }
    }

    public void Dispose()
    {
        try { _cts.Cancel(); } catch { }
        try { _worker?.Join(2000); } catch { }
        try { _computer.Close(); } catch { }
        _cts.Dispose();
    }
}
