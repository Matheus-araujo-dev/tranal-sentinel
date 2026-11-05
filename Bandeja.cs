using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace TranalSentinel;

/// <summary>
/// Ícone na bandeja do sistema (a "bolinha" ao lado do relógio).
/// </summary>
/// <remarks>
/// <para>
/// Existe para o app poder ficar acompanhando o PC em segundo plano: aberto
/// junto com o Windows, ele precisa morar na bandeja e não tomar a tela toda.
/// </para>
/// <para>
/// É feito com P/Invoke direto (Shell_NotifyIcon) em vez de System.Windows.Forms:
/// ligar o WinForms neste projeto criaria ambiguidade entre os tipos de nome
/// igual dos dois frameworks (MessageBox, Application, ToolTip) e o app já
/// faz P/Invoke em vários lugares — é o padrão do projeto.
/// </para>
/// </remarks>
public sealed class Bandeja : IDisposable
{
    private const int NIM_ADD = 0x00000000;
    private const int NIM_MODIFY = 0x00000001;
    private const int NIM_DELETE = 0x00000002;
    private const int NIM_SETVERSION = 0x00000004;
    private const int NOTIFYICON_VERSION_4 = 4;

    private const int NIF_MESSAGE = 0x00000001;
    private const int NIF_ICON = 0x00000002;
    private const int NIF_TIP = 0x00000004;

    private const int WM_DESTROY = 0x0002;
    private const int WM_COMMAND = 0x0111;
    private const int WM_CONTEXTMENU = 0x007B;
    private const int WM_APP = 0x8000;
    private const int WM_TRAY = WM_APP + 1;

    private const int TPM_RIGHTBUTTON = 0x0002;
    private const int MF_STRING = 0x0000;

    private readonly IntPtr _janela;
    private IntPtr _iconeGrande;
    private IntPtr _iconePequeno;
    private bool _criado;

    /// <summary>Disparado quando o usuário pede para abrir a janela (clique no ícone).</summary>
    public event Action? AoAbrir;

    /// <summary>Disparado quando o usuário pede para sair de verdade (menu do ícone).</summary>
    public event Action? AoSair;

    /// <summary>Disparado quando o usuário pede para encerrar o programa travado.</summary>
    public event Action? AoEncerrarTravado;

    private bool _temTravado;
    private string _rotuloTravado = "Encerrar programa travado";

    /// <summary>Cria o ícone na bandeja, preso a uma janela do WPF.</summary>
    /// <param name="janela">Janela que hospeda o ícone e recebe as mensagens da bandeja.</param>
    /// <param name="dica">Texto que aparece ao passar o mouse (ex.: "TRANAL SENTINEL — tudo certo").</param>
    public Bandeja(Window janela, string dica)
    {
        var helper = new WindowInteropHelper(janela);
        _janela = helper.EnsureHandle();

        // O ícone sai do próprio executável: é o mesmo da barra de tarefas e
        // dispensa qualquer arquivo extra em disco.
        string caminho = Environment.ProcessPath ?? "";
        if (caminho.Length > 0 && File.Exists(caminho))
        {
            if (ExtractIconEx(caminho, 0, out _iconeGrande, out _iconePequeno, 1) == 0)
            {
                _iconeGrande = IntPtr.Zero;
                _iconePequeno = IntPtr.Zero;
            }
        }

        var dados = new NOTIFYICONDATA
        {
            cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = _janela,
            uID = 1,
            uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP,
            uCallbackMessage = WM_TRAY,
            hIcon = _iconePequeno,
            szTip = Truncar(dica, 127),
        };

        if (Shell_NotifyIcon(NIM_ADD, ref dados))
        {
            _criado = true;
            dados.uTimeoutOrVersion = (uint)NOTIFYICON_VERSION_4;
            Shell_NotifyIcon(NIM_SETVERSION, ref dados);
        }

        // As mensagens da bandeja chegam na fila da janela: este é o gancho que as intercepta.
        var fonte = HwndSource.FromHwnd(_janela);
        fonte?.AddHook(ProcMensagem);
    }

    /// <summary>Atualiza a dica (texto ao passar o mouse) sem recriar o ícone.</summary>
    public void AtualizarDica(string dica)
    {
        if (!_criado) return;
        var dados = new NOTIFYICONDATA
        {
            cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = _janela,
            uID = 1,
            uFlags = NIF_TIP,
            szTip = Truncar(dica, 127),
        };
        Shell_NotifyIcon(NIM_MODIFY, ref dados);
    }

    /// <summary>
    /// Diz se há programa travado (o menu ganha a opção de encerrar).
    /// </summary>
    public void DefinirTravado(bool disponivel, string rotulo = "Encerrar programa travado")
    {
        _temTravado = disponivel;
        _rotuloTravado = string.IsNullOrWhiteSpace(rotulo) ? "Encerrar programa travado" : rotulo;
    }

    private IntPtr ProcMensagem(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_TRAY)
        {
            // Com NOTIFYICON_VERSION_4, o evento vem no lParam (baixa palavra)
            // e o id do ícone no wParam (baixa palavra).
            int evento = (int)(lParam.ToInt64() & 0xFFFF);
            if (evento == WM_CONTEXTMENU || evento == 0x0204 /* WM_RBUTTONUP */)
            {
                MostrarMenu();
                handled = true;
            }
            else if (evento == 0x0205 /* WM_LBUTTONUP */ || evento == 0x0202 /* WM_LBUTTONDBLCLK */)
            {
                AoAbrir?.Invoke();
                handled = true;
            }
            return IntPtr.Zero;
        }

        if (msg == WM_COMMAND)
        {
            int id = (int)(wParam.ToInt64() & 0xFFFF);
            if (id == 1001) { AoAbrir?.Invoke(); handled = true; }
            else if (id == 1002) { AoSair?.Invoke(); handled = true; }
            else if (id == 1003) { AoEncerrarTravado?.Invoke(); handled = true; }
            return IntPtr.Zero;
        }

        return IntPtr.Zero;
    }

    private void MostrarMenu()
    {
        var menu = CreatePopupMenu();
        if (menu == IntPtr.Zero) return;
        try
        {
            AppendMenu(menu, MF_STRING, 1001, "Abrir o TRANAL SENTINEL");
            if (_temTravado) AppendMenu(menu, MF_STRING, 1003, _rotuloTravado);
            AppendMenu(menu, MF_STRING, 1002, "Sair");
            // A janela precisa estar em primeiro plano para o menu sumir ao clicar fora.
            SetForegroundWindow(_janela);
            uint cmd = TrackPopupMenu(menu, TPM_RIGHTBUTTON, 0, 0, 0, _janela, IntPtr.Zero);
            if (cmd == 1001) AoAbrir?.Invoke();
            else if (cmd == 1002) AoSair?.Invoke();
            else if (cmd == 1003) AoEncerrarTravado?.Invoke();
        }
        finally
        {
            DestroyMenu(menu);
        }
    }

    private static string Truncar(string texto, int maximo)
    {
        if (string.IsNullOrEmpty(texto)) return "";
        return texto.Length <= maximo ? texto : texto[..maximo];
    }

    /// <summary>Remove o ícone da bandeja e libera os handles.</summary>
    public void Dispose()
    {
        if (_criado)
        {
            var dados = new NOTIFYICONDATA
            {
                cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATA>(),
                hWnd = _janela,
                uID = 1,
            };
            Shell_NotifyIcon(NIM_DELETE, ref dados);
            _criado = false;
        }
        if (_iconePequeno != IntPtr.Zero) { DestroyIcon(_iconePequeno); _iconePequeno = IntPtr.Zero; }
        if (_iconeGrande != IntPtr.Zero) { DestroyIcon(_iconeGrande); _iconeGrande = IntPtr.Zero; }
    }

    // ---------------------------------------------------------------- P/Invoke

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATA
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public uint uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIcon(uint mensagem, ref NOTIFYICONDATA dados);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint ExtractIconEx(string arquivo, int indice, out IntPtr grande, out IntPtr pequeno, uint quantidade);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr icone);

    [DllImport("user32.dll")]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool AppendMenu(IntPtr menu, uint opcoes, uint id, string texto);

    [DllImport("user32.dll")]
    private static extern uint TrackPopupMenu(IntPtr menu, uint opcoes, int x, int y, int reservado, IntPtr janela, IntPtr semPai);

    [DllImport("user32.dll")]
    private static extern bool DestroyMenu(IntPtr menu);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr janela);
}
