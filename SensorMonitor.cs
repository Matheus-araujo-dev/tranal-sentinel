using System.Collections.ObjectModel;
using LibreHardwareMonitor.Hardware;

namespace ResetExplode;

/// <summary>Uma leitura de sensor como veio da coleta — sem nenhum vínculo com a interface.</summary>
public sealed record SensorReading(
    string Id,
    string Hardware,
    string Category,
    string Name,
    SensorType Type,
    HardwareType HardwareType,
    float? Value,
    float? Min,
    float? Max);

/// <summary>Resultado de uma volta da coleta: as leituras e o que deu errado nela.</summary>
public sealed record SensorSnapshot(IReadOnlyList<SensorReading> Leituras, IReadOnlyList<string> Falhas, DateTime Momento);

/// <summary>
/// Encapsula o LibreHardwareMonitorLib e mantém a lista de sensores atualizada.
///
/// <para>
/// <b>Por que a coleta está numa thread própria:</b> a biblioteca chama código
/// nativo do driver (ADL na GPU da AMD, PawnIO na CPU). Uma chamada travada
/// ali congela a janela inteira — e era o que acontecia com o timer de 1 s na
/// thread da UI. Aqui a coleta roda fora e a interface só copia o resultado.
/// </para>
///
/// <para>
/// <b>Por que cada hardware é atualizado dentro de um try/catch:</b> um
/// hardware que falha é desligado e os outros continuam. Sem isso, uma única
/// placa com problema derrubava a leitura inteira.
/// </para>
///
/// <para>
/// <b>Por que a GPU pode ficar de fora:</b> o LibreHardwareMonitor lê ponteiro
/// devolvido pela ADL; se o driver devolver lixo, o .NET leva um access
/// violation dentro de <c>Marshal.PtrToStructure</c> — que não dá para pegar
/// em C#, então o processo morre. O <see cref="SensorGuard"/> decide, a
/// partir dos crashes reais do app, se a GPU entra na coleta.
/// </para>
/// </summary>
public sealed class SensorMonitor : IDisposable
{
    /// <summary>De quanto em quanto tempo a coleta roda.</summary>
    private static readonly TimeSpan Intervalo = TimeSpan.FromSeconds(1);

    private readonly Dictionary<string, SensorRow> _index = new();
    private readonly List<string> _falhas = new();
    private readonly HashSet<string> _desligados = new();   // identificadores dos hardwares que falharam

    private Computer? _computer;
    private Thread? _worker;
    private CancellationTokenSource? _cts;
    private TaskCompletionSource<bool>? _primeiraColeta;
    private SensorSnapshot? _snapshot;
    private volatile bool _open;

    /// <summary>A GPU entra na coleta? Só pode mudar antes de abrir (ou reiniciando).</summary>
    public bool PermitirGpuAdl { get; set; } = true;

    /// <summary>Pausa a coleta (durante o benchmark de GPU, que não pode ter o driver contestado).</summary>
    public bool Pausado { get; set; }

    public ObservableCollection<SensorRow> Rows { get; } = new();

    public bool IsOpen => _open;

    /// <summary>Quando foi feita a última coleta (null se ainda não houve).</summary>
    public DateTime? UltimaColeta { get; private set; }

    /// <summary>Último erro da coleta (diagnóstico). Nunca derruba o app.</summary>
    public string? ErroColeta { get; private set; }

    /// <summary>Quantos hardwares o LibreHardwareMonitor detectou (diagnóstico).</summary>
    public int QuantidadeHardware => _computer?.Hardware.Count ?? 0;

    /// <summary>O computer chegou a ser criado/aberto (diagnóstico).</summary>
    public bool ComputerPronto => _computer is not null && _open;

    /// <summary>Erro na abertura do computer (diagnóstico).</summary>
    public string? ErroAbertura { get; private set; }

    /// <summary>Hardwares que falharam na última coleta (com o motivo).</summary>
    public IReadOnlyList<string> Falhas
    {
        get { lock (_falhas) return _falhas.ToArray(); }
    }

    /// <summary>
    /// Detecta o hardware e começa a coletar. Devolve quando a primeira leva
    /// chega (ou quando o tempo acaba — o app continua funcionando sem sensores).
    /// </summary>
    public async Task OpenAsync()
    {
        if (_open) return;

        Computer computer = CriarComputer();
        try
        {
            // ConfigureAwait(false): a continuação não pode ficar presa no Dispatcher do WPF.
            // Quem chama pode estar segurando a thread da interface (modo headless) — aí
            // a continuação nunca rodaria e a coleta nunca abriria.
            await Task.Run(() => computer.Open()).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            ErroAbertura = ex.ToString();
            throw;
        }
        _computer = computer;
        _open = true;

        _primeiraColeta = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        IniciarWorker();

        await Task.WhenAny(_primeiraColeta.Task, Task.Delay(TimeSpan.FromSeconds(25))).ConfigureAwait(false);
    }

    /// <summary>
    /// Passa a última coleta para a lista da interface. Não chama código
    /// nativo: só copia o snapshot — precisa rodar na thread da janela
    /// (a lista é observável e está ligada ao grid).
    /// </summary>
    public void Refresh()
    {
        var snap = Volatile.Read(ref _snapshot);
        if (snap is null) return;

        foreach (var leitura in snap.Leituras)
        {
            if (!_index.TryGetValue(leitura.Id, out var row))
            {
                row = new SensorRow
                {
                    Id = leitura.Id,
                    Hardware = leitura.Hardware,
                    Category = leitura.Category,
                    Name = leitura.Name,
                    Type = leitura.Type,
                    HardwareType = leitura.HardwareType
                };
                _index[leitura.Id] = row;
                Rows.Add(row);
            }
            row.Update(leitura.Value, leitura.Min, leitura.Max);
        }
    }

    /// <summary>
    /// Reabre a coleta com outra configuração (por exemplo: ligando de novo os
    /// sensores da GPU). Precisa ser chamado na thread da janela.
    /// </summary>
    public async Task ReiniciarAsync(bool permitirGpuAdl)
    {
        PararWorker();
        FecharComputer();

        _index.Clear();
        Rows.Clear();
        lock (_falhas) _falhas.Clear();
        _desligados.Clear();

        PermitirGpuAdl = permitirGpuAdl;
        _open = false;
        Volatile.Write(ref _snapshot, null);

        await OpenAsync();
        Refresh();
    }

    /// <summary>Temperatura principal da CPU: Package (Intel) / Tctl-Tdie (AMD), senão a maior entre os núcleos.</summary>
    public float? CpuTemperature
    {
        get
        {
            var temps = Rows.Where(r => r.HardwareType == HardwareType.Cpu
                                     && r.Type == SensorType.Temperature
                                     && r.RawValue is > 0).ToList();
            var main = temps.FirstOrDefault(r => r.Name.Contains("Package") || r.Name.Contains("Tctl") || r.Name.Contains("Tdie"));
            return main?.RawValue ?? temps.Max(r => r.RawValue);
        }
    }

    /// <summary>Carga total da CPU em %.</summary>
    public float? CpuLoad => Rows.FirstOrDefault(r => r.HardwareType == HardwareType.Cpu
                                                   && r.Type == SensorType.Load
                                                   && r.Name == "CPU Total")?.RawValue;

    /// <summary>Consumo do pacote da CPU em W.</summary>
    public float? CpuPower => Rows.FirstOrDefault(r => r.HardwareType == HardwareType.Cpu
                                                    && r.Type == SensorType.Power
                                                    && r.Name.Contains("Package"))?.RawValue;

    /// <summary>
    /// Driver PawnIO (necessário para temperatura/consumo da CPU via MSR/SMN).
    /// A biblioteca procura exatamente esta chave de desinstalação.
    /// </summary>
    public static bool IsPawnIOInstalled
    {
        get
        {
            using var hklm = Microsoft.Win32.RegistryKey.OpenBaseKey(Microsoft.Win32.RegistryHive.LocalMachine,
                                                                     Microsoft.Win32.RegistryView.Registry64);
            using var key = hklm.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO");
            return key != null;
        }
    }

    public static bool IsElevated =>
        new System.Security.Principal.WindowsPrincipal(System.Security.Principal.WindowsIdentity.GetCurrent())
            .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);

    public enum CpuSensorProblem { None, NotElevated, PawnIOMissing, ReadFailed }

    /// <summary>Por que a temperatura/consumo da CPU não aparecem (o motivo mais provável primeiro).</summary>
    public CpuSensorProblem DiagnoseCpuSensors()
    {
        if (CpuTemperature != null) return CpuSensorProblem.None;
        if (!IsElevated) return CpuSensorProblem.NotElevated;
        if (!IsPawnIOInstalled) return CpuSensorProblem.PawnIOMissing;
        return CpuSensorProblem.ReadFailed;
    }

    public static string Explain(CpuSensorProblem p) => p switch
    {
        CpuSensorProblem.NotElevated =>
            "O RESET EXPLODE não está rodando como administrador, e o Windows só libera os sensores internos da CPU " +
            "nesse modo. Feche e abra de novo aceitando o pedido de administrador.",
        CpuSensorProblem.PawnIOMissing =>
            "Falta o driver PawnIO, que lê a temperatura e o consumo da CPU. Ele é gratuito, assinado e usado também " +
            "pelo LibreHardwareMonitor e pelo FanControl. Depois de instalar, feche e abra o RESET EXPLODE.",
        CpuSensorProblem.ReadFailed =>
            "O PawnIO está instalado, mas a leitura falhou. Se acabou de instalar, reinicie o PC. Se continuar, " +
            "confira se o antivírus não bloqueou o driver ou se a CPU é muito antiga/recente para a biblioteca de sensores.",
        _ => ""
    };

    // ---------------- coleta ----------------

    private Computer CriarComputer() => new()
    {
        IsCpuEnabled = true,
        IsGpuEnabled = PermitirGpuAdl,
        IsMemoryEnabled = true,
        IsMotherboardEnabled = true,
        IsStorageEnabled = true,
        IsNetworkEnabled = true,
        IsControllerEnabled = true
    };

    private void IniciarWorker()
    {
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        _worker = new Thread(() => Loop(token))
        {
            IsBackground = true,
            Name = "Sensores"
        };
        _worker.Start();
    }

    private void Loop(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                if (!Pausado) Coletar(token);
            }
            catch (Exception ex)
            {
                // O worker nunca morre: uma falha na coleta não pode derrubar o app.
                ErroColeta = ex.ToString();
            }

            try { token.WaitHandle.WaitOne(Intervalo); }
            catch { break; }
        }
    }

    private void Coletar(CancellationToken token)
    {
        var computer = _computer;
        if (computer is null) return;

        var leituras = new List<SensorReading>();
        var falhas = new List<string>();

        foreach (var hw in computer.Hardware)
        {
            if (token.IsCancellationRequested) return;
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

        lock (_falhas)
        {
            _falhas.Clear();
            _falhas.AddRange(falhas);
        }

        UltimaColeta = DateTime.Now;
        Volatile.Write(ref _snapshot, new SensorSnapshot(leituras, falhas, UltimaColeta.Value));
        _primeiraColeta?.TrySetResult(true);
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

    private void PararWorker()
    {
        var cts = _cts;
        var worker = _worker;
        _cts = null;
        _worker = null;
        if (cts is null) return;

        cts.Cancel();
        try { worker?.Join(3000); }
        catch { }
        cts.Dispose();
    }

    private void FecharComputer()
    {
        var computer = _computer;
        _computer = null;
        _open = false;
        if (computer is null) return;
        try { computer.Close(); }
        catch { }
    }

    public void Dispose()
    {
        PararWorker();
        FecharComputer();
    }
}
