using System.Diagnostics;
using System.IO;
using System.Text;

namespace TranalSetup.Infra;

/// <summary>
/// Log do instalador em %ProgramData%\Tranal Sentinel\instalador.log.
///
/// Uma linha por operação, com data e hora. Se a gravação falhar, o instalador
/// continua: um log que não grava não pode derrubar a instalação.
/// </summary>
public sealed class LogInstalador : ILog
{
    private readonly string _arquivo;

    /// <summary>Cria o log apontando para o arquivo informado.</summary>
    public LogInstalador(string arquivo)
    {
        _arquivo = arquivo;
    }

    /// <inheritdoc />
    public void Registrar(string mensagem)
    {
        string linha = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {mensagem}";
        try
        {
            string? pasta = Path.GetDirectoryName(_arquivo);
            if (!string.IsNullOrEmpty(pasta)) Directory.CreateDirectory(pasta);
            File.AppendAllText(_arquivo, linha + Environment.NewLine, Encoding.UTF8);
        }
        catch (Exception ex)
        {
            // O log é um registro, não um requisito: avisa no console e segue.
            Debug.WriteLine($"[instalador] não foi possível gravar o log: {ex.Message}");
            EscreverNoConsole($"[instalador] não foi possível gravar o log ({_arquivo}): {ex.Message}");
        }
    }

    private static void EscreverNoConsole(string texto)
    {
        try
        {
            Console.Error.WriteLine(texto);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[instalador] console indisponível: {ex.Message}");
        }
    }
}
