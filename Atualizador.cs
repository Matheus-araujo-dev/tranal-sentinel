using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;

namespace TranalSentinel;

/// <summary>
/// Troca do executável em uso quando entra uma versão nova.
/// </summary>
/// <remarks>
/// <para>
/// Não dá para sobrescrever um .exe que está rodando: o Windows trava o
/// arquivo. A solução é o app chamar a si mesmo com a flag
/// <see cref="AutoUpdate.ArgumentoAtualizacao"/>; esse processo filho espera o
/// pai sair, troca o arquivo e reabre o app.
/// </para>
/// <para>
/// O filho já nasce com privilégio de administrador (é filho de um processo
/// elevado), então consegue escrever em <c>%ProgramFiles%</c> sem pedir UAC de
/// novo — o que seria inaceitável no meio de uma atualização.
/// </para>
/// <para>
/// Os caminhos e o nome da flag não são repetidos aqui: vêm do
/// <see cref="AutoUpdate"/>, que é quem define o contrato da atualização.
/// </para>
/// </remarks>
public static class Atualizador
{
    /// <summary>
    /// Executa a troca (lado do processo filho): espera o pai sair, troca o
    /// executável e reabre o app.
    /// </summary>
    /// <returns>0 se deu tudo certo, 1 se falhou.</returns>
    public static int Aplicar(string caminhoNovo, int pidPai, bool reabrir)
    {
        try
        {
            if (caminhoNovo.Length == 0 || !File.Exists(caminhoNovo))
            {
                Console.Error.WriteLine("Arquivo da nova versão não encontrado: " + caminhoNovo);
                return 1;
            }

            // Quem chamou a atualização sabe qual processo está usando o .exe.
            // Se não vier no argumento, o log da atualização guarda o PID.
            if (pidPai <= 0) pidPai = LerPidDoLog();

            if (pidPai > 0 && !EsperarPaiSair(pidPai, TimeSpan.FromSeconds(120)))
            {
                Console.Error.WriteLine("O app anterior não fechou a tempo; atualização cancelada.");
                return 1;
            }

            string alvo = Environment.ProcessPath ?? "";
            if (alvo.Length == 0) return 1;

            if (!TrocarComTentativas(caminhoNovo, alvo))
            {
                Console.Error.WriteLine("Não foi possível trocar o executável (o arquivo continua em uso).");
                return 1;
            }

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

    /// <summary>
    /// Copia o executável novo por cima do antigo, com backup e rollback.
    /// </summary>
    /// <remarks>
    /// Se o Windows ainda estiver segurando o arquivo (o app velho levou
    /// alguns segundos para sair de verdade), a cópia é tentada de novo.
    /// </remarks>
    private static bool TrocarComTentativas(string origem, string alvo)
    {
        string backup = alvo + ".bak";
        bool tinhaBackup = false;

        try
        {
            if (File.Exists(backup)) File.Delete(backup);
            File.Copy(alvo, backup, true);
            tinhaBackup = true;
        }
        catch
        {
            // Sem backup não vale o risco: só troca direto se não houver nada para perder.
            tinhaBackup = false;
        }

        for (int tentativa = 0; tentativa < 20; tentativa++)
        {
            try
            {
                File.Copy(origem, alvo, true);
                if (tinhaBackup) { try { File.Delete(backup); } catch { } }
                return true;
            }
            catch (IOException)
            {
                // O arquivo ainda está em uso: espera o app velho soltar.
                Thread.Sleep(500);
            }
            catch (UnauthorizedAccessException)
            {
                Thread.Sleep(500);
            }
        }

        if (tinhaBackup)
        {
            try { File.Copy(backup, alvo, true); } catch { }
            try { File.Delete(backup); } catch { }
        }
        return false;
    }

    /// <summary>
    /// Descobre o PID do app que pediu a atualização lendo o log da atualização.
    /// </summary>
    /// <remarks>
    /// O log é escrito antes de o processo filho nascer, justamente para não
    /// haver corrida entre "gravar o pid" e "ler o pid".
    /// </remarks>
    private static int LerPidDoLog()
    {
        try
        {
            string arquivo = Path.Combine(AutoUpdate.PastaUpdate, AutoUpdate.NomeArquivoLog);
            if (!File.Exists(arquivo)) return 0;

            string[] linhas = File.ReadAllLines(arquivo);
            for (int i = linhas.Length - 1; i >= 0; i--)
            {
                int pos = linhas[i].LastIndexOf("pid ", StringComparison.OrdinalIgnoreCase);
                if (pos < 0) continue;
                string digitos = new string(linhas[i][(pos + 4)..].TakeWhile(char.IsDigit).ToArray());
                if (int.TryParse(digitos, out int pid) && pid > 0) return pid;
            }
        }
        catch { }
        return 0;
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
            catch
            {
                return true;   // o pai já não existe
            }
            Thread.Sleep(300);
        }
        return false;
    }
}
