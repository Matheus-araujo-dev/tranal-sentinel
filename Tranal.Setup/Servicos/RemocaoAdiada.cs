using System.Diagnostics;
using System.IO;

namespace TranalSetup.Servicos;

/// <summary>
/// Remove um arquivo que está em uso pelo processo atual.
///
/// O instalador é copiado para a pasta de instalação e é de lá que a
/// desinstalação é chamada: o Windows não deixa apagar um executável em
/// execução. A solução é pedir a remoção para depois que o processo terminar.
/// </summary>
public static class RemocaoAdiada
{
    /// <summary>
    /// Agenda a remoção do arquivo (e da pasta, se ela ficar vazia) para depois
    /// que este processo terminar. Devolve false se não foi possível agendar.
    /// </summary>
    /// <remarks>
    /// A tentativa é repetida por um tempo: quem costuma segurar o arquivo não é
    /// só o instalador, é o TRANAL SENTINEL aberto. Uma tentativa única
    /// deixaria o app antigo órfão quando ele estivesse aberto.
    /// </remarks>
    public static bool Agendar(string caminhoArquivo, string? pastaParaRemover = null)
    {
        try
        {
            string powershell = Path.Combine(
                Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
            if (!File.Exists(powershell)) powershell = "powershell.exe";

            string comando =
                "$arquivo='" + Escapar(caminhoArquivo) + "'"
                + "; for($i=0;$i -lt 40;$i++){"
                + " if(-not (Test-Path -LiteralPath $arquivo)){break};"
                + " Start-Sleep -Seconds 3;"
                + " Remove-Item -LiteralPath $arquivo -Force -ErrorAction SilentlyContinue }";
            if (!string.IsNullOrWhiteSpace(pastaParaRemover))
                comando += "; Remove-Item -LiteralPath '" + Escapar(pastaParaRemover) + "' -Force -ErrorAction SilentlyContinue";

            var inicio = new ProcessStartInfo(powershell)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            inicio.ArgumentList.Add("-NoProfile");
            inicio.ArgumentList.Add("-NonInteractive");
            inicio.ArgumentList.Add("-WindowStyle");
            inicio.ArgumentList.Add("Hidden");
            inicio.ArgumentList.Add("-Command");
            inicio.ArgumentList.Add(comando);

            Process.Start(inicio)?.Dispose();
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[instalador] não foi possível agendar a remoção de {caminhoArquivo}: {ex.Message}");
            return false;
        }
    }

    // O comando do PowerShell usa aspas simples: um apóstrofo no caminho
    // fecharia a string, então é duplicado (como o próprio PowerShell faz).
    private static string Escapar(string caminho) => caminho.Replace("'", "''");
}
