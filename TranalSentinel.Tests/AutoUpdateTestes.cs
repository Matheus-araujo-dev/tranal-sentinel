using System.IO;
using Xunit;

namespace TranalSentinel.Tests;

/// <summary>
/// Testes do núcleo da atualização automática.
///
/// O que é testado aqui é a parte pura: ler a versão de um texto e montar a release a partir do
/// JSON do GitHub. Nenhum teste fala com a rede e nenhum mexe no disco.
/// </summary>
public class AutoUpdateTestes
{
    // JSON sintético no formato da API do GitHub (releases/latest).
    private const string JsonComExe = """
        {
          "tag_name": "v0.17.0",
          "name": "TRANAL SENTINEL 0.17.0",
          "html_url": "https://github.com/Matheus-araujo-dev/tranal-sentinel/releases/tag/v0.17.0",
          "assets": [
            { "name": "TranalSentinel.zip", "browser_download_url": "https://exemplo/TranalSentinel.zip", "size": 100 },
            { "name": "TranalSentinel.exe", "browser_download_url": "https://exemplo/TranalSentinel.exe", "size": 123456 }
          ]
        }
        """;

    private const string JsonComOutroExe = """
        {
          "tag_name": "v0.17.0",
          "name": "TRANAL SENTINEL 0.17.0",
          "html_url": "https://github.com/Matheus-araujo-dev/tranal-sentinel/releases/tag/v0.17.0",
          "assets": [
            { "name": "TranalSentinel.zip", "browser_download_url": "https://exemplo/TranalSentinel.zip", "size": 100 },
            { "name": "OutraCoisa.exe", "browser_download_url": "https://exemplo/OutraCoisa.exe", "size": 222 }
          ]
        }
        """;

    private const string JsonSemExe = """
        {
          "tag_name": "v0.17.0",
          "name": "TRANAL SENTINEL 0.17.0",
          "html_url": "https://github.com/Matheus-araujo-dev/tranal-sentinel/releases/tag/v0.17.0",
          "assets": [
            { "name": "TranalSentinel.zip", "browser_download_url": "https://exemplo/TranalSentinel.zip", "size": 100 },
            { "name": "leia-me.txt", "browser_download_url": "https://exemplo/leia-me.txt", "size": 10 }
          ]
        }
        """;

    // Outro .exe vem antes do executável do app: o nome exato tem que ganhar mesmo assim.
    private const string JsonComExeNoFinal = """
        {
          "tag_name": "v0.17.0",
          "name": "TRANAL SENTINEL 0.17.0",
          "html_url": "https://github.com/Matheus-araujo-dev/tranal-sentinel/releases/tag/v0.17.0",
          "assets": [
            { "name": "OutraCoisa.exe", "browser_download_url": "https://exemplo/OutraCoisa.exe", "size": 222 },
            { "name": "TranalSentinel.exe", "browser_download_url": "https://exemplo/TranalSentinel.exe", "size": 123456 }
          ]
        }
        """;

    private const string JsonSemTag = """
        {
          "name": "TRANAL SENTINEL",
          "html_url": "https://github.com/Matheus-araujo-dev/tranal-sentinel",
          "assets": []
        }
        """;

    // ---------------- Versao.Tentar ----------------

    [Theory]
    [InlineData("0.16.0", 0, 16, 0)]
    [InlineData("v0.16.0", 0, 16, 0)]
    [InlineData("0.16", 0, 16, 0)]
    [InlineData("1.2.3.4", 1, 2, 3)]   // usa só as três primeiras
    [InlineData("V2.0.1", 2, 0, 1)]
    public void Tentar_LeAVersaoDoTexto(string texto, int maior, int menor, int correcao)
    {
        var versao = Versao.Tentar(texto);

        Assert.NotNull(versao);
        Assert.Equal(new Versao(maior, menor, correcao), versao);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("1.x.3")]
    [InlineData("-1.0.0")]
    [InlineData(null)]
    public void Tentar_DevolveNullQuandoNaoEVersao(string? texto)
    {
        Assert.Null(Versao.Tentar(texto));
    }

    [Fact]
    public void Completa_EToStringMostramAsTresPartes()
    {
        var versao = new Versao(0, 16, 0);

        Assert.Equal("0.16.0", versao.Completa);
        Assert.Equal("0.16.0", versao.ToString());
    }

    // ---------------- Comparação de versões ----------------

    [Fact]
    public void CompareTo_MesmaVersaoDaZero()
    {
        Assert.Equal(0, new Versao(0, 16, 0).CompareTo(new Versao(0, 16, 0)));
    }

    [Fact]
    public void CompareTo_ComNuloDaMenosUm()
    {
        Assert.Equal(-1, new Versao(0, 16, 0).CompareTo(null));
    }

    [Theory]
    [InlineData(0, 16, 1)]   // correção maior
    [InlineData(0, 17, 0)]   // menor maior
    [InlineData(1, 0, 0)]    // maior maior
    public void CompareTo_VersaoMaiorVemDepois(int maior, int menor, int correcao)
    {
        Assert.True(new Versao(maior, menor, correcao).CompareTo(new Versao(0, 16, 0)) > 0);
    }

    [Theory]
    [InlineData(0, 15, 0)]   // menor menor
    [InlineData(0, 0, 0)]    // maior menor
    public void CompareTo_VersaoMenorVemAntes(int maior, int menor, int correcao)
    {
        Assert.True(new Versao(maior, menor, correcao).CompareTo(new Versao(0, 16, 0)) < 0);
    }

    [Theory]
    [InlineData(0, 16, 0, false)]   // mesma versão não é versão nova
    [InlineData(0, 16, 1, true)]    // correção maior
    [InlineData(0, 17, 0, true)]    // menor maior
    [InlineData(1, 0, 0, true)]     // maior maior
    [InlineData(0, 15, 9, false)]   // menor menor
    [InlineData(0, 0, 0, false)]    // maior menor
    public void EhMaisNova_ComparaPartePorParte(int maior, int menor, int correcao, bool esperado)
    {
        Assert.Equal(esperado, AutoUpdate.EhMaisNova(new Versao(maior, menor, correcao), new Versao(0, 16, 0)));
    }

    // ---------------- ExtrairRelease ----------------

    [Fact]
    public void ExtrairRelease_EscolheOAssetComONomeExato()
    {
        var release = AutoUpdate.ExtrairRelease(JsonComExe, "TranalSentinel.exe");

        Assert.NotNull(release);
        Assert.Equal("v0.17.0", release!.Tag);
        Assert.Equal("TRANAL SENTINEL 0.17.0", release.Titulo);
        Assert.Equal("https://github.com/Matheus-araujo-dev/tranal-sentinel/releases/tag/v0.17.0", release.UrlPagina);
        Assert.Equal("TranalSentinel.exe", release.NomeAsset);
        Assert.Equal("https://exemplo/TranalSentinel.exe", release.UrlAsset);
        Assert.Equal(123456, release.Tamanho);
    }

    [Fact]
    public void ExtrairRelease_NomeDoExeNaoBateMasTemOutroExe()
    {
        var release = AutoUpdate.ExtrairRelease(JsonComOutroExe, "TranalSentinel.exe");

        Assert.NotNull(release);
        Assert.Equal("OutraCoisa.exe", release!.NomeAsset);
        Assert.Equal("https://exemplo/OutraCoisa.exe", release.UrlAsset);
        Assert.Equal(222, release.Tamanho);
    }

    [Fact]
    public void ExtrairRelease_NomeExatoGanhaMesmoDepoisDeOutroExe()
    {
        var release = AutoUpdate.ExtrairRelease(JsonComExeNoFinal, "TranalSentinel.exe");

        Assert.NotNull(release);
        Assert.Equal("TranalSentinel.exe", release!.NomeAsset);
        Assert.Equal(123456, release.Tamanho);
    }

    [Fact]
    public void ExtrairRelease_NaoBateEmCaixaDiferente()
    {
        var release = AutoUpdate.ExtrairRelease(JsonComExe, "tranalSentinel.EXE");

        Assert.NotNull(release);
        Assert.Equal("TranalSentinel.exe", release!.NomeAsset);
    }

    [Fact]
    public void ExtrairRelease_SemNenhumExeDevolveNulo()
    {
        Assert.Null(AutoUpdate.ExtrairRelease(JsonSemExe, "TranalSentinel.exe"));
    }

    [Fact]
    public void ExtrairRelease_JsonInvalidoDevolveNulo()
    {
        Assert.Null(AutoUpdate.ExtrairRelease("{ isto não é json", "TranalSentinel.exe"));
    }

    [Fact]
    public void ExtrairRelease_JsonVazioDevolveNulo()
    {
        Assert.Null(AutoUpdate.ExtrairRelease("", "TranalSentinel.exe"));
    }

    [Fact]
    public void ExtrairRelease_SemTagDevolveNulo()
    {
        Assert.Null(AutoUpdate.ExtrairRelease(JsonSemTag, "TranalSentinel.exe"));
    }

    [Fact]
    public void ExtrairRelease_TagComPrefixoVVirouVersao()
    {
        var release = AutoUpdate.ExtrairRelease(JsonComExe, "TranalSentinel.exe");

        Assert.NotNull(release);
        Assert.Equal(new Versao(0, 17, 0), Versao.Tentar(release!.Tag));
    }

    // ---------------- Montagem do resultado ----------------

    [Fact]
    public void Resultado_ComReleaseAcimaDaAtualMarcaQueHaVersaoNova()
    {
        var release = AutoUpdate.ExtrairRelease(JsonComExe, "TranalSentinel.exe")!;
        var versao = Versao.Tentar(release.Tag)!;
        var atual = new Versao(0, 16, 0);

        var resultado = new ResultadoAtualizacao(AutoUpdate.EhMaisNova(versao, atual), atual, versao, release, null);

        Assert.True(resultado.HaVersaoNova);
        Assert.Equal(atual, resultado.VersaoAtual);
        Assert.Equal(versao, resultado.VersaoNova);
        Assert.Same(release, resultado.Release);
        Assert.Null(resultado.Erro);
    }

    [Fact]
    public void Resultado_ComReleaseIgualNaAtualNaoMarcaVersaoNova()
    {
        var release = AutoUpdate.ExtrairRelease(JsonComExe, "TranalSentinel.exe")!;
        var atual = new Versao(0, 17, 0);

        var resultado = new ResultadoAtualizacao(AutoUpdate.EhMaisNova(new Versao(0, 17, 0), atual), atual, new Versao(0, 17, 0), release, null);

        Assert.False(resultado.HaVersaoNova);
        Assert.Null(resultado.Erro);
    }

    // ---------------- Contrato do app ----------------

    [Fact]
    public void RepositorioPadrao_EOProjectoNoGitHub()
    {
        Assert.Equal("Matheus-araujo-dev/tranal-sentinel", AutoUpdate.RepositorioPadrao);
    }

    [Fact]
    public void VersaoDoApp_VemDoAssemblyDoApp()
    {
        var doAssembly = typeof(AutoUpdate).Assembly.GetName().Version!;

        Assert.Equal(new Versao(doAssembly.Major, doAssembly.Minor, Math.Max(doAssembly.Build, 0)), AutoUpdate.VersaoDoApp);
    }

    [Fact]
    public void ArgumentoDeAtualizacao_EEscapeDoArquivoNovo()
    {
        Assert.Equal("--atualizar", AutoUpdate.ArgumentoAtualizacao);
        Assert.Equal("TranalSentinel.exe.new", AutoUpdate.NomeArquivoNovo);
    }

    [Fact]
    public void PastaUpdate_FicaNoLocalAppData()
    {
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        Assert.StartsWith(local, AutoUpdate.PastaUpdate, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(Path.Combine("Tranal Sentinel", "update", "TranalSentinel.exe.new"),
                         AutoUpdate.CaminhoArquivoNovo, StringComparison.OrdinalIgnoreCase);
    }
}
