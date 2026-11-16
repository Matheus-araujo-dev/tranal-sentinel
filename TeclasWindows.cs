using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace TranalSentinel;

/// <summary>
/// O teclado visto pelo Windows: o estado das teclas modificadoras, a
/// repetição automática e o "soltar" (key-up) injetado.
///
/// <para>
/// É a única parte que conversa com o Windows nesta história. A regra
/// (<see cref="TeclaPresaRegras"/>) é pura e a vigia
/// (<see cref="MonitorTeclaPresa"/>) só orquestra.
/// </para>
/// </summary>
public static class TeclasWindows
{
    /// <summary>Flag do key-up na injeção de tecla (KEYEVENTF_KEYUP).</summary>
    private const uint KeyUp = 0x0002;

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll")]
    private static extern short GetKeyState(int vKey);

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);

    /// <summary>
    /// O Windows diz que a tecla está pressionada agora?
    /// </summary>
    /// <remarks>
    /// <c>GetAsyncKeyState</c> é o estado físico do teclado (o que o Windows
    /// acha que está pressionado agora); o <c>GetKeyState</c> é o estado da
    /// fila de entrada da thread. Nos dois, uma tecla presa aparece como
    /// pressionada — foi assim que a tecla grudada foi medida no PC do dono.
    /// </remarks>
    public static bool EstaPressionada(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

    /// <summary>
    /// O estado da fila de entrada da thread (a outra metade da medição).
    /// </summary>
    public static bool EstaNaFila(int vk) => (GetKeyState(vk) & 0x8000) != 0;

    /// <summary>
    /// As modificadoras que o Windows diz que estão pressionadas agora.
    /// </summary>
    public static IReadOnlyList<Modificadora> ModificadorasPressionadas()
    {
        var lista = new List<Modificadora>();
        foreach (var modificadora in TeclaPresaRegras.Modificadoras)
        {
            if (EstaPressionada(modificadora.Vk)) lista.Add(modificadora);
        }
        return lista;
    }

    /// <summary>
    /// Injeta o "soltar" de uma tecla (key-up).
    ///
    /// <para>
    /// É exatamente o que o Windows faz quando o dedo levanta: sem isso, o
    /// estado interno continua marcando a tecla como pressionada (e é por
    /// isso que o clique do owner virava clique com Shift).
    /// </para>
    /// </summary>
    public static void Soltar(int vk) => keybd_event((byte)vk, 0, KeyUp, UIntPtr.Zero);

    /// <summary>
    /// Injeta o "soltar" de várias teclas de uma vez.
    /// </summary>
    public static void SoltarTodas(IEnumerable<int> vks)
    {
        foreach (var vk in vks) Soltar(vk);
    }

    /// <summary>
    /// A repetição automática do teclado está ligada?
    ///
    /// <para>
    /// A repetição automática é o que distingue "alguém está segurando a
    /// tecla" de "a tecla ficou presa": quem segura continua mandando
    /// repetição, quem ficou preso não manda nada. Se o dono desligou a
    /// repetição do Windows, o silêncio não prova nada — e o app se cala
    /// (solte a tecla que a pessoa está segurando é pior do que a tecla
    /// presa).
    /// </para>
    ///
    /// <para>
    /// A configuração vive em <c>HKCU\Control Panel\Keyboard\Keyboard Speed</c>
    /// (0 = desligada, 1 = ligada). Se não der para ler, o app assume que está
    /// desligada e não age — do mesmo jeito que o número que não se lê não
    /// vira zero.
    /// </para>
    /// </summary>
    public static bool RepeticaoDoTecladoLigada()
    {
        try
        {
            using var chave = Registry.CurrentUser.OpenSubKey(@"Control Panel\Keyboard");
            if (chave?.GetValue("Keyboard Speed") is int valor) return valor != 0;
            return false;
        }
        catch
        {
            // Sem conseguir ler a configuração, o app não age: quem não sabe
            // não afirma.
            return false;
        }
    }
}

/// <summary>
/// O gancho de teclado que sabe quando o dono mexeu no teclado.
///
/// <para>
/// É um gancho de baixo nível (<c>WH_KEYBOARD_LL</c>): o Windows avisa
/// este processo de <b>todo</b> teclado que existe na sessão, antes de
/// qualquer janela. Só o que importa é o instante do último evento — o app
/// guarda o horário e não guarda o que foi digitado (nada disso vira log).
/// </para>
///
/// <para>
/// Medido no PC do dono em 16/11/2026: uma tecla injetada e nunca solta
/// (que é o estado de uma tecla presa) <b>não</b> gera nenhum evento em
/// 10 s de gancho — e injetar o key-up limpa o estado. É essa diferença que
/// permite pegar a tecla presa.
/// </para>
/// </summary>
public sealed class VigiaTeclado : IDisposable
{
    /// <summary>WH_KEYBOARD_LL.</summary>
    private const int TecladoDeBaixoNivel = 13;

    private delegate IntPtr ProcedimentoDoGancho(int codigo, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int id, ProcedimentoDoGancho procedimento, IntPtr modulo, uint idThread);

    [DllImport("user32.dll")]
    private static extern bool UnhookWindowsHookEx(IntPtr gancho);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr gancho, int codigo, IntPtr wParam, IntPtr lParam);

    // O modulo do proprio processo: IntPtr.Zero e o jeito de pedir "o
    // modulo de quem chamou" (sem string nula, que e aviso do compilador).
    [DllImport("kernel32.dll")]
    private static extern IntPtr GetModuleHandle(IntPtr nomeDoModulo);

    /// <summary>O delegate precisa sobreviver: sem ele, o Windows descarta o gancho.</summary>
    private ProcedimentoDoGancho? _procedimento;

    private IntPtr _gancho = IntPtr.Zero;

    /// <summary>Instante do último evento de teclado (em ticks de <see cref="DateTime"/>).</summary>
    private long _ultimaAtividade;

    /// <summary>O gancho está instalado?</summary>
    public bool Instalado => _gancho != IntPtr.Zero;

    /// <summary>
    /// Quando foi o último evento de teclado. Null quando o gancho não está
    /// instalado (e, portanto, o silêncio não é prova de nada).
    /// </summary>
    public DateTime? UltimaAtividade
    {
        get
        {
            if (!Instalado) return null;
            return new DateTime(Interlocked.Read(ref _ultimaAtividade), DateTimeKind.Utc);
        }
    }

    /// <summary>
    /// Há quanto tempo o teclado não manda evento nenhum.
    /// Null quando o gancho não está instalado — e, sem gancho, o
    /// silêncio não é prova de nada.
    /// </summary>
    public TimeSpan? Silencio
    {
        get
        {
            DateTime? ultima = UltimaAtividade;
            if (!ultima.HasValue) return null;
            TimeSpan silencio = DateTime.UtcNow - ultima.Value;
            return silencio < TimeSpan.Zero ? TimeSpan.Zero : silencio;
        }
    }

    /// <summary>
    /// Instala o gancho. Precisa ser chamado numa thread que bombeia
    /// mensagens — a thread da interface do WPF sempre bombeia.
    /// </summary>
    /// <returns>true se o Windows aceitou o gancho.</returns>
    public bool Instalar()
    {
        if (Instalado) return true;
        try
        {
            _procedimento = Procedimento;
            _gancho = SetWindowsHookEx(TecladoDeBaixoNivel, _procedimento, GetModuleHandle(IntPtr.Zero), 0);
            if (_gancho == IntPtr.Zero) return false;

            // O silêncio começa a contar agora: sem evento nenhum desde que o
            // gancho entrou, o app não sabe se o teclado está em silêncio.
            Interlocked.Exchange(ref _ultimaAtividade, DateTime.UtcNow.Ticks);
            return true;
        }
        catch
        {
            // Gancho que não instala só significa que o app não vigia a tecla
            // presa — o resto do app segue igual.
            _gancho = IntPtr.Zero;
            return false;
        }
    }

    /// <summary>Desinstala o gancho.</summary>
    public void Remover()
    {
        try
        {
            if (_gancho != IntPtr.Zero) UnhookWindowsHookEx(_gancho);
        }
        catch
        {
            // Gancho que não sai não derruba o app na hora de fechar.
        }
        _gancho = IntPtr.Zero;
    }

    /// <summary>Fecha o gancho (o <see cref="IDisposable"/>).</summary>
    public void Dispose() => Remover();

    /// <summary>
    /// O callback do gancho: só registra o instante e passa a chave adiante.
    /// Ele roda na thread que instalou o gancho, e o Windows descarta o
    /// gancho se o callback demorar — por isso ele não faz nada além de
    /// anotar a hora.
    /// </summary>
    private IntPtr Procedimento(int codigo, IntPtr wParam, IntPtr lParam)
    {
        if (codigo >= 0)
        {
            Interlocked.Exchange(ref _ultimaAtividade, DateTime.UtcNow.Ticks);
        }
        return CallNextHookEx(_gancho, codigo, wParam, lParam);
    }
}
