using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using LibreHardwareMonitor.Hardware;

namespace TranalSentinel;

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

/// <summary>
/// Resultado de uma volta da coleta: as leituras e o que deu errado nela.
/// </summary>
/// <param name="Leituras">As leituras que deram certo.</param>
/// <param name="Falhas">Hw que não pôde ser aberto (um por hw). Não impede as outras leituras.</param>
/// <param name="Momento">Quando a volta foi feita.</param>
/// <param name="ErroGeral">
/// A coleta inteira parou (o processo filho caiu ou deu erro). Differe de
/// <paramref name="Falhas"/>: aqui não há leitura nova, e é este texto que
/// o app mostra como motivo quando a CPU não responde.
/// </param>
/// <param name="FalhaCpu">
/// A CPU em si não pôde ser lida (o driver recusou, o acesso foi negado).
/// É o motivo que interessa quando o app pergunta pela temperatura da CPU —
/// sem ele, o app culparia o driver sem saber de nada.
/// </param>
public sealed record SensorSnapshot(
    IReadOnlyList<SensorReading> Leituras,
    IReadOnlyList<string> Falhas,
    DateTime Momento,
    string? ErroGeral = null,
    string? FalhaCpu = null);

/// <summary>
/// Os sensores do TRANAL SENTINEL.
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
    private DateTime _ultimaGravacao = DateTime.MinValue;
    private DateTime _ultimoMomentoGravado = DateTime.MinValue;
    private AmostraSensor? _amostraPendente;

    /// <summary>
    /// O nome do hardware da GPU que o app acompanha (a placa dedicada).
    /// Vazio = a primeira GPU que aparecer nas leituras.
    /// </summary>
    public string? GpuPrincipal { get; set; }

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

    /// <summary>
    /// Erro que derrubou a coleta inteira (vindo do processo filho).
    /// Vazio quando a coleta funcionou — mesmo que algum hw específico tenha falhado.
    /// </summary>
    public string? ErroColeta { get; private set; }

    /// <summary>
    /// Motivo pelo qual a CPU não pôde ser lida (o driver recusou, o acesso
    /// foi negado). Vazio quando a CPU foi lida normalmente.
    /// </summary>
    public string? FalhaCpu { get; private set; }

    /// <summary>
    /// O motivo da falta do sensor da CPU, quando a coleta souber.
    ///
    /// <para>
    /// A regra é uma só, e a prioridade também: primeiro a falha que
    /// derrubou a coleta inteira, depois a falha da CPU em si. Sem nenhum dos
    /// dois, o app só pode chutar o motivo provável.
    /// </para>
    /// </summary>
    public string? MotivoCpuSensor => MotivoCpu(ErroColeta, FalhaCpu);

    /// <summary>
    /// Escolhe o motivo da falta do sensor da CPU: a falha geral da coleta
    /// vem primeiro (ela explica tudo), depois a falha da CPU em si.
    /// </summary>
    public static string? MotivoCpu(string? erroGeral, string? falhaCpu)
    {
        if (!string.IsNullOrWhiteSpace(erroGeral)) return erroGeral;
        if (!string.IsNullOrWhiteSpace(falhaCpu)) return falhaCpu;
        return null;
    }

    /// <summary>
    /// De quanto em quanto tempo o processo filho interroga o hardware.
    /// </summary>
    /// <remarks>
    /// Vem do perfil de recursos: no modo economia a leitura é bem mais rara
    /// (o custo é do LibreHardwareMonitor, não do .NET).
    /// </remarks>
    public int IntervaloColetaMs { get; set; } = 1000;

    /// <summary>O processo de coleta está rodando?</summary>
    public bool ProcessoVivo => _filho is { HasExited: false };

    /// <summary>Aconteceu quando o processo de coleta morreu (quantas mortes, se a GPU foi desligada).</summary>
    public event Action<int, bool>? ColetorMorreu;

    public SensorMonitor()
    {
        _pasta = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TranalSentinel", "sensores", Guid.NewGuid().ToString("N"));
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
    /// As linhas que a coleta entregou na leva atual.
    ///
    /// <para>
    /// A lista de todos os sensores que o app já viu (<see cref="Rows"/>)
    /// não serve para medir: um sensor que sai da coleta continua lá,
    /// com o valor antigo. Quem mede precisa saber o que veio <b>agora</b>
    /// — é a diferença entre "a ventoinha parou de responder" e "a
    /// ventoinha está a 3.400 RPM".
    /// </para>
    /// </summary>
    public IReadOnlyList<SensorRow> LinhasDaColeta { get; private set; } = Array.Empty<SensorRow>();

    /// <summary>
    /// Quando a coleta entregou a leva atual (null = não há leva em vigor).
    ///
    /// <para>
    /// A janela redesenha a cada segundo e a coleta nem sempre chegou
    /// nesse meio tempo: o instante da leva é o que diz se a leitura é
    /// nova ou se o app está olhando para o mesmo valor de novo.
    /// </para>
    /// </summary>
    public DateTime? MomentoDaColeta { get; private set; }

    /// <summary>
    /// A leva que a interface já pintou (para não refazer a lista a cada
    /// tique).
    /// </summary>
    private SensorSnapshot? _levaPintada;

    /// <summary>
    /// Passa a última coleta para a lista da interface. Só copia o que a
    /// thread de leitura já brought do disco — não lê arquivo nem faz JSON
    /// na thread da janela (isso engasgava o benchmark).
    /// </summary>
    public void Refresh()
    {
        var snap = Volatile.Read(ref _snapshot);
        if (snap is null)
        {
            // Sem leva não há sensor: o que ficou na lista é o valor velho.
            LinhasDaColeta = Array.Empty<SensorRow>();
            MomentoDaColeta = null;
            _levaPintada = null;
            return;
        }

        // A leva só é montada quando a coleta traz uma nova: a janela
        // redesenha a cada segundo, e refazer a lista a cada tique seria
        // desperdício.
        bool mudou = !ReferenceEquals(snap, _levaPintada);
        List<SensorRow>? linhas = mudou ? new List<SensorRow>(snap.Leituras.Count) : null;

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
            linhas?.Add(row);
        }

        if (mudou)
        {
            _levaPintada = snap;
            LinhasDaColeta = linhas!;
            MomentoDaColeta = snap.Momento;
        }

        PrepararAmostra(snap);
    }

    /// <summary>
    /// Monta a amostra do histórico (a cada 5 s, e só quando chega leitura
    /// nova) e deixa à espera para a thread leitora gravar.
    ///
    /// <para>
    /// A gravação em si é feita fora da thread da janela: escrever arquivo na
    /// thread da interface já chegou a engasgar o benchmark.
    /// </para>
    /// </summary>
    private void PrepararAmostra(SensorSnapshot snap)
    {
        try
        {
            // A mesma leitura pode ser refreshada várias vezes (a janela
            // atualiza a cada segundo, a coleta nem sempre chegou).
            if (snap.Momento == _ultimoMomentoGravado) return;
            if (DateTime.Now - _ultimaGravacao < SensorHistorico.IntervaloGravacao) return;

            _ultimoMomentoGravado = snap.Momento;
            _ultimaGravacao = DateTime.Now;

            Volatile.Write(ref _amostraPendente, new AmostraSensor(
                snap.Momento,
                CpuTemperature, CpuLoad, CpuPower, CpuClocks().Avg,
                GpuTemperature, GpuLoad, GpuClock, GpuVram,
                RamUso, DiscoLeitura, DiscoEscrita));
        }
        catch
        {
            // O log é um extra: se ele falhar, a leitura dos sensores continua.
        }
    }

    /// <summary>Grava a amostra que ficou esperando (a thread leitora chama).</summary>
    private void GravarAmostraPendente()
    {
        try
        {
            var amostra = Interlocked.Exchange(ref _amostraPendente, null);
            if (amostra is not null) SensorHistorico.Gravar(amostra);
        }
        catch
        {
            // O log é um extra: se ele falhar, a leitura dos sensores continua.
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

    /// <summary>Clock médio e máximo dos núcleos da CPU (MHz).</summary>
    public (float? Avg, float? Max) CpuClocks()
    {
        var cores = Rows.Where(r => r.HardwareType == HardwareType.Cpu && r.Type == SensorType.Clock &&
                                     r.Name.StartsWith("Core #") && !r.Name.Contains("Effective") && r.RawValue > 0)
                         .Select(r => r.RawValue!.Value).ToList();
        return cores.Count > 0 ? (cores.Average(), cores.Max()) : (null, null);
    }

    /// <summary>Temperatura da GPU em °C ("GPU Core" quando existe, senão a primeira da placa).</summary>
    public float? GpuTemperature
    {
        get
        {
            var temps = LinhasGpu().Where(r => r.Type == SensorType.Temperature && r.RawValue != null).ToList();
            var principal = temps.FirstOrDefault(r => r.Name == "GPU Core");
            return (principal ?? temps.FirstOrDefault())?.RawValue;
        }
    }

    /// <summary>Carga da GPU em %.</summary>
    public float? GpuLoad => LinhasGpu()
        .FirstOrDefault(r => r.Type == SensorType.Load && r.Name == "GPU Core")?.RawValue;

    /// <summary>Clock do núcleo da GPU em MHz.</summary>
    public float? GpuClock => LinhasGpu()
        .FirstOrDefault(r => r.Type == SensorType.Clock && r.Name == "GPU Core")?.RawValue;

    /// <summary>Memória de vídeo em uso, em MB.</summary>
    public float? GpuVram => LinhasGpu()
        .FirstOrDefault(r => r.Type == SensorType.SmallData && r.Name == "GPU Memory Used")?.RawValue;

    /// <summary>
    /// Memória RAM em uso, em %. Vem do próprio Windows (não do driver): é a
    /// mesma fonte que o overlay usa.
    /// </summary>
    public float? RamUso
    {
        get
        {
            var (total, disponivel) = Optimizer.MemoryStatus();
            if (total <= 0) return null;
            return (float)(100.0 * (total - disponivel) / total);
        }
    }

    /// <summary>Taxa de leitura dos discos, em MB/s (somando todos eles).</summary>
    public float? DiscoLeitura => TaxaDisco("Read");

    /// <summary>Taxa de escrita dos discos, em MB/s (somando todos eles).</summary>
    public float? DiscoEscrita => TaxaDisco("Write");

    /// <summary>
    /// As linhas da GPU que o app acompanha: a placa principal, quando o app
    /// sabe qual é (a dedicada vem primeiro na lista de placas).
    /// </summary>
    private List<SensorRow> LinhasGpu()
    {
        var todas = Rows.Where(r => r.HardwareType is HardwareType.GpuAmd or HardwareType.GpuNvidia).ToList();
        if (todas.Count == 0) return todas;

        if (!string.IsNullOrWhiteSpace(GpuPrincipal))
        {
            var principal = todas.Where(r => r.Hardware == GpuPrincipal).ToList();
            if (principal.Count > 0) return principal;
        }
        return todas;
    }

    /// <summary>
    /// Taxa de um dos sentidos do disco, em MB/s. Vem das leituras de
    /// armazenamento (o que a coleta já traz); sem elas, fica vazio.
    /// </summary>
    private float? TaxaDisco(string prefixo)
    {
        var valores = Rows
            .Where(r => r.HardwareType == HardwareType.Storage
                        && r.Type == SensorType.Throughput
                        && r.Name.StartsWith(prefixo, StringComparison.OrdinalIgnoreCase)
                        && r.RawValue != null)
            .Select(r => r.RawValue!.Value)
            .ToList();

        if (valores.Count == 0) return null;
        return (float)(valores.Sum() / 1048576.0);
    }

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
            "O TRANAL SENTINEL não está rodando como administrador, e o Windows só libera os sensores internos da CPU " +
            "nesse modo. Feche e abra de novo aceitando o pedido de administrador.",
        CpuSensorProblem.PawnIOMissing =>
            "Falta o driver PawnIO, que lê a temperatura e o consumo da CPU. Ele é gratuito, assinado e usado também " +
            "pelo LibreHardwareMonitor e pelo FanControl. Depois de instalar, feche e abra o TRANAL SENTINEL.",
        CpuSensorProblem.ReadFailed =>
            "O PawnIO está instalado, mas a leitura falhou. Se acabou de instalar, reinicie o PC. Se continuar, " +
            "confira se o antivírus não bloqueou o driver ou se a CPU é muito antiga/recente para a biblioteca de sensores.",
        _ => ""
    };

    /// <summary>
    /// O rótulo curto do aviso, por motivo.
    ///
    /// <para>
    /// É a única fonte desses textos: o menu, o painel e a janela de correção
    /// consomem daqui, em vez de cada um ter o seu <c>switch</c>.
    /// </para>
    /// </summary>
    public static string ResumoCpuSensor(CpuSensorProblem p) => p switch
    {
        CpuSensorProblem.NotElevated => "Sem permissão de administrador.",
        CpuSensorProblem.PawnIOMissing => "Falta o driver PawnIO.",
        CpuSensorProblem.ReadFailed => "O driver não respondeu.",
        _ => ""
    };

    /// <summary>
    /// O texto do aviso no menu — a regra em si: o estado entra por parâmetro.
    ///
    /// <para>
    /// Quando a coleta contou por que parou, esse motivo real vale mais que o
    /// genérico — é o que evita culpar o driver quando o defeito está em outro
    /// lugar. Só entra no lugar de "a leitura falhou": sem elevação ou sem
    /// driver, o motivo conhecido é mais exato que qualquer erro registrado.
    /// </para>
    /// </summary>
    public static string TextoAvisoCpuSensor(CpuSensorProblem p, string? motivo)
    {
        if (p == CpuSensorProblem.None) return "";
        if (p == CpuSensorProblem.ReadFailed && !string.IsNullOrWhiteSpace(motivo))
            return "Falha na coleta: " + motivo;
        return ResumoCpuSensor(p);
    }

    /// <summary>
    /// O texto da janela de correção: o motivo provável e, se houver, o erro real.
    /// </summary>
    public static string TextoExplicacaoCpuSensor(CpuSensorProblem p, string? motivo)
    {
        if (p == CpuSensorProblem.None) return "";
        string texto = Explain(p);
        if (p == CpuSensorProblem.ReadFailed && !string.IsNullOrWhiteSpace(motivo))
            texto += " A coleta registrou este erro: " + motivo + ".";
        return texto;
    }

    /// <summary>O aviso do menu, com o estado atual da coleta.</summary>
    public string AvisoCpuSensor() => TextoAvisoCpuSensor(DiagnoseCpuSensors(), MotivoCpuSensor);

    /// <summary>A explicação da janela de correção, com o estado atual da coleta.</summary>
    public string ExplicarCpuSensor() => TextoExplicacaoCpuSensor(DiagnoseCpuSensors(), MotivoCpuSensor);

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
        psi.ArgumentList.Add("--intervalo");
        psi.ArgumentList.Add(IntervaloColetaMs.ToString());

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
        catch { /* O coletor que morreu nao volta: o app continua sem sensores ate a proxima leitura. */ }
    }

    private void PararFilho()
    {
        var filho = _filho;
        _filho = null;
        if (filho is null) return;
        try { if (!filho.HasExited) filho.Kill(true); } catch { /* O processo ja morreu: nao ha o que encerrar. */ }
        try { filho.Dispose(); } catch { /* Liberar o handle falhou: o processo ja foi encerrado e o Windows recolhe o resto. */ }
    }

    private void IniciarLeitor()
    {
        var thread = new Thread(() =>
        {
            while (IsOpen)
            {
                try { LerArquivo(); }
                catch { /* O log sumiu ou trancou: a leitura espera a próxima volta. */ }
                try { GravarAmostraPendente(); }
                catch { /* O log de sensores é um extra: a leitura continua. */ }
                try { UsoAmostrador.Amostrar(DateTime.Now); }
                catch { /* O histórico de uso é um extra: a leitura continua. */ }
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
            var snap = JsonSerializer.Deserialize<SensorSnapshot>(json, SensorValor.OpcoesJson);
            if (snap is null) return;

            Volatile.Write(ref _snapshot, snap);
            UltimaColeta = snap.Momento;
            Falhas = snap.Falhas;
            ErroColeta = snap.ErroGeral;
            FalhaCpu = snap.FalhaCpu;
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
