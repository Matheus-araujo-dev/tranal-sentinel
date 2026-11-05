using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;

namespace TranalSentinel;

/// <summary>
/// Versão de três partes (major.minor.patch) do app.
/// </summary>
public sealed record Versao(int Maior, int Menor, int Correcao) : IComparable<Versao>
{
    /// <summary>
    /// Tenta ler uma versão de um texto. Aceita "0.16.0", "v0.16.0", "0.16" (vira 0.16.0)
    /// e "1.2.3.4" (usa as três primeiras). Devolve null quando o texto não é uma versão.
    /// </summary>
    public static Versao? Tentar(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return null;

        string limpo = texto.Trim();
        if (limpo.Length > 0 && (limpo[0] == 'v' || limpo[0] == 'V')) limpo = limpo[1..].Trim();

        string[] partes = limpo.Split('.', StringSplitOptions.TrimEntries);
        if (partes.Length == 0) return null;

        int[] numeros = new int[3];
        for (int i = 0; i < 3; i++)
        {
            if (i >= partes.Length) break; // "0.16" vira 0.16.0
            if (!int.TryParse(partes[i], NumberStyles.None, CultureInfo.InvariantCulture, out numeros[i])) return null;
        }

        return new Versao(numeros[0], numeros[1], numeros[2]);
    }

    /// <summary>Versão no formato "0.16.0".</summary>
    public string Completa => $"{Maior}.{Menor}.{Correcao}";

    public override string ToString() => Completa;

    /// <summary>Compara parte a parte: maior, depois menor, depois correção. Null é a menor versão.</summary>
    public int CompareTo(Versao? outra)
    {
        if (outra is null) return -1;
        int c = Maior.CompareTo(outra.Maior);
        if (c != 0) return c;
        c = Menor.CompareTo(outra.Menor);
        if (c != 0) return c;
        return Correcao.CompareTo(outra.Correcao);
    }
}

/// <summary>Release do GitHub com o asset do executável que o app sabe substituir.</summary>
public sealed record ReleaseGitHub(string Tag, string Titulo, string UrlPagina, string NomeAsset, string UrlAsset, long Tamanho);

/// <summary>Resultado de uma checagem de atualização.</summary>
public sealed record ResultadoAtualizacao(bool HaVersaoNova, Versao VersaoAtual, Versao? VersaoNova, ReleaseGitHub? Release, string? Erro);

/// <summary>Resultado do download do executável novo.</summary>
public sealed record DownloadConcluido(string CaminhoArquivo, long TamanhoEsperado, long TamanhoBaixado);

/// <summary>
/// Núcleo da atualização automática: lê a última release do GitHub, diz se existe versão
/// nova e baixa o executável novo para o disco.
///
/// Não tem interface e não mexe no app principal: a troca do executável é concluída por um
/// processo separado (a flag <see cref="ArgumentoAtualizacao"/>), quando o app atual fechar.
/// </summary>
public static class AutoUpdate
{
    /// <summary>Repositório do projeto no GitHub (de onde saem as releases).</summary>
    public const string RepositorioPadrao = "Matheus-araujo-dev/tranal-sentinel";

    /// <summary>Nome do executável do app (é o asset procurado na release).</summary>
    public const string NomeExecutavelPadrao = "TranalSentinel.exe";

    /// <summary>Flag que o app principal entende: troca o executável quando o app fechar.</summary>
    public const string ArgumentoAtualizacao = "--atualizar";

    /// <summary>Nome do arquivo baixado enquanto a troca não acontece.</summary>
    public const string NomeArquivoNovo = "TranalSentinel.exe.new";

    /// <summary>Nome do log da atualização (na mesma pasta do executável novo).</summary>
    public const string NomeArquivoLog = "atualizacao.log";

    private const string UserAgent = "TranalSentinel";
    private static readonly TimeSpan TempoLimiteChecagem = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan TempoLimiteDownload = TimeSpan.FromMinutes(15);

    /// <summary>Pasta do executável novo: %LOCALAPPDATA%\Tranal Sentinel\update.</summary>
    public static string PastaUpdate => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Tranal Sentinel", "update");

    /// <summary>Caminho do executável novo: %LOCALAPPDATA%\Tranal Sentinel\update\TranalSentinel.exe.new.</summary>
    public static string CaminhoArquivoNovo => Path.Combine(PastaUpdate, NomeArquivoNovo);

    /// <summary>Versão do app em execução (as três primeiras partes da versão do assembly).</summary>
    public static Versao VersaoDoApp { get; } = LerVersaoDoApp();

    /// <summary>Diz se a versão nova é realmente mais nova que a atual.</summary>
    public static bool EhMaisNova(Versao nova, Versao atual) => nova.CompareTo(atual) > 0;

    /// <summary>
    /// Lê o JSON da release do GitHub e devolve o asset do executável (sem rede).
    /// Escolhe o asset com o nome exato; sem ele, o primeiro ".exe"; sem nenhum ".exe", null.
    /// </summary>
    public static ReleaseGitHub? ExtrairRelease(string json, string nomeExe)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;

        try
        {
            using var doc = JsonDocument.Parse(json);
            var raiz = doc.RootElement;
            if (raiz.ValueKind != JsonValueKind.Object) return null;

            string tag = Texto(raiz, "tag_name");
            if (tag.Length == 0) return null;

            string titulo = Texto(raiz, "name");
            if (titulo.Length == 0) titulo = tag;
            string urlPagina = Texto(raiz, "html_url");

            if (!raiz.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array) return null;

            ReleaseGitHub? primeiroExe = null;
            foreach (var asset in assets.EnumerateArray())
            {
                if (asset.ValueKind != JsonValueKind.Object) continue;

                string nome = Texto(asset, "name");
                if (nome.Length == 0) continue;

                string url = Texto(asset, "browser_download_url");
                long tamanho = asset.TryGetProperty("size", out var s) && s.ValueKind == JsonValueKind.Number && s.TryGetInt64(out long v)
                    ? v
                    : 0;

                // O nome exato do executável sempre ganha.
                if (string.Equals(nome, nomeExe, StringComparison.OrdinalIgnoreCase))
                    return new ReleaseGitHub(tag, titulo, urlPagina, nome, url, tamanho);

                // Senão, o primeiro .exe da release.
                if (primeiroExe is null && nome.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    primeiroExe = new ReleaseGitHub(tag, titulo, urlPagina, nome, url, tamanho);
            }

            return primeiroExe;
        }
        catch (JsonException) { return null; }
        catch (Exception) { return null; }
    }

    /// <summary>
    /// Consulta a última release do GitHub e diz se existe versão nova.
    /// Nunca derruba o app: qualquer erro de rede volta em <see cref="ResultadoAtualizacao.Erro"/>.
    /// </summary>
    public static ResultadoAtualizacao Checar(string? repositorio = null, HttpClient? cliente = null, CancellationToken ct = default)
    {
        string repo = string.IsNullOrWhiteSpace(repositorio) ? RepositorioPadrao : repositorio.Trim();
        bool clienteProprio = cliente is null;
        HttpClient http = cliente ?? new HttpClient();

        try
        {
            using var limite = CancellationTokenSource.CreateLinkedTokenSource(ct);
            limite.CancelAfter(TempoLimiteChecagem);

            using var req = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{repo}/releases/latest");
            req.Headers.UserAgent.ParseAdd(UserAgent); // a API do GitHub recusa requisição sem User-Agent
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

            using var resp = http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, limite.Token).GetAwaiter().GetResult();

            // Repositório sem release ainda não é erro: só não há versão nova.
            if (resp.StatusCode == HttpStatusCode.NotFound)
                return new ResultadoAtualizacao(false, VersaoDoApp, null, null, null);

            if (!resp.IsSuccessStatusCode)
                return new ResultadoAtualizacao(false, VersaoDoApp, null, null, MensagemDeHttp(resp.StatusCode));

            string json = resp.Content.ReadAsStringAsync(limite.Token).GetAwaiter().GetResult();

            var release = ExtrairRelease(json, NomeExecutavelPadrao);
            if (release is null) return new ResultadoAtualizacao(false, VersaoDoApp, null, null, null);

            var versao = Versao.Tentar(release.Tag);
            if (versao is null) return new ResultadoAtualizacao(false, VersaoDoApp, null, null, null);

            if (!EhMaisNova(versao, VersaoDoApp))
                return new ResultadoAtualizacao(false, VersaoDoApp, versao, release, null);

            return new ResultadoAtualizacao(true, VersaoDoApp, versao, release, null);
        }
        catch (Exception ex)
        {
            return new ResultadoAtualizacao(false, VersaoDoApp, null, null, MensagemDeErro(ex));
        }
        finally
        {
            if (clienteProprio) http.Dispose();
        }
    }

    /// <summary>
    /// Baixa o asset da release para o disco e devolve o que foi gravado.
    /// O progresso vai em bytes baixados.
    /// </summary>
    public static async Task<DownloadConcluido> BaixarAsync(ReleaseGitHub release, string caminhoDestino,
                                                            IProgress<long>? progresso = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(release);

        string? pasta = Path.GetDirectoryName(caminhoDestino);
        if (!string.IsNullOrEmpty(pasta)) Directory.CreateDirectory(pasta);

        using var http = new HttpClient { Timeout = TempoLimiteDownload };
        http.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);

        using var resp = await http.GetAsync(release.UrlAsset, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();

        await using var origem = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        await using var destino = new FileStream(caminhoDestino, FileMode.Create, FileAccess.Write, FileShare.None, 64 * 1024, useAsync: true);

        byte[] buffer = new byte[64 * 1024];
        long lidos = 0;
        int n;
        while ((n = await origem.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
        {
            await destino.WriteAsync(buffer.AsMemory(0, n), ct).ConfigureAwait(false);
            lidos += n;
            progresso?.Report(lidos);
        }
        await destino.FlushAsync(ct).ConfigureAwait(false);

        return new DownloadConcluido(caminhoDestino, release.Tamanho, lidos);
    }

    /// <summary>
    /// Chama o próprio executável com a flag <see cref="ArgumentoAtualizacao"/> para concluir a
    /// troca quando o app fechar. O processo é separado: ele espera o app atual sair e troca
    /// o executável no lugar.
    /// </summary>
    public static bool IniciarTrocaDeExecutavel(string caminhoNovoExecutavel)
    {
        if (!File.Exists(caminhoNovoExecutavel)) return false;

        string exe = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName ?? "";
        if (exe.Length == 0) return false;

        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(caminhoNovoExecutavel) ?? PastaUpdate,
        };
        psi.ArgumentList.Add(ArgumentoAtualizacao);
        psi.ArgumentList.Add(caminhoNovoExecutavel);

        using var filho = Process.Start(psi);
        return filho is not null;
    }

    /// <summary>Registra uma linha no log da atualização (na pasta do executável novo).</summary>
    public static void RegistrarNoLog(string mensagem)
    {
        try
        {
            Directory.CreateDirectory(PastaUpdate);
            File.AppendAllText(Path.Combine(PastaUpdate, NomeArquivoLog),
                                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {mensagem}{Environment.NewLine}");
        }
        catch { }
    }

    // ---------------- auxiliares ----------------

    private static Versao LerVersaoDoApp()
    {
        Version? v = Assembly.GetExecutingAssembly().GetName().Version;
        if (v is null) return new Versao(0, 0, 0);
        return new Versao(Math.Max(v.Major, 0), Math.Max(v.Minor, 0), Math.Max(v.Build, 0));
    }

    private static string Texto(JsonElement elemento, string propriedade)
        => elemento.TryGetProperty(propriedade, out var v) && v.ValueKind == JsonValueKind.String ? (v.GetString() ?? "") : "";

    private static string MensagemDeHttp(HttpStatusCode status) => status switch
    {
        HttpStatusCode.Forbidden => "O GitHub recusou a consulta (limite de requisições ou acesso negado).",
        HttpStatusCode.Unauthorized => "O GitHub recusou a consulta (acesso negado).",
        HttpStatusCode.TooManyRequests => "O GitHub está limitando as consultas. Tente mais tarde.",
        _ => $"O GitHub respondeu {(int)status}.",
    };

    private static string MensagemDeErro(Exception ex) => ex switch
    {
        OperationCanceledException => "A verificação de atualização foi cancelada ou demorou demais.",
        HttpRequestException http => "Não foi possível falar com o GitHub: " + http.Message,
        _ => "Falha na verificação de atualização: " + ex.Message,
    };
}
