using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace ResetExplode;

/// <summary>
/// Teste de linha de comando do visor Kalkan.
///
/// Serve para conferir o hardware sem depender da interface:
/// <c>ResetExplode.exe --kalkan-teste</c> conecta no visor, manda um quadro
/// com a temperatura do CPU e grava o resultado em
/// <c>%AppData%\ResetExplode\kalkan-teste.txt</c>.
/// </summary>
internal static class KalkanTeste
{
    public static string CaminhoRelatorio
    {
        get
        {
            string pasta = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ResetExplode");
            Directory.CreateDirectory(pasta);
            return Path.Combine(pasta, "kalkan-teste.txt");
        }
    }

    public static void Rodar()
    {
        var linhas = new List<string>
        {
            "Teste do visor Kalkan · " + DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss"),
            ""
        };

        try
        {
            using (var visor = new KalkanDisplay())
            {
                bool conectou = visor.Conectar();
                linhas.Add("Conexão: " + (conectou ? "OK · " + visor.DetalheConexao : "FALHOU · " + visor.Erro));

                if (conectou)
                {
                    float? temperatura = null;
                    ushort fan = 0;
                    byte carga = 0;

                    using (var monitor = new SensorMonitor())
                    {
                        try
                        {
                            monitor.OpenAsync().Wait(TimeSpan.FromSeconds(10));
                            monitor.Refresh();
                            temperatura = monitor.CpuTemperature;
                            carga = monitor.CpuLoad.HasValue
                                ? (byte)Math.Clamp(Math.Round(monitor.CpuLoad.Value), 0, 100)
                                : (byte)0;
                            fan = Ventoinha(monitor);
                        }
                        catch (Exception ex)
                        {
                            linhas.Add("Sensores: " + ex.Message);
                        }
                    }

                    linhas.Add("Temperatura do CPU: " + (temperatura.HasValue ? temperatura.Value.ToString("0.#") : "sem leitura") + " °C");
                    linhas.Add("Ventoinha: " + fan + " RPM · carga: " + carga + " %");

                    byte temp;
                    ushort rpm;
                    if (temperatura.HasValue)
                    {
                        temp = (byte)Math.Clamp(Math.Round(temperatura.Value), 0, 255);
                        rpm = fan;
                        linhas.Add("Quadro enviado com os valores reais do PC.");
                    }
                    else
                    {
                        // Sem leitura de sensor (falta rodar como administrador): manda um
                        // quadro de teste bem visível para dar para conferir no visor.
                        temp = 99;
                        rpm = 4321;
                        carga = 55;
                        linhas.Add("Sem leitura de sensor: para o teste, foi enviado um quadro de");
                        linhas.Add("TESTE (99 °C / 4321 RPM / 55 %). No visor deve aparecer 99.");
                    }

                    bool enviou = visor.Enviar(temp, fan == 0 ? rpm : fan, carga);
                    linhas.Add("Envio do quadro: " + (enviou ? "OK" : "FALHOU · " + visor.Erro));
                    linhas.Add("Quadros enviados: " + visor.QuadrosEnviados + " · respostas do visor: " + visor.LeiturasRecebidas);
                    if (visor.UltimaLeituraHex.Length > 0)
                        linhas.Add("Última resposta do visor: " + visor.UltimaLeituraHex);
                    linhas.Add("(a resposta vem decodificada pelo próprio aparelho: 01 e0 <índice>)");
                }
            }
        }
        catch (Exception ex)
        {
            linhas.Add("Erro inesperado: " + ex);
        }

        linhas.Add("");
        linhas.Add("A leitura da temperatura do CPU precisa do app rodando como administrador");
        linhas.Add("(e do driver PawnIO). Sem isso, o teste envia valores de teste.");

        try
        {
            File.WriteAllLines(CaminhoRelatorio, linhas, new UTF8Encoding(false));
        }
        catch
        {
            // Se não der para gravar, pelo menos não derruba o app.
        }
    }

    private static ushort Ventoinha(SensorMonitor monitor)
    {
        var ventoinhas = monitor.Rows
            .Where(r => r.Type == LibreHardwareMonitor.Hardware.SensorType.Fan && r.RawValue is > 0)
            .ToList();
        if (ventoinhas.Count == 0) return 0;
        var cpu = ventoinhas.FirstOrDefault(r => r.HardwareType == LibreHardwareMonitor.Hardware.HardwareType.Cpu);
        float rpm = cpu?.RawValue ?? ventoinhas.Max(r => r.RawValue ?? 0);
        return (ushort)Math.Clamp(Math.Round(rpm), 0, ushort.MaxValue);
    }
}
