using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace TranalSetup.Infra;

/// <summary>
/// Liga o instalador ao console do processo que o chamou.
///
/// O instalador é um WinExe (não pode abrir uma janela de console junto com a
/// interface), mas os modos de linha de comando precisam escrever no console.
/// Com o processo já redirecionado (pipe do PowerShell, por exemplo) não há
/// nada a fazer: a saída já vai para o lugar certo.
/// </summary>
public static class ConsoleAnexo
{
    private const int ProcessoPai = -1;
    private const int CodepageUnicode = 65001;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(int idProcesso);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AllocConsole();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern int GetConsoleOutputCP();

    private static bool _anexado;

    /// <summary>
    /// Prepara a saída de console. Devolve false quando não há console para
    /// escrever (nesse caso o instalador só escreve no log).
    /// </summary>
    public static bool Anexar()
    {
        if (_anexado) return true;
        _anexado = true;

        // Já existe um destino para a saída (pipe, arquivo): não mexe nos handles.
        bool redirecionado = Console.IsOutputRedirected;
        if (!redirecionado)
        {
            if (!AttachConsole(ProcessoPai))
            {
                // Sem console do pai: abre um console próprio, senão a saída se perde.
                if (!AllocConsole()) return false;
            }
        }

        Encoding codificacao = redirecionado ? Utf8SemBom() : CodificacaoDoConsole();

        try
        {
            Console.SetOut(new StreamWriter(Console.OpenStandardOutput(), codificacao) { AutoFlush = true });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[instalador] não foi possível reabrir a saída: {ex.Message}");
            return false;
        }

        return true;
    }

    /// <summary>
    /// Escreve uma linha no console. Se a saída estiver fechada (o pipe do
    /// terminal foi interrompido), o instalador não pode quebrar por causa disso.
    /// </summary>
    public static void Escrever(string texto)
    {
        try
        {
            Console.Out.WriteLine(texto);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[instalador] não foi possível escrever no console: {ex.Message}");
        }
    }

    /// <summary>Escreve uma linha de erro no console, sem quebrar o instalador.</summary>
    public static void EscreverErro(string texto)
    {
        try
        {
            Console.Error.WriteLine(texto);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[instalador] não foi possível escrever no console: {ex.Message}");
        }
    }

    /// <summary>
    /// UTF-8 sem BOM: é o que a captura de saída (pipe, arquivo, terminal
    /// moderno) espera. O BOM no começo da saída viraria caractere estranho.
    /// </summary>
    private static Encoding Utf8SemBom() => new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    /// <summary>
    /// Codificação do console em que o instalador está escrevendo.
    ///
    /// Se o console é UTF-8 (padrão do Windows Terminal e do console novo), o
    /// texto sai certo. Se for um console legado (850), o texto é escrito na
    /// codificação dele, que é o que o console sabe mostrar.
    /// </summary>
    private static Encoding CodificacaoDoConsole()
    {
        try
        {
            int codepage = GetConsoleOutputCP();
            if (codepage == CodepageUnicode) return Utf8SemBom();
            if (codepage > 0) return Encoding.GetEncoding(codepage);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[instalador] console sem codificação conhecida: {ex.Message}");
        }

        return Utf8SemBom();
    }
}
