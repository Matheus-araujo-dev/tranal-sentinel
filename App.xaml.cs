using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;

namespace ResetExplode;

public partial class App : Application
{
    // Uma única instância: abrir o RESET EXPLODE de novo só traz a janela que já está aberta para a frente.
    private static Mutex? _single;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindow(string? cls, string title);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr h);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr h);

    protected override void OnStartup(StartupEventArgs e)
    {
        // Teste de linha de comando do visor Kalkan: não abre janela e não trava
        // a instância única (dá para rodar com o app já aberto).
        if (Array.Exists(e.Args, a => string.Equals(a, "--kalkan-teste", StringComparison.OrdinalIgnoreCase)))
        {
            KalkanTeste.Rodar();
            Shutdown();
            return;
        }

        // Teste de linha de comando da guarda dos sensores: mostra o que o app
        // decidiu fazer com os sensores depois dos crashes.
        if (Array.Exists(e.Args, a => string.Equals(a, "--sensores", StringComparison.OrdinalIgnoreCase)))
        {
            SensorTeste.Rodar();
            Shutdown();
            return;
        }

        bool first;
        try { _single = new Mutex(true, @"Local\ResetExplode.SingleInstance", out first); }
        catch (UnauthorizedAccessException) { first = false; } // criado por outra instância com outro nível de permissão

        if (!first)
        {
            IntPtr h = FindWindow(null, "RESET EXPLODE");
            if (h != IntPtr.Zero)
            {
                if (IsIconic(h)) ShowWindow(h, 9); // SW_RESTORE
                SetForegroundWindow(h);
            }
            Shutdown();
            return;
        }
        base.OnStartup(e);
        new MainWindow().Show();
    }
}
