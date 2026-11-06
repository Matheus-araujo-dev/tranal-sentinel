using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;

namespace TranalSentinel;

public partial class App : Application
{
    // Uma única instância: abrir o TRANAL SENTINEL de novo só traz a janela que já está aberta para a frente.
    private static Mutex? _single;

    /// <summary>
    /// O app foi aberto junto com o Windows: entra escondido na bandeja,
    /// sem tomar a tela no primeiro momento.
    /// </summary>
    public static bool IniciarComoSistema { get; private set; }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindow(string? cls, string title);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr h);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr h);

    protected override void OnStartup(StartupEventArgs e)
    {
        // Troca do executável quando entra uma versão nova: este processo roda
        // separado e espera o app atual fechar antes de mexer no arquivo.
        int indiceAtualizar = Array.FindIndex(e.Args, a => string.Equals(a, AutoUpdate.ArgumentoAtualizacao, StringComparison.OrdinalIgnoreCase));
        if (indiceAtualizar >= 0)
        {
            string caminhoNovo = indiceAtualizar + 1 < e.Args.Length ? e.Args[indiceAtualizar + 1] : "";
            int pidPai = Argumento(e.Args, "--pid", 0);
            bool reabrir = !Array.Exists(e.Args, a => string.Equals(a, "--sem-reabrir", StringComparison.OrdinalIgnoreCase));
            Shutdown(Atualizador.Aplicar(caminhoNovo, pidPai, reabrir));
            return;
        }

        // Diagnóstico pela linha de comando: coleta, comparação e relatório.
        // Reaproveita o mesmo núcleo da aba TRANAL SAÚDE.
        if (Array.Exists(e.Args, a => string.Equals(a, "--saude", StringComparison.OrdinalIgnoreCase)))
        {
            Shutdown(SaudeCli.Executar(e.Args));
            return;
        }

        // Memória pela linha de comando: lê o SPD dos módulos e mostra em texto.
        if (Array.Exists(e.Args, a => string.Equals(a, "--memoria", StringComparison.OrdinalIgnoreCase)))
        {
            Shutdown(MemoriaCli.Executar(e.Args));
            return;
        }

        // S.M.A.R.T. pela linha de comando: lê a tabela de atributos dos
        // discos e mostra em texto.
        if (Array.Exists(e.Args, a => string.Equals(a, "--smart", StringComparison.OrdinalIgnoreCase)))
        {
            Shutdown(SmartCli.Executar(e.Args));
            return;
        }

        // Comparação de benchmarks entre PCs pela linha de comando: exporta
        // os resultados desta máquina ou importa o arquivo de outro PC.
        if (Array.Exists(e.Args, a => string.Equals(a, "--comparar", StringComparison.OrdinalIgnoreCase)))
        {
            Shutdown(CompararCli.Executar(e.Args));
            return;
        }

        // Processo filho da coleta de sensores: não abre janela e não trava
        // a instância única (é quem grava as leituras para o app pai ler).
        int indiceWorker = Array.FindIndex(e.Args, a => string.Equals(a, SensorWorker.Argumento, StringComparison.OrdinalIgnoreCase));
        if (indiceWorker >= 0)
        {
            string caminhoLeituras = indiceWorker + 1 < e.Args.Length ? e.Args[indiceWorker + 1] : "";
            string caminhoPausa = indiceWorker + 2 < e.Args.Length ? e.Args[indiceWorker + 2] : "";
            int idPai = indiceWorker + 3 < e.Args.Length && int.TryParse(e.Args[indiceWorker + 3], out int pid) ? pid : 0;
            bool semGpu = Array.Exists(e.Args, a => string.Equals(a, "--sem-gpu", StringComparison.OrdinalIgnoreCase));
            int intervalo = Argumento(e.Args, "--intervalo", 1000);
            if (caminhoLeituras.Length > 0)
                SensorWorker.Rodar(caminhoLeituras, caminhoPausa, idPai, !semGpu, intervalo);
            Shutdown();
            return;
        }

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
        try { _single = new Mutex(true, @"Local\TranalSentinel.SingleInstance", out first); }
        catch (UnauthorizedAccessException) { first = false; } // criado por outra instância com outro nível de permissão

        if (!first)
        {
            IntPtr h = FindWindow(null, "TRANAL SENTINEL");
            if (h != IntPtr.Zero)
            {
                if (IsIconic(h)) ShowWindow(h, 9); // SW_RESTORE
                SetForegroundWindow(h);
            }
            Shutdown();
            return;
        }

        IniciarComoSistema = Array.Exists(e.Args, a => string.Equals(a, "--iniciar-como-sistema", StringComparison.OrdinalIgnoreCase));

        base.OnStartup(e);
        new MainWindow().Show();
    }

    /// <summary>Lê o valor de um argumento do tipo "--nome valor".</summary>
    private static int Argumento(string[] args, string nome, int padrao)
    {
        int i = Array.FindIndex(args, a => string.Equals(a, nome, StringComparison.OrdinalIgnoreCase));
        if (i < 0 || i + 1 >= args.Length) return padrao;
        return int.TryParse(args[i + 1], out int n) ? n : padrao;
    }
}
