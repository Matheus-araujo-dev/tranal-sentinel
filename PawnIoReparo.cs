using System.Diagnostics;
using Microsoft.Win32;

namespace TranalSentinel;

/// <summary>
/// Reinicia o dispositivo do driver PawnIO (o executor; a decisão de quando
/// oferecer mora em <see cref="PawnIoEstado"/>).
///
/// <para>
/// É o que tirou o driver do estado travado no PC do dono em 22/11/2026
/// (<c>pnputil /restart-device ROOT\PAWNIO\0001</c>): o dispositivo volta a
/// existir sem reiniciar o PC e sem reinstalar nada. Não há o que desfazer:
/// reiniciar o dispositivo não muda configuração nenhuma. Só roda quando o dono
/// confirma, e o resultado vai para o diário.
/// </para>
/// </summary>
public static class PawnIoReparo
{
    /// <summary>
    /// Os dispositivos do PawnIO cadastrados no Windows (ROOT\PAWNIO\0000,
    /// 0001...). O número muda a cada reinstalação, então é lido, nunca fixo.
    /// </summary>
    public static IReadOnlyList<string> Dispositivos()
    {
        using var chave = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\ROOT\PAWNIO");
        return chave?.GetSubKeyNames().Select(n => @"ROOT\PAWNIO\" + n).ToList() ?? new List<string>();
    }

    /// <summary>
    /// Reinicia cada dispositivo do PawnIO e devolve se o driver abre depois,
    /// com o relato do que aconteceu (para a tela e para o diário).
    /// </summary>
    public static (bool Abriu, string Relato) Reiniciar()
    {
        var dispositivos = Dispositivos();
        if (dispositivos.Count == 0)
            return (false, "Nenhum dispositivo do PawnIO está cadastrado no Windows: reinstale o TRANAL SENTINEL, que instala o driver.");

        var partes = new List<string>();
        foreach (string id in dispositivos)
        {
            try
            {
                var psi = new ProcessStartInfo(System.IO.Path.Combine(Environment.SystemDirectory, "pnputil.exe"), $"/restart-device \"{id}\"")
                {
                    UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true,
                };
                using var p = Process.Start(psi)!;
                // A saída é lida em paralelo: lida antes da espera, ela prenderia
                // o prazo (o ReadToEnd só volta quando o processo acaba).
                var saida = p.StandardOutput.ReadToEndAsync();
                if (!p.WaitForExit(30_000))
                {
                    try { p.Kill(); } catch (InvalidOperationException) { /* Já tinha terminado entre a espera e o Kill. */ }
                    partes.Add($"{id}: o pnputil não respondeu em 30 s");
                    continue;
                }
                saida.Wait(2000);
                partes.Add(p.ExitCode switch
                {
                    0 => $"{id}: reiniciado",
                    3010 => $"{id}: reiniciado, mas o Windows pede reiniciar o PC",
                    _ => $"{id}: o pnputil devolveu o código {p.ExitCode}",
                });
            }
            catch (Exception ex)
            {
                partes.Add($"{id}: {ex.Message}");
            }
        }

        Thread.Sleep(1500);   // o dispositivo leva um instante para voltar
        int erro = PawnIoTransporte.Sondar();
        string relato = erro == 0
            ? $"Driver PawnIO reiniciado e respondendo ({string.Join("; ", partes)})."
            : $"Driver PawnIO reiniciado, mas continua sem abrir: {PawnIoEstado.MotivoDeNaoAbrir(SensorMonitor.IsPawnIOInstalled, erro)} " +
              $"({string.Join("; ", partes)}). Feche o OpenRGB ou outro programa de RGB e sensores, ou reinicie o PC.";
        return (erro == 0, relato);
    }
}
