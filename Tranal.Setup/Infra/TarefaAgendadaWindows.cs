using System.Diagnostics;
using System.IO;
using System.Text;
using TranalSetup.Dominio;

namespace TranalSetup.Infra;

/// <summary>
/// Tarefa agendada do Windows, pelo comando <c>schtasks</c>.
/// </summary>
/// <remarks>
/// <para>
/// A definição da tarefa é passada como XML (e não pela linha de comando do
/// schtasks) porque o caminho do executável tem espaço e as aspas do
/// schtasks são traiçoeiras. Com o XML, o que o Windows recebe é exatamente
/// o que o <see cref="TarefaInicializacao.GerarXml"/> montou.
/// </para>
/// </remarks>
public sealed class TarefaAgendadaWindows : ITarefaAgendada
{
    /// <inheritdoc />
    public void Criar(string nome, string caminhoExecutavel, string argumento)
    {
        string xml = TarefaInicializacao.GerarXml(caminhoExecutavel, argumento, TarefaInicializacao.UsuarioAtual());
        string arquivo = Path.Combine(Path.GetTempPath(), $"tranal-tarefa-{Guid.NewGuid():N}.xml");
        try
        {
            // O schtasks espera o XML em UTF-16, que é o que a declaração diz.
            File.WriteAllText(arquivo, xml, Encoding.Unicode);

            var argumentos = new List<string> { "/create", "/tn", nome, "/xml", arquivo, "/f" };
            (int codigo, string saida) = Executar("schtasks", argumentos);
            if (codigo != 0)
                throw new InvalidOperationException($"O schtasks não criou a tarefa (código {codigo}): {saida}");
        }
        finally
        {
            try { if (File.Exists(arquivo)) File.Delete(arquivo); } catch { }
        }
    }

    /// <inheritdoc />
    public bool Existe(string nome)
    {
        (int codigo, _) = Executar("schtasks", new List<string> { "/query", "/tn", nome });
        return codigo == 0;
    }

    /// <inheritdoc />
    public void Remover(string nome)
    {
        // A remoção de uma tarefa que não existe também devolve erro: não é problema.
        Executar("schtasks", new List<string> { "/delete", "/tn", nome, "/f" });
    }

    private static (int Codigo, string Saida) Executar(string arquivo, List<string> argumentos)
    {
        try
        {
            var info = new ProcessStartInfo(arquivo)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            foreach (string argumento in argumentos) info.ArgumentList.Add(argumento);

            using var processo = Process.Start(info);
            if (processo == null) return (-1, "não foi possível iniciar o " + arquivo);

            string saida = processo.StandardOutput.ReadToEnd();
            string erro = processo.StandardError.ReadToEnd();
            processo.WaitForExit(30_000);
            return (processo.ExitCode, saida + erro);
        }
        catch (Exception ex)
        {
            return (-1, ex.Message);
        }
    }
}
