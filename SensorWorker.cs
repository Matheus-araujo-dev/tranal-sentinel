using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace TranalSentinel;

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

    /// <summary>
    /// O ajuste do runtime do .NET para o processo de coleta, passado pelo
    /// ambiente quando o app pai o lança.
    ///
    /// <para>
    /// Vale <b>só</b> para o coletor: ele fica aberto o tempo todo, tem um heap
    /// pequeno e não roda nada que precise do JIT no máximo. O app pai fica
    /// com o padrão do runtime, porque o TRANAL BENCH e os stress tests medem
    /// a CPU com ele — mudar o JIT ou o GC lá mudaria as notas.
    /// </para>
    ///
    /// <para>
    /// Medido em 20/11/2026: com os três ajustes, o coletor caiu de ~24 MB
    /// para ~19 MB de memória privada em uso. Sem a coleta em segundo plano
    /// (<c>gcConcurrent=0</c>) o GC não reserva a estrutura da coleta
    /// concorrente; o orçamento da geração 0 (<c>GCgen0size</c>, em
    /// hexadecimal: 2 MB) vem do tamanho do cache L3 e, num Ryzen, deixava
    /// lixo acumular à toa; e sem o PGO dinâmico o JIT não guarda a versão
    /// instrumentada dos métodos.
    /// </para>
    /// </summary>
    internal static readonly IReadOnlyDictionary<string, string> AmbienteDoRuntime = new Dictionary<string, string>
    {
        ["DOTNET_gcConcurrent"] = "0",
        ["DOTNET_GCgen0size"] = "200000",
        ["DOTNET_TieredPGO"] = "0",
    };

    /// <summary>
    /// Se a linha de comando é a do processo de coleta, roda a coleta até o
    /// app pai sumir e devolve <c>true</c>; senão, devolve <c>false</c> e não
    /// faz nada. Chamado pelo <see cref="Programa"/> antes do WPF subir: este
    /// processo fica aberto o tempo todo, e não precisa de nada da interface.
    /// </summary>
    public static bool TentarRodar(string[] args)
    {
        int indice = Array.FindIndex(args, a => string.Equals(a, Argumento, StringComparison.OrdinalIgnoreCase));
        if (indice < 0) return false;

        string caminhoLeituras = indice + 1 < args.Length ? args[indice + 1] : "";
        string caminhoPausa = indice + 2 < args.Length ? args[indice + 2] : "";
        int idPai = indice + 3 < args.Length && int.TryParse(args[indice + 3], out int pid) ? pid : 0;
        bool semGpu = LinhaDeComando.Tem(args, "--sem-gpu");
        int intervalo = LinhaDeComando.Inteiro(args, "--intervalo", 1000);
        if (caminhoLeituras.Length > 0)
            Rodar(caminhoLeituras, caminhoPausa, idPai, !semGpu, intervalo);
        return true;
    }

    /// <summary>
    /// Abre a coleta e grava cada volta nova no arquivo de leituras, até o app
    /// pai sumir.
    /// </summary>
    public static void Rodar(string caminhoLeituras, string caminhoPausa, int idPai, bool usarGpu, int intervaloMs = 1000)
    {
        using var coleta = new SensorColeta(usarGpu, intervaloMs);
        try
        {
            coleta.Iniciar();
        }
        catch (Exception ex)
        {
            // A coleta inteira não começou: o motivo fica no snapshot, para o app
            // mostrar o que deu errado em vez de acusar o driver à toa.
            // O texto é o motivo puro — quem mostra ele é que coloca o contexto.
            Gravar(caminhoLeituras, new SensorSnapshot(
                Array.Empty<SensorReading>(), Array.Empty<string>(), DateTime.Now,
                ErroGeral: ex.GetType().Name + ": " + ex.Message));
            return;
        }

        SensorSnapshot? ultimoGravado = null;
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
                    // Só grava a volta nova: a coleta roda de 1 em 1 s (ou
                    // mais devagar, no perfil de economia) e este laço gira a
                    // cada 700 ms. Regravar a mesma volta fazia o app pai ler
                    // e desmontar o mesmo JSON duas ou três vezes à toa.
                    var snap = coleta.UltimoSnapshot;
                    if (snap != null && !ReferenceEquals(snap, ultimoGravado))
                    {
                        Gravar(caminhoLeituras, snap);
                        ultimoGravado = snap;
                    }
                }
            }
            catch (Exception ex)
            {
                // Falha ao gravar não derruba o filho: ele tenta de novo no próximo ciclo.
                // A falha fica registrada no snapshot, para o app mostrar o motivo
                // em vez de ficar sem sensores e sem explicação.
                // O arquivo agora tem o aviso de erro, e não a volta: a
                // próxima volta boa precisa ser gravada de novo.
                ultimoGravado = null;
                try
                {
                    // As últimas leituras continuam no lugar: elas ainda valem
                    // como último estado conhecido, e o aviso diz que a
                    // coleta parou.
                    var anterior = coleta.UltimoSnapshot;
                    Gravar(caminhoLeituras, new SensorSnapshot(
                        anterior?.Leituras ?? Array.Empty<SensorReading>(),
                        anterior?.Falhas ?? Array.Empty<string>(),
                        DateTime.Now,
                        ErroGeral: ex.GetType().Name + ": " + ex.Message));
                }
                catch
                {
                    // Nem o snapshot de emergência pôde ser gravado: o processo filho encerra, e o app percebe que a coleta parou.
                }
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
        // Direto para o arquivo, sem montar a string: o JSON de uma volta tem
        // ~60 mil caracteres (~120 KB como string), e uma string desse
        // tamanho vai para o heap de objetos grandes, que só é limpo na
        // coleta completa. Medido em 20/11/2026: ~95 delas mortas e paradas
        // no heap do app pai (~11 MB de lixo).
        string temporario = caminho + ".tmp";
        using (var arquivo = new FileStream(temporario, FileMode.Create, FileAccess.Write, FileShare.None, 16 * 1024))
            JsonSerializer.Serialize(arquivo, snapshot, SensorValor.OpcoesJson);
        File.Move(temporario, caminho, true);   // atômico: o pai nunca lê pela metade
    }
}
