using System.IO;
using System.Text;

namespace ResetExplode;

/// <summary>
/// Teste de linha de comando da guarda dos sensores.
///
/// <c>ResetExplode.exe --sensores</c> mostra o que o app decidiu fazer com os
/// sensores depois dos crashes: quantos crashes o Windows registrou, qual
/// caminho foi desligado e se a coleta abre sem morrer. O resultado fica em
/// <c>%AppData%\ResetExplode\sensores.txt</c>.
///
/// Serve para conferir a decisão sem depender da janela — e para provar que
/// abrir a coleta sem a GPU é seguro na máquina que crashou.
/// </summary>
internal static class SensorTeste
{
    public static string CaminhoRelatorio
    {
        get
        {
            string pasta = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ResetExplode");
            Directory.CreateDirectory(pasta);
            return Path.Combine(pasta, "sensores.txt");
        }
    }

    public static void Rodar()
    {
        var linhas = new List<string>
        {
            "Teste dos sensores · " + DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss"),
            ""
        };

        try
        {
            string nome = System.Diagnostics.Process.GetCurrentProcess().ProcessName;
            var crashes = SensorCrashes.Ler(nome);
            DateTime agora = DateTime.Now;

            linhas.Add($"Crashes do app no log do Windows: {crashes.Count}");
            foreach (var c in crashes.Take(10))
                linhas.Add($"  {c.Momento:dd/MM/yyyy HH:mm:ss}  {c.CodigoExcecao}  modulo={c.Modulo}");

            int n = SensorGuard.ContarCrashes(crashes, agora);
            var nivel = SensorGuard.Nivel(crashes, agora);
            linhas.Add("");
            linhas.Add($"Crashes nativos na janela: {n}");
            linhas.Add($"Nivel escolhido: {nivel}");
            linhas.Add($"Usar o LibreHardwareMonitor: {SensorGuard.UsarLhm(nivel)}");
            linhas.Add($"Usar a GPU (ADL do driver da AMD): {SensorGuard.UsarGpuAdl(nivel)}");
            linhas.Add($"Aviso para o usuario: {SensorGuard.Mensagem(nivel, n)}");

            linhas.Add("");
            linhas.Add("Abrindo a coleta com essa configuracao...");
            using (var monitor = new SensorMonitor { PermitirGpuAdl = SensorGuard.UsarGpuAdl(nivel) })
            {
                try
                {
                    monitor.OpenAsync().Wait(TimeSpan.FromSeconds(40));
                    monitor.Refresh();
                    linhas.Add($"Sensores lidos: {monitor.Rows.Count}");
                    linhas.Add($"Processo de coleta vivo: {monitor.ProcessoVivo}");
                    linhas.Add($"Mortes do processo: {monitor.MortesProcesso}");
                    foreach (var hw in monitor.Rows.Select(r => r.Hardware).Distinct())
                        linhas.Add($"  - {hw}");
                    if (monitor.Falhas.Count > 0)
                    {
                        linhas.Add("Falhas na coleta:");
                        foreach (var f in monitor.Falhas) linhas.Add($"  ! {f}");
                    }

                    // Prova do isolamento: mata o processo de coleta e confere se o
                    // app o reinicia sozinho (é o mesmo caminho de um access violation).
                    linhas.Add("");
                    linhas.Add("Matando o processo de coleta de proposito...");
                    bool matou = monitor.MatarColetorParaTeste();
                    linhas.Add($"Processo morto: {matou}");
                    Thread.Sleep(8000);   // dá tempo de o pai reiniciar o coletor
                    monitor.Refresh();
                    linhas.Add($"Mortes do processo: {monitor.MortesProcesso}");
                    linhas.Add($"Processo de coleta vivo depois: {monitor.ProcessoVivo}");
                    linhas.Add($"GPU na coleta depois da morte: {monitor.PermitirGpuAdl}");
                    linhas.Add($"Sensores lidos depois: {monitor.Rows.Count}");
                }
                catch (Exception ex)
                {
                    linhas.Add("Falha ao abrir a coleta: " + ex);
                }
            }
        }
        catch (Exception ex)
        {
            linhas.Add("Erro inesperado: " + ex);
        }

        linhas.Add("");
        linhas.Add("A coleta roda num processo separado: um access violation do driver mata");
        linhas.Add("só esse processo, e o app o reinicia (sem a GPU, se foi ela a culpada).");

        try
        {
            File.WriteAllLines(CaminhoRelatorio, linhas, new UTF8Encoding(false));
        }
        catch
        {
            // Se não der para gravar, pelo menos não derruba o app.
        }
    }
}
