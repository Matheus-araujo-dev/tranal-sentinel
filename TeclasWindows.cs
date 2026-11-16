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

    /// <summary>
    /// Flag da tecla estendida (KEYEVENTF_EXTENDEDKEY): o Windows precisa
    /// dela para saber que é a tecla da direita.
    /// </summary>
    private const uint TeclaEstendida = 0x0001;

    /// <summary>
    /// As teclas que vivem na parte estendida do teclado: o Ctrl e o Alt
    /// direitos e as duas do Windows. Sem a flag, o "soltar" injetado seria
    /// entendido como o da esquerda — e a tecla da direita continuaria
    /// marcada no Windows (que foi exatamente o caso medido no PC do dono:
    /// o Shift direito).
    /// </summary>
    private static readonly HashSet<int> TeclasEstendidas = new() { 0xA3, 0xA5, 0x5B, 0x5C };

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
    public static void Soltar(int vk)
    {
        uint flags = KeyUp;
        if (TeclasEstendidas.Contains(vk)) flags |= TeclaEstendida;
        keybd_event((byte)vk, 0, flags, UIntPtr.Zero);
    }

    /// <summary>
    /// Injeta o "soltar" de várias teclas de uma vez.
    /// </summary>
    public static void SoltarTodas(IEnumerable<int> vks)
    {
        foreach (var vk in vks) Soltar(vk);
    }

    /// <summary>
    /// A velocidade da repetição que o Windows guardou (0 = desligada).
    /// Null = não há valor guardado (vale o padrão do Windows).
    /// </summary>
    /// <remarks>
    /// O valor vive em <c>Control Panel\Keyboard</c> ("Keyboard Speed", que
    /// é a velocidade da repetição; 0 = desligada). Ele pode estar no
    /// perfil do usuário ou no perfil padrão do Windows — e, quando não está em
    /// nenhum dos dois, vale o padrão, que é a repetição ligada.
    /// </remarks>
    private static int? VelocidadeDaRepeticao()
    {
        // O perfil do usuário primeiro; quando ele não tem o valor, o perfil
        // padrão do Windows é quem manda (medido no PC do dono em
        // 16/11/2026: o valor estava só lá).
        RegistryKey? usuario = null;
        try { usuario = Registry.CurrentUser.OpenSubKey(@"Control Panel\Keyboard"); }
        catch { }
        int? velocidade = VelocidadeNaChave(usuario);
        usuario?.Dispose();
        if (velocidade.HasValue) return velocidade;

        RegistryKey? padrao = null;
        try { padrao = Registry.Users.OpenSubKey(@".DEFAULT\Control Panel\Keyboard"); }
        catch { }
        velocidade = VelocidadeNaChave(padrao);
        padrao?.Dispose();
        return velocidade;
    }

    /// <summary>
    /// O valor da velocidade da repetição dentro de uma chave (null quando
    /// a chave não tem o valor).
    /// </summary>
    private static int? VelocidadeNaChave(RegistryKey? chave)
    {
        if (chave is null) return null;
        try
        {
            foreach (string nome in new[] { "Keyboard Speed", "KeyboardSpeed" })
            {
                object? bruto = chave.GetValue(nome);
                if (bruto is int numero) return numero;
                if (bruto is string texto && int.TryParse(texto, out int convertido)) return convertido;
            }
        }
        catch
        {
            // Chave sem permissão ou valor de outro tipo: sem valor, e vale o
            // padrão do Windows.
        }
        return null;
    }

    /// <summary>
    /// A repetição automática do teclado está ligada?
    /// </summary>
    public static bool RepeticaoDoTecladoLigada() =>
        TeclaPresaRegras.RepeticaoDoTecladoLigada(VelocidadeDaRepeticao());
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
///
/// <para>
/// <b>O gancho mora numa thread só dele</b>, com a sua bomba de mensagens.
/// O Windows remove um gancho de baixo nível em silêncio quando a thread que
/// o instalou demora demais para responder — e a thread da interface do WPF
/// trava (a análise do TRANAL SAÚDE, por exemplo, lê o log de eventos
/// inteiro). Com o gancho na interface, ele morria sozinho e o silêncio
/// congelado fazia o app soltar a tecla que o dono estava segurando.
/// </para>
/// </summary>
public sealed class VigiaTeclado : IDisposable
{
    /// <summary>WH_KEYBOARD_LL.</summary>
    private const int TecladoDeBaixoNivel = 13;

    /// <summary>WM_QUIT: o pedido de fim da bomba de mensagens.</summary>
    private const uint WmQuit = 0x0012;

    [StructLayout(LayoutKind.Sequential)]
    private struct Msg
    {
        public IntPtr Hwnd;
        public uint Message;
        public IntPtr WParam;
        public IntPtr LParam;
        public uint Time;
        public int X;
        public int Y;
    }

    private delegate IntPtr ProcedimentoDoGancho(int codigo, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int id, ProcedimentoDoGancho procedimento, IntPtr modulo, uint idThread);

    [DllImport("user32.dll")]
    private static extern bool UnhookWindowsHookEx(IntPtr gancho);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr gancho, int codigo, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern int GetMessage(out Msg msg, IntPtr hwnd, uint min, uint max);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref Msg msg);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessage(ref Msg msg);

    [DllImport("user32.dll")]
    private static extern bool PostThreadMessage(uint idThread, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetModuleHandle(IntPtr nomeDoModulo);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    /// <summary>O delegate precisa sobreviver: sem ele, o Windows descarta o gancho.</summary>
    private ProcedimentoDoGancho? _procedimento;

    private IntPtr _gancho = IntPtr.Zero;
    private Thread? _thread;
    private uint _idDaThread;
    private bool _instalado;

    /// <summary>
    /// Instante do último evento de teclado, em milissegundos do relógio
    /// monotônico do Windows. O relógio de parede não serve aqui: ele salta
    /// quando a hora é corrigida ou o fuso muda, e um salto faria o app
    /// achar que o teclado está em silêncio há mais tempo do que está.
    /// </summary>
    private long _ultimaAtividade;

    /// <summary>Aguarda a thread instalar o gancho (ou desistir).</summary>
    private readonly ManualResetEventSlim _pronto = new(false);

    /// <summary>O gancho está instalado?</summary>
    public bool Instalado => _instalado && _gancho != IntPtr.Zero;

    /// <summary>
    /// Há quanto tempo o teclado não manda evento nenhum.
    /// Null quando o gancho não está instalado — e, sem gancho, o
    /// silêncio não é prova de nada.
    /// </summary>
    public TimeSpan? Silencio
    {
        get
        {
            if (!Instalado) return null;
            long decorrido = Environment.TickCount64 - Interlocked.Read(ref _ultimaAtividade);
            return TimeSpan.FromMilliseconds(decorrido < 0 ? 0 : decorrido);
        }
    }

    /// <summary>
    /// Quando foi o último evento de teclado. Null quando o gancho não está
    /// instalado (e, portanto, o silêncio não é prova de nada).
    /// </summary>
    public DateTime? UltimaAtividade
    {
        get
        {
            TimeSpan? silencio = Silencio;
            if (!silencio.HasValue) return null;
            return DateTime.UtcNow - silencio.Value;
        }
    }

    /// <summary>
    /// Instala o gancho numa thread própria, com a sua bomba de mensagens.
    /// </summary>
    /// <returns>true se o Windows aceitou o gancho.</returns>
    public bool Instalar()
    {
        if (Instalado) return true;

        _pronto.Reset();
        _thread = new Thread(Rodar)
        {
            IsBackground = true,
            Name = "vigia-teclado"
        };
        _thread.Start();

        // A thread instala o gancho e avisa; se ela não conseguir (ou não
        // conseguir a tempo), a vigia simplesmente não existe.
        bool respondeu = _pronto.Wait(TimeSpan.FromSeconds(5));
        return respondeu && Instalado;
    }

    /// <summary>Desinstala o gancho e fecha a thread dele.</summary>
    public void Remover()
    {
        Thread? thread = _thread;
        _thread = null;
        _instalado = false;
        if (thread is null) return;

        try { PostThreadMessage(_idDaThread, WmQuit, IntPtr.Zero, IntPtr.Zero); }
        catch
        {
            // A thread pode já ter morrido: o gancho foi desinstalado com ela.
        }

        try { thread.Join(2000); }
        catch
        {
            // Thread que não morreu a tempo: ela é de fundo (IsBackground), e
            // o processo do app encerra sem esperar por ela.
        }
    }

    /// <summary>Fecha o gancho (o <see cref="IDisposable"/>).</summary>
    public void Dispose() => Remover();

    /// <summary>
    /// A thread do gancho: instala o gancho e fica bombeando mensagens.
    /// </summary>
    private void Rodar()
    {
        try
        {
            _idDaThread = GetCurrentThreadId();
            _procedimento = Procedimento;
            _gancho = SetWindowsHookEx(TecladoDeBaixoNivel, _procedimento, GetModuleHandle(IntPtr.Zero), 0);
            _instalado = _gancho != IntPtr.Zero;

            // O silêncio começa a contar agora: sem evento nenhum desde que o
            // gancho entrou, o app não sabe se o teclado está em silêncio.
            Interlocked.Exchange(ref _ultimaAtividade, Environment.TickCount64);
        }
        catch
        {
            // Gancho que não instala só significa que o app não vigia a tecla
            // presa — o resto do app segue igual.
            _gancho = IntPtr.Zero;
            _instalado = false;
        }
        finally
        {
            _pronto.Set();
        }

        if (!_instalado) return;

        Msg msg;
        try
        {
            // A bomba de mensagens é o que entrega o callback do gancho: sem
            // ela, o Windows não avisa nada (e remove o gancho).
            while (GetMessage(out msg, IntPtr.Zero, 0, 0) > 0)
            {
                TranslateMessage(ref msg);
                DispatchMessage(ref msg);
            }
        }
        catch
        {
            // Falha na bomba de mensagens: o gancho sai junto com a thread.
        }
        finally
        {
            try { UnhookWindowsHookEx(_gancho); } catch { }
            _gancho = IntPtr.Zero;
            _instalado = false;
        }
    }

    /// <summary>
    /// O callback do gancho: só registra o instante e passa a chave adiante.
    /// O Windows descarta o gancho se o callback demorar — por isso ele não
    /// faz nada além de anotar a hora.
    /// </summary>
    private IntPtr Procedimento(int codigo, IntPtr wParam, IntPtr lParam)
    {
        if (codigo >= 0)
        {
            Interlocked.Exchange(ref _ultimaAtividade, Environment.TickCount64);
        }
        return CallNextHookEx(_gancho, codigo, wParam, lParam);
    }
}
