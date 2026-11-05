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
                    monitor.OpenAsync().Wait(TimeSpan.FromSeconds(30));
                    monitor.Refresh();
                    linhas.Add($"Sensores lidos: {monitor.Rows.Count}");
                    linhas.Add($"Hardwares detectados: {monitor.QuantidadeHardware}");
                    linhas.Add($"Computer pronto: {monitor.ComputerPronto}");
                    if (monitor.ErroAbertura is not null)
                    {
                        linhas.Add("ERRO ao abrir o computer:");
                        linhas.Add(monitor.ErroAbertura);
                    }
                    if (monitor.ErroColeta is not null)
                    {
                        linhas.Add("ERRO na coleta:");
                        linhas.Add(monitor.ErroColeta);
                    }
                    foreach (var hw in monitor.Rows.Select(r => r.Hardware).Distinct())
                        linhas.Add($"  - {hw}");
                    if (monitor.Falhas.Count > 0)
                    {
                        linhas.Add("Falhas na coleta:");
                        foreach (var f in monitor.Falhas) linhas.Add($"  ! {f}");
                    }
                }
                catch (Exception ex)
                {
                    linhas.Add("Falha ao abrir a coleta: " + ex.Message);
                }
            }
        }
        catch (Exception ex)
        {
            linhas.Add("Erro inesperado: " + ex);
        }

        linhas.Add("");
        linhas.Add("O caminho nativo da GPU (ADL) fica de fora quando o app ja morreu por falha nativa.");

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
