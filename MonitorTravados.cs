using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace TranalSentinel;

/// <summary>Uma janela que está travada (não responde).</summary>
public sealed record JanelaTravada(int Pid, string Processo, string Titulo);

/// <summary>
/// Um programa que parou de responder, há quanto tempo.
/// </summary>
public sealed class AppTravado
{
    public int Pid { get; init; }
    public string Processo { get; init; } = "";
    public string Titulo { get; init; } = "";
    public int Segundos { get; init; }

    /// <summary>Texto para o aviso.</summary>
    public string Resumo => Segundos >= 60
        ? $"{Processo} não responde há {Segundos / 60} min"
        : $"{Processo} não responde há {Segundos} s";
}

/// <summary>
/// Vigia as janelas abertas e avisa quando um programa para de responder.
///
/// <para>
/// A pergunta "está travado?" é do Windows (a janela não responde a uma
/// mensagem). O app só mede <b>há quanto tempo</b> está travado e oferece
/// encerrar — quem decide é o usuário.
/// </para>
///
/// <para>
/// A lógica aqui é pura (recebe a lista de janelas travadas e um relógio),
/// o que a torna testável sem Windows. Quem conversa com o Windows é o
/// <see cref="JanelasWindows"/>.
/// </para>
/// </summary>
public sealed class MonitorTravados
{
    private readonly Func<IReadOnlyList<JanelaTravada>> _listar;
    private readonly Func<DateTime> _agora;

    /// <summary>
    /// Trava do estado. A verificação roda na thread do timer e a lista é
    /// lida na thread da interface (quando o usuário vai encerrar): sem a
    /// trava, uma enumeração no meio de uma escrita estoura
    /// "Collection was modified".
    /// </summary>
    private readonly object _tranco = new();
    private readonly Dictionary<int, (DateTime Desde, JanelaTravada Janela)> _travadoDesde = new();
    private readonly HashSet<int> _avisados = new();

    /// <summary>Disparado quando um programa trava (uma vez por travamento).</summary>
    public event Action<AppTravado>? Travou;

    /// <summary>Disparado quando o programa volta a responder.</summary>
    public event Action<AppTravado>? Respondeu;

    /// <summary>Quantos segundos o programa precisa estar travado para o app avisar.</summary>
    public int SegundosMinimos { get; set; } = 30;

    public MonitorTravados(Func<IReadOnlyList<JanelaTravada>> listar, Func<DateTime>? agora = null)
    {
        _listar = listar;
        _agora = agora ?? (() => DateTime.UtcNow);
    }

    /// <summary>
    /// Roda uma verificação. Avisa uma vez por programa travado (não fica
    /// repetindo o aviso enquanto ele continua travado) e avisa quando ele
    /// volta.
    /// </summary>
    public void Verificar()
    {
        IReadOnlyList<JanelaTravada> travadas;
        try
        {
            travadas = _listar() ?? Array.Empty<JanelaTravada>();
        }
        catch
        {
            // Falhar em listar janelas não pode derrubar o app.
            return;
        }

        var agora = _agora();
        var ids = new HashSet<int>();
        var paraAvisar = new List<AppTravado>();
        var paraAvisarResposta = new List<AppTravado>();

        lock (_tranco)
        {
            foreach (var janela in travadas)
            {
                ids.Add(janela.Pid);
                if (!_travadoDesde.ContainsKey(janela.Pid))
                    _travadoDesde[janela.Pid] = (agora, janela);

                if (_avisados.Contains(janela.Pid)) continue;
                int segundos = (int)(agora - _travadoDesde[janela.Pid].Desde).TotalSeconds;
                if (segundos < SegundosMinimos) continue;

                _avisados.Add(janela.Pid);
                paraAvisar.Add(new AppTravado
                {
                    Pid = janela.Pid,
                    Processo = janela.Processo,
                    Titulo = janela.Titulo,
                    Segundos = segundos
                });
            }

            // Quem voltou a responder sai da lista (e pode avisar de novo depois).
            foreach (var pid in _travadoDesde.Keys.ToList())
            {
                if (ids.Contains(pid)) continue;
                _travadoDesde.Remove(pid);
                if (!_avisados.Remove(pid)) continue;
                paraAvisarResposta.Add(new AppTravado { Pid = pid });
            }
        }

        // Os avisos saem FORA da trava. Quem recebe o aviso pode voltar
        // para a interface — e a interface pode pedir a lista de travados,
        // que usa a mesma trava. Disparar dentro dela travaria as duas.
        foreach (var app in paraAvisar) Travou?.Invoke(app);
        foreach (var app in paraAvisarResposta) Respondeu?.Invoke(app);
    }

    /// <summary>Programs que estão travados agora.</summary>
    public IReadOnlyList<AppTravado> TravadosAgora()
    {
        var agora = _agora();
        lock (_tranco)
        {
            return _travadoDesde
                .Select(p => new AppTravado
                {
                    Pid = p.Key,
                    Processo = p.Value.Janela.Processo,
                    Titulo = p.Value.Janela.Titulo,
                    Segundos = (int)(agora - p.Value.Desde).TotalSeconds
                })
                .OrderByDescending(a => a.Segundos)
                .ToList();
        }
    }

    /// <summary>
    /// Só os que o app já avisou (passaram do tempo mínimo). É esta lista que
    /// a bandeja oferece para encerrar: um programa travado há 3 s ainda não é
    /// um travamento, é uma janela lenta.
    /// </summary>
    public IReadOnlyList<AppTravado> TravadosAvisados()
    {
        var agora = _agora();
        lock (_tranco)
        {
            return _avisados
                .Where(pid => _travadoDesde.ContainsKey(pid))
                .Select(pid => new AppTravado
                {
                    Pid = pid,
                    Processo = _travadoDesde[pid].Janela.Processo,
                    Titulo = _travadoDesde[pid].Janela.Titulo,
                    Segundos = (int)(agora - _travadoDesde[pid].Desde).TotalSeconds
                })
                .OrderByDescending(a => a.Segundos)
                .ToList();
        }
    }
}

/// <summary>
/// As janelas do Windows, em P/Invoke direto.
///
/// <para>
/// A pergunta "esta janela responde?" é <c>SendMessageTimeout</c> com
/// <c>SMTO_ABORTIFHUNG</c>: se a janela não responde em 300 ms, ela está
/// travada. É assim que o próprio Windows responde ao "Não responder".
/// </para>
/// </summary>
public static class JanelasWindows
{
    private const uint WM_NULL = 0x0000;
    private const uint SMTO_ABORTIFHUNG = 0x0002;
    private const uint SMTO_BLOCK = 0x0001;

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder texto, int maximo);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(
        IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam,
        uint flags, uint timeout, out IntPtr resultado);

    /// <summary>
    /// Lista as janelas visíveis que estão travadas. Janelas sem título
    /// (janelas de ferramenta,-icons invisíveis) e o próprio app ficam de fora.
    /// </summary>
    public static IReadOnlyList<JanelaTravada> ListarTravadas()
    {
        var achadas = new List<JanelaTravada>();
        int meuPid = Environment.ProcessId;

        EnumWindows((hWnd, _) =>
        {
            try
            {
                if (!IsWindowVisible(hWnd)) return true;

                var titulo = new StringBuilder(512);
                GetWindowText(hWnd, titulo, titulo.Capacity);
                if (titulo.Length == 0) return true;   // janela sem título não interessa

                GetWindowThreadProcessId(hWnd, out uint pid);
                if (pid == 0 || (int)pid == meuPid) return true;

                if (!EstaTravada(hWnd)) return true;

                achadas.Add(new JanelaTravada((int)pid, NomeProcesso((int)pid), titulo.ToString()));
            }
            catch
            {
                // Uma janela exótica não pode derrubar a varredura.
            }
            return true;
        }, IntPtr.Zero);

        return achadas;
    }

    /// <summary>A janela não responde?</summary>
    public static bool EstaTravada(IntPtr hWnd)
    {
        try
        {
            var resultado = SendMessageTimeout(
                hWnd, WM_NULL, IntPtr.Zero, IntPtr.Zero,
                SMTO_ABORTIFHUNG | SMTO_BLOCK, 300, out _);
            return resultado == IntPtr.Zero;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Nome do processo (sem extensão).</summary>
    public static string NomeProcesso(int pid)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            return p.ProcessName;
        }
        catch
        {
            return "programa";
        }
    }
}
