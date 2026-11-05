using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace ResetExplode;

/// <summary>
/// Processo filho da coleta de sensores (<c>--sensores-worker</c>).
///
/// <para>
/// É o mesmo executável, chamado com argumentos diferentes: abre o
/// LibreHardwareMonitor e grava as leituras num arquivo, que o app pai lê.
/// Se o driver da GPU devolver ponteiro inválido, o access violation mata
/// <b>este</b> processo — e só ele. O app pai percebe que o filho morreu e
/// o reinicia (sem a GPU, se foi ela a culpada).
/// </para>
///
/// <para>
/// A comunicação é por arquivo (e não por pipe) porque o app é um executável
/// de subsistema Windows, sem console: não tem stdout para escrever. A
/// escrita é atômica (grava num .tmp e renomeia), então o pai nunca lê um
/// arquivo pela metade.
/// </para>
/// </summary>
internal static class SensorWorker
{
    /// <summary>Nome do argumento que liga o modo de processo filho.</summary>
    public const string Argumento = "--sensores-worker";

    public static void Rodar(string caminhoLeituras, string caminhoPausa, int idPai, bool usarGpu)
    {
        using var coleta = new SensorColeta(usarGpu);
        try
        {
            coleta.Iniciar();
        }
        catch (Exception ex)
        {
            Gravar(caminhoLeituras, new SensorSnapshot(Array.Empty<SensorReading>(), new[] { "Falha ao abrir: " + ex.Message }, DateTime.Now));
            return;
        }

        while (true)
        {
            try
            {
                // Sem o app pai, este processo não serve para nada: sai em vez de
                // ficar órfão rodando sozinho.
                if (idPai > 0 && !PaiVivo(idPai)) return;

                // O pai pede pausa durante o benchmark de GPU (o driver não pode
                // ser contestado duas vezes ao mesmo tempo).
                if (!File.Exists(caminhoPausa))
                {
                    var snap = coleta.UltimoSnapshot;
                    if (snap != null) Gravar(caminhoLeituras, snap);
                }
            }
            catch
            {
                // Falha ao gravar não derruba o filho: ele tenta de novo no próximo ciclo.
            }
            Thread.Sleep(700);
        }
    }

    private static bool PaiVivo(int idPai)
    {
        try
        {
            using var p = Process.GetProcessById(idPai);
            return !p.HasExited;
        }
        catch (ArgumentException)
        {
            return false;   // o pai não existe mais
        }
        catch
        {
            return true;
        }
    }

    private static void Gravar(string caminho, SensorSnapshot snapshot)
    {
        string json = JsonSerializer.Serialize(snapshot);
        string temporario = caminho + ".tmp";
        File.WriteAllText(temporario, json);
        File.Move(temporario, caminho, true);   // atômico: o pai nunca lê pela metade
    }
}
