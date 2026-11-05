using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
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
/// Os sensores do RESET EXPLODE.
///
/// <para>
/// A coleta roda num <b>processo separado</b> (o mesmo executável, chamado com
/// <c>--sensores-worker</c>). Isso é deliberado: o LibreHardwareMonitor chama
/// código nativo do driver — a ADL da AMD, no caso da GPU — e, quando esse
/// driver devolve ponteiro inválido, o .NET leva um access violation dentro de
/// <c>Marshal.PtrToStructure</c>. Access violation não dá para pegar em C#: o
/// processo inteiro morre. Num processo isolado, morre só o filho, que aqui é
/// reiniciado automaticamente.
/// </para>
///
/// <para>
/// Quando o filho morre, o app conta a morte e o reinicia <b>sem a GPU</b>:
/// é o caminho nativo que derruba as coisas, e o resto dos sensores continua
/// funcionando. O usuário pode religar a GPU à mão pelo aviso na lateral.
/// </para>
///
/// <para>
/// Cada hardware que falha dentro do filho é desligado e os outros continuam —
/// um try/catch por hardware protege de falha comum; o isolamento protege do
/// crash que não dá para pegar.
/// </para>
/// </summary>
public sealed class SensorMonitor : IDisposable
{
    private readonly Dictionary<string, SensorRow> _index = new();
    private readonly string _pasta;
    private readonly string _arquivoLeituras;
    private readonly string _arquivoPausa;

    private Process? _filho;
    private SensorSnapshot? _snapshot;
    private bool _permitirGpuAdl = true;
    private bool _pausado;
    private DateTime _ultimaLeituraArquivo = DateTime.MinValue;

    /// <summary>A GPU entra na coleta? (É o caminho nativo do driver da AMD.)</summary>
    public bool PermitirGpuAdl
    {
        get => _permitirGpuAdl;
        set { if (!IsOpen) _permitirGpuAdl = value; }
    }

    /// <summary>Pausa a coleta (durante o benchmark de GPU, que não pode ter o driver contestado).</summary>
    public bool Pausado
    {
        get => _pausado;
        set
        {
            if (_pausado == value) return;
            _pausado = value;
            try
            {
                // O filho olha esse arquivo antes de cada coleta.
                if (value) File.WriteAllText(_arquivoPausa, "1");
                else if (File.Exists(_arquivoPausa)) File.Delete(_arquivoPausa);
            }
            catch { }
        }
    }

    public ObservableCollection<SensorRow> Rows { get; } = new();

    public bool IsOpen { get; private set; }

    /// <summary>Quando foi feita a última coleta (null se ainda não houve).</summary>
    public DateTime? UltimaColeta { get; private set; }

    /// <summary>Quantas vezes o processo de coleta morreu (e foi reiniciado).</summary>
    public int MortesProcesso { get; private set; }

    /// <summary>Hardwares que falharam na última coleta (com o motivo).</summary>
    public IReadOnlyList<string> Falhas { get; private set; } = Array.Empty<string>();

    /// <summary>Erro na coleta (vindo do processo filho).</summary>
    public string? ErroColeta { get; private set; }

    /// <summary>O processo de coleta está rodando?</summary>
    public bool ProcessoVivo => _filho is { HasExited: false };

    /// <summary>Aconteceu quando o processo de coleta morreu (quantas mortes, se a GPU foi desligada).</summary>
    public event Action<int, bool>? ColetorMorreu;

    public SensorMonitor()
    {
        _pasta = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ResetExplode", "sensores", Guid.NewGuid().ToString("N"));
        _arquivoLeituras = Path.Combine(_pasta, "leituras.json");
        _arquivoPausa = Path.Combine(_pasta, "pausa.flag");
        try { Directory.CreateDirectory(_pasta); } catch { }
    }

    /// <summary>
    /// Sobe o processo de coleta e espera a primeira leva chegar
    /// (ou o tempo acabar — o app continua funcionando sem sensores).
    /// </summary>
    public async Task OpenAsync()
    {
        if (IsOpen) return;
        IsOpen = true;

        await Task.Run(() => IniciarFilho(_permitirGpuAdl)).ConfigureAwait(false);
        IniciarLeitor();

        DateTime limite = DateTime.Now.AddSeconds(30);
        while (DateTime.Now < limite && UltimaColeta is null)
        {
            // O arquivo é lido aqui também: quem espera a primeira leva não pode
            // depender de alguém chamar Refresh() (no modo headless, ninguém chama).
            LerArquivo();
            if (UltimaColeta is not null) break;
            await Task.Delay(200).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Passa a última coleta para a lista da interface. Só copia o que a
    /// thread de leitura já brought do disco — não lê arquivo nem faz JSON
    /// na thread da janela (isso engasgava o benchmark).
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
    /// Religa (ou desliga) a GPU na coleta, reiniciando o processo filho.
    /// Precisa ser chamado na thread da janela.
    /// </summary>
    public async Task ReiniciarAsync(bool permitirGpuAdl)
    {
        _permitirGpuAdl = permitirGpuAdl;
        _index.Clear();
        Rows.Clear();
        Volatile.Write(ref _snapshot, null);
        UltimaColeta = null;

        PararFilho();
        await Task.Run(() => IniciarFilho(permitirGpuAdl)).ConfigureAwait(false);

        DateTime limite = DateTime.Now.AddSeconds(30);
        while (DateTime.Now < limite && UltimaColeta is null)
            await Task.Delay(200).ConfigureAwait(false);
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

    // ---------------- processo filho ----------------

    private void IniciarFilho(bool usarGpu)
    {
        string exe = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName ?? "";
        if (exe.Length == 0) return;

        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true
        };
        psi.ArgumentList.Add(SensorWorker.Argumento);
        psi.ArgumentList.Add(_arquivoLeituras);
        psi.ArgumentList.Add(_arquivoPausa);
        psi.ArgumentList.Add(Environment.ProcessId.ToString());
        if (!usarGpu) psi.ArgumentList.Add("--sem-gpu");

        var filho = Process.Start(psi);
        if (filho is null) return;
        _filho = filho;

        // O filho é quem detecta a morte dele: avisa e o app reinicia.
        filho.EnableRaisingEvents = true;
        filho.Exited += (_, _) => ColetorMorreuInterno();
    }

    private void ColetorMorreuInterno()
    {
        if (!IsOpen) return;   // encerramento normal

        MortesProcesso++;
        bool semGpu = SensorGuard.GpuDepoisDaMorte(MortesProcesso);
        _permitirGpuAdl = !semGpu;

        ColetorMorreu?.Invoke(MortesProcesso, semGpu);

        try { IniciarFilho(!semGpu); }
        catch { }
    }

    private void PararFilho()
    {
        var filho = _filho;
        _filho = null;
        if (filho is null) return;
        try { if (!filho.HasExited) filho.Kill(true); } catch { }
        try { filho.Dispose(); } catch { }
    }

    private void IniciarLeitor()
    {
        var thread = new Thread(() =>
        {
            while (IsOpen)
            {
                try { LerArquivo(); }
                catch { }
                try { Thread.Sleep(500); }
                catch { break; }
            }
        })
        { IsBackground = true, Name = "Leitor de sensores" };
        thread.Start();
    }

    private void LerArquivo()
    {
        try
        {
            var info = new FileInfo(_arquivoLeituras);
            if (!info.Exists) return;
            if (info.LastWriteTimeUtc == _ultimaLeituraArquivo) return;
            _ultimaLeituraArquivo = info.LastWriteTimeUtc;

            string json = File.ReadAllText(_arquivoLeituras);
            var snap = JsonSerializer.Deserialize<SensorSnapshot>(json);
            if (snap is null) return;

            Volatile.Write(ref _snapshot, snap);
            UltimaColeta = snap.Momento;
            Falhas = snap.Falhas;
            ErroColeta = snap.Falhas.Count > 0 ? snap.Falhas[0] : null;
        }
        catch
        {
            // Arquivo sendo escrito: tenta no próximo ciclo.
        }
    }

    /// <summary>
    /// Mata o processo de coleta de propósito (teste). O app deve perceber a
    /// morte e reiniciá-lo sozinho — é o mesmo caminho de um access violation.
    /// </summary>
    public bool MatarColetorParaTeste()
    {
        var filho = _filho;
        if (filho is null) return false;
        try { filho.Kill(true); return true; }
        catch { return false; }
    }

    public void Dispose()
    {
        IsOpen = false;
        PararFilho();
        try { if (Directory.Exists(_pasta)) Directory.Delete(_pasta, true); } catch { }
    }
}
