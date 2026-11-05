using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using LibreHardwareMonitor.Hardware;

namespace TranalSentinel;

/// <summary>
/// Uma volta da coleta: as leituras, as falhas por hardware e o motivo da CPU
/// (quando foi ela que não pôde ser lida).
/// </summary>
internal sealed record ColetaVolta(
    IReadOnlyList<SensorReading> Leituras,
    IReadOnlyList<string> Falhas,
    string? FalhaCpu);

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
///
/// <para>
/// A regra da volta mora em <see cref="MontarVolta"/>: o que é falha, o que
/// desliga o hardware, o que é o motivo da CPU. O LibreHardwareMonitor entra
/// só pelo adaptador <see cref="HardwareLibre"/>.
/// </para>
/// </summary>
internal sealed class SensorColeta : IDisposable
{
    private readonly TimeSpan _intervalo;
    private readonly Computer _computer;
    private readonly Dictionary<string, string> _desligados = new();
    private readonly CancellationTokenSource _cts = new();
    private SensorSnapshot? _snapshot;
    private Thread? _worker;

    /// <summary>Hardware que falhou e foi desligado (com o motivo da falha).</summary>
    public IReadOnlyList<string> Falhas { get; private set; } = Array.Empty<string>();

    /// <summary>A última leva de leituras.</summary>
    public SensorSnapshot? UltimoSnapshot => Volatile.Read(ref _snapshot);

    public SensorColeta(bool permitirGpuAdl, int intervaloMs = 1000)
    {
        // O intervalo vem de fora porque o perfil de recursos do app muda a
        // periodicidade: no modo economia a leitura é 5 vezes mais rara.
        _intervalo = TimeSpan.FromMilliseconds(Math.Max(200, intervaloMs));
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
                // Mas o motivo vai para o snapshot — senão o app ficaria
                // mostrando as últimas leituras como se fossem de agora, sem
                // explicar por que a coleta parou.
                var anterior = Volatile.Read(ref _snapshot);
                Volatile.Write(ref _snapshot, new SensorSnapshot(
                    anterior?.Leituras ?? Array.Empty<SensorReading>(),
                    anterior?.Falhas ?? Array.Empty<string>(),
                    DateTime.Now,
                    ErroGeral: ex.GetType().Name + ": " + ex.Message));
            }

            try { _cts.Token.WaitHandle.WaitOne(_intervalo); }
            catch { break; }
        }
    }

    private void Coletar()
    {
        var hardwares = _computer.Hardware.Select(h => (HardwareColeta)new HardwareLibre(h));
        var volta = MontarVolta(hardwares, _desligados, _cts.Token);
        Falhas = volta.Falhas;
        Volatile.Write(ref _snapshot, new SensorSnapshot(volta.Leituras, volta.Falhas, DateTime.Now, FalhaCpu: volta.FalhaCpu));
    }

    /// <summary>
    /// Monta uma volta da coleta: percorre os hardwares, lê os sensores e
    /// anota as falhas.
    ///
    /// <para>
    /// O motivo da falha fica guardado em <paramref name="desligados"/>: o
    /// hardware que falhou não volta, e sem guardar o motivo o app perderia a
    /// explicação na volta seguinte (e voltaria a culpar o driver à toa).
    /// </para>
    /// </summary>
    internal static ColetaVolta MontarVolta(
        IEnumerable<HardwareColeta> hardwares,
        Dictionary<string, string> desligados,
        CancellationToken cancela)
    {
        var leituras = new List<SensorReading>();
        var falhas = new List<string>();
        string? falhaCpu = null;

        foreach (var hw in hardwares)
        {
            if (cancela.IsCancellationRequested) break;
            ColetarHardware(hw, leituras, falhas, desligados, cancela, ref falhaCpu);
        }

        return new ColetaVolta(leituras, falhas, falhaCpu);
    }

    /// <summary>
    /// Lê um hardware e os seus sub-hardwares. A falha de um sub-hardware não
    /// derruba o pai: só ele é desligado.
    /// </summary>
    private static void ColetarHardware(
        HardwareColeta hw,
        List<SensorReading> leituras,
        List<string> falhas,
        Dictionary<string, string> desligados,
        CancellationToken cancela,
        ref string? falhaCpu)
    {
        // Hardware desligado em outra volta: a falha continua valendo (é
        // por causa dela que ele foi desligado), então o motivo continua
        // visível em vez de sumir no silêncio da volta seguinte.
        if (desligados.TryGetValue(hw.Id, out string? motivo))
        {
            falhas.Add($"{hw.Nome}: {motivo}");
            if (hw.Tipo == HardwareType.Cpu) falhaCpu = motivo;
            return;
        }

        try
        {
            leituras.AddRange(hw.Ler());
        }
        catch (Exception ex)
        {
            // Um hardware problemático é desligado; os outros continuam.
            desligados[hw.Id] = ex.Message;
            falhas.Add($"{hw.Nome}: {ex.Message}");
            if (hw.Tipo == HardwareType.Cpu) falhaCpu = ex.Message;
            return;
        }

        foreach (var sub in hw.Subs)
        {
            if (cancela.IsCancellationRequested) return;
            ColetarHardware(sub, leituras, falhas, desligados, cancela, ref falhaCpu);
        }
    }

    /// <summary>
    /// Fecha a coleta: para o laço, espera a thread e fecha o hardware.
    /// </summary>
    public void Dispose()
    {
        try { _cts.Cancel(); } catch { }
        try { _worker?.Join(2000); } catch { }
        try { _computer.Close(); } catch { }
        _cts.Dispose();
    }
}
