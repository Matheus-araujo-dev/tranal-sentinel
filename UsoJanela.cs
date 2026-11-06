using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace TranalSentinel;

/// <summary>
/// Uma mudança de janela em foco: o programa que passou para a frente.
///
/// <para>
/// É o evento mais simples que explica "o que a pessoa estava fazendo":
/// o nome do processo e o título da janela. Nada mais é coletado — sem
/// conteúdo de arquivo, sem digitação, sem tela.
/// </para>
/// </summary>
/// <param name="Momento">Quando a janela passou para a frente (hora local).</param>
/// <param name="Processo">O nome do processo (ex.: "chrome", "Code", "explorer").</param>
/// <param name="Titulo">O título da janela (pode vir vazio).</param>
public sealed record TrocaUso(DateTime Momento, string Processo, string Titulo);

/// <summary>
/// Lê a janela que está em frente na tela.
///
/// <para>
/// É somente leitura: o app pergunta ao Windows qual é a janela em foco e
/// a quem ela pertence. Nada é enviado a lugar nenhum — o histórico fica no
/// computador do dono.
/// </para>
///
/// <para>
/// Quando o Windows não responde (janela protegida, processo encerrado no
/// meio da leitura), devolve <c>null</c> em vez de throwing: a falta de uma
/// leitura não é motivo para derrubar o app.
/// </para>
/// </summary>
public static class UsoJanela
{
    /// <summary>
    /// O nome do app como o usuário conhece. É o que aparece no ranking
    /// quando o próprio TRANAL SENTINEL está em frente na tela.
    /// </summary>
    public const string NomeDoApp = "TRANAL SENTINEL";

    /// <summary>
    /// A janela em frente agora: o processo e o título. Devolve null quando
    /// não há janela legível.
    ///
    /// <para>
    /// A janela do próprio TRANAL SENTINEL também conta, com o nome do
    /// próprio app. Antes ela era ignorada — e o tempo em que a pessoa
    /// olhava o monitor acabava creditado no programa que estava em frente
    /// antes, inflando o ranking de um jeito que ninguém percebia. Agora o
    /// tempo do monitor fica com o monitor.
    /// </para>
    /// </summary>
    public static TrocaUso? LerEmDestaque(DateTime agora)
    {
        try
        {
            IntPtr janela = GetForegroundWindow();
            if (janela == IntPtr.Zero) return null;

            _ = GetWindowThreadProcessId(janela, out int processoId);
            if (processoId <= 0) return null;

            string titulo = LerTitulo(janela);

            // O próprio app entra no histórico com o nome que o usuário
            // conhece. O nome do executável ("TranalSentinel") não aparece
            // em lugar nenhum da tela.
            string processo = processoId == Environment.ProcessId ? NomeDoApp : LerProcesso(processoId);

            // Sem nome de processo não há como saber quem estava em frente.
            if (string.IsNullOrWhiteSpace(processo)) return null;

            return new TrocaUso(agora, processo.Trim(), titulo);
        }
        catch
        {
            // Janela protegida ou processo que morreu no meio da leitura: sem
            // leitura, sem drama.
            return null;
        }
    }

    /// <summary>O nome do processo (sem extensão). Vazio quando não dá para ler.</summary>
    private static string LerProcesso(int processoId)
    {
        try
        {
            using var processo = Process.GetProcessById(processoId);
            return processo.ProcessName;
        }
        catch
        {
            // Processo protegido ou já encerrado: o nome não é essencial, a
            // janela em frente é que importa.
            return "";
        }
    }

    /// <summary>O título da janela (string builder, sem alocar a cada chamada).</summary>
    private static string LerTitulo(IntPtr janela)
    {
        try
        {
            var buffer = new StringBuilder(512);
            int tamanho = GetWindowTextW(janela, buffer, buffer.Capacity);
            return tamanho > 0 ? buffer.ToString() : "";
        }
        catch
        {
            // Título de janela protegida não vem: fica sem título, e o
            // registro do programa segue valendo.
            return "";
        }
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern int GetWindowThreadProcessId(IntPtr janela, out int processoId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextW(IntPtr janela, StringBuilder texto, int maximo);
}
