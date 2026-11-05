using System;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace TranalSentinel;

/// <summary>
/// Troca do executável em uso quando entra uma versão nova.
/// </summary>
/// <remarks>
/// <para>
/// Não dá para sobrescrever um .exe que está rodando: o Windows trava o
/// arquivo. A solução é o app chamar a si mesmo com a flag
/// <c>--atualizar</c>; esse processo filho espera o pai sair, troca o arquivo
/// e reabre o app.
/// </para>
/// <para>
/// O filho já nasce com privilégio de administrador (é filho de um processo
/// elevado), então consegue escrever em <c>%ProgramFiles%</c> sem pedir UAC de
/// novo — o que seria inaceitável no meio de uma atualização.
/// </para>
/// </remarks>
public static class Atualizador
{
    /// <summary>Flag que liga o modo de troca do executável.</summary>
    public const string Argumento = "--atualizar";

    /// <summary>Pastas onde a versão nova é baixada antes da troca.</summary>
    public static string Pasta => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Tranal Sentinel", "update");

    /// <summary>Nome do arquivo baixado enquanto a troca não acontece.</summary>
    public static string CaminhoNovo => Path.Combine(Pasta, "TranalSentinel.exe.novo");

    /// <summary>
    /// Dispara a troca: chama este mesmo executável com a flag de atualização.
    /// </summary>
    /// <remarks>Devolve o processo filho (que fica esperando o pai sair) ou null se deu erro.</remarks>
    public static Process? Iniciar(string caminhoNovo)
    {
        try
        {
            string exe = Environment.ProcessPath ?? "";
            if (exe.Length == 0) return null;

            var info = new ProcessStartInfo(exe)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            info.ArgumentList.Add(Argumento);
            info.ArgumentList.Add(caminhoNovo);
            info.ArgumentList.Add("--pid");
            info.ArgumentList.Add(Environment.ProcessId.ToString());
            return Process.Start(info);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Executa a troca (lado do processo filho): espera o pai sair, troca o
    /// executável e reabre o app.
    /// </summary>
    /// <returns>0 se deu tudo certo, 1 se falhou.</returns>
    public static int Aplicar(string caminhoNovo, int pidPai, bool reabrir)
    {
        try
        {
            if (!File.Exists(caminhoNovo))
            {
                Console.Error.WriteLine("Arquivo da nova versão não encontrado: " + caminhoNovo);
                return 1;
            }

            // A troca só pode começar quando o app velho não estiver mais usando o .exe.
            if (!EsperarPaiSair(pidPai, TimeSpan.FromSeconds(120)))
            {
                Console.Error.WriteLine("O app anterior não fechou a tempo; atualização cancelada.");
                return 1;
            }

            string alvo = Environment.ProcessPath ?? "";
            if (alvo.Length == 0) return 1;

            string backup = alvo + ".bak";
            bool tinhaBackup = File.Exists(backup);
            try
            {
                if (tinhaBackup) File.Delete(backup);
                File.Copy(alvo, backup, true);
            }
            catch
            {
                // Sem backup não vale o risco: troca direto só se não houver nada para perder.
                tinhaBackup = false;
            }

            try
            {
                File.Copy(caminhoNovo, alvo, true);
            }
            catch (Exception ex)
            {
                if (tinhaBackup)
                {
                    try { File.Copy(backup, alvo, true); } catch { }
                    try { File.Delete(backup); } catch { }
                }
                Console.Error.WriteLine("Falha ao trocar o executável: " + ex.Message);
                return 1;
            }

            try { if (tinhaBackup) File.Delete(backup); } catch { }
            try { File.Delete(caminhoNovo); } catch { }

            if (reabrir)
            {
                try
                {
                    Process.Start(new ProcessStartInfo(alvo) { UseShellExecute = false });
                }
                catch { }
            }
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Falha na atualização: " + ex.Message);
            return 1;
        }
    }

    /// <summary>Espera o processo pai terminar (ou o tempo acabar).</summary>
    private static bool EsperarPaiSair(int pidPai, TimeSpan limite)
    {
        if (pidPai <= 0) return true;
        DateTime fim = DateTime.UtcNow + limite;
        while (DateTime.UtcNow < fim)
        {
            try
            {
                using var p = Process.GetProcessById(pidPai);
                if (p.HasExited) return true;
            }
            catch (ArgumentException)
            {
                return true;   // o pai já não existe
            }
            catch
            {
                return true;
            }
            Thread.Sleep(300);
        }
        return false;
    }
}
