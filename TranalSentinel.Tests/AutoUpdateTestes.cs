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
    // JSON sintético no formato da API do GitHub (uma release).
    private const string JsonComExe = """
        {
          "tag_name": "v0.17.0",
          "name": "TRANAL SENTINEL 0.17.0",
          "html_url": "https://github.com/Matheus-araujo-dev/tranal-sentinel/releases/tag/v0.17.0",
          "assets": [
            { "name": "TranalSentinel.zip", "browser_download_url": "https://exemplo/TranalSentinel.zip", "size": 100 },
            { "name": "TranalSentinel-Setup.exe", "browser_download_url": "https://exemplo/TranalSentinel-Setup.exe", "size": 123456 }
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
            { "name": "TranalSentinel-Setup.exe", "browser_download_url": "https://exemplo/TranalSentinel-Setup.exe", "size": 123456 }
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

    // ---------------- ExtrairReleases (a lista de releases do repositorio) ----------------

    // O repositorio publica duas coisas: a release com tag de versao (v0.17) e
    // uma release "rolante" com tag "latest" (que nao e uma versao). A API
    // /releases/latest devolve sempre a rolante, por isso o app olha a lista.

    private const string JsonDaLista = """
        [
          {
            "tag_name": "latest",
            "name": "TRANAL SENTINEL (versao mais recente)",
            "html_url": "https://github.com/exemplo/releases/tag/latest",
            "assets": [
              { "name": "TranalSentinel-Setup.exe", "browser_download_url": "https://exemplo/TranalSentinel-Setup.exe", "size": 123456 }
            ]
          },
          {
            "tag_name": "v0.17.0",
            "name": "TRANAL SENTINEL 0.17.0",
            "html_url": "https://github.com/exemplo/releases/tag/v0.17.0",
            "assets": [
              { "name": "TranalSentinel-Setup.exe", "browser_download_url": "https://exemplo/TranalSentinel-Setup.exe", "size": 123456 }
            ]
          },
          {
            "tag_name": "v0.16.0",
            "name": "TRANAL SENTINEL 0.16.0",
            "html_url": "https://github.com/exemplo/releases/tag/v0.16.0",
            "assets": [
              { "name": "TranalSentinel-Setup.exe", "browser_download_url": "https://exemplo/TranalSentinel-Setup.exe", "size": 123456 }
            ]
          }
        ]
        """;

    [Fact]
    public void ExtrairReleases_LeTodasComExecutavel()
    {
        var releases = AutoUpdate.ExtrairReleases(JsonDaLista, "TranalSentinel-Setup.exe");

        Assert.Equal(3, releases.Count);
        Assert.Equal("latest", releases[0].Tag);
        Assert.Equal("v0.17.0", releases[1].Tag);
        Assert.Equal("v0.16.0", releases[2].Tag);
    }

    [Fact]
    public void ExtrairReleases_IgnoraReleaseSemExecutavel()
    {
        const string json = """
            [
              { "tag_name": "v0.17.0", "assets": [ { "name": "leia-me.txt", "browser_download_url": "x", "size": 1 } ] }
            ]
            """;

        Assert.Empty(AutoUpdate.ExtrairReleases(json, "TranalSentinel-Setup.exe"));
    }

    [Fact]
    public void ExtrairReleases_JsonInvalidoDevolveListaVazia()
    {
        Assert.Empty(AutoUpdate.ExtrairReleases("isto nao e json", "TranalSentinel-Setup.exe"));
        Assert.Empty(AutoUpdate.ExtrairReleases("", "TranalSentinel-Setup.exe"));
        Assert.Empty(AutoUpdate.ExtrairReleases("{}", "TranalSentinel-Setup.exe"));   // objeto, nao lista
    }

    [Fact]
    public void AEtiquetaLatestNaoEVersao()
    {
        // E o que faz a release "rolante" nao atrapalhar a comparacao.
        Assert.Null(Versao.Tentar("latest"));
        Assert.NotNull(Versao.Tentar("v0.17.0"));
    }

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
        var release = AutoUpdate.ExtrairRelease(JsonComExe, "TranalSentinel-Setup.exe");

        Assert.NotNull(release);
        Assert.Equal("v0.17.0", release!.Tag);
        Assert.Equal("TRANAL SENTINEL 0.17.0", release.Titulo);
        Assert.Equal("https://github.com/Matheus-araujo-dev/tranal-sentinel/releases/tag/v0.17.0", release.UrlPagina);
        Assert.Equal("TranalSentinel-Setup.exe", release.NomeAsset);
        Assert.Equal("https://exemplo/TranalSentinel-Setup.exe", release.UrlAsset);
        Assert.Equal(123456, release.Tamanho);
    }

    [Fact]
    public void ExtrairRelease_OutroExeNaoViraInstalador()
    {
        // O asset é rodado em modo silencioso: um .exe qualquer da release não pode ser
        // confundido com o instalador.
        Assert.Null(AutoUpdate.ExtrairRelease(JsonComOutroExe, "TranalSentinel-Setup.exe"));
    }

    [Fact]
    public void ExtrairRelease_ReleaseAntigaSoComOExecutavelFicaDeFora()
    {
        // As releases até a v0.36 têm o TranalSentinel.exe portátil e não têm o instalador.
        const string json = """
            {
              "tag_name": "v0.36.2",
              "assets": [ { "name": "TranalSentinel.exe", "browser_download_url": "https://exemplo/TranalSentinel.exe", "size": 5 } ]
            }
            """;

        Assert.Null(AutoUpdate.ExtrairRelease(json, AutoUpdate.NomeInstaladorPadrao));
    }

    [Fact]
    public void ExtrairRelease_NomeExatoGanhaMesmoDepoisDeOutroExe()
    {
        var release = AutoUpdate.ExtrairRelease(JsonComExeNoFinal, "TranalSentinel-Setup.exe");

        Assert.NotNull(release);
        Assert.Equal("TranalSentinel-Setup.exe", release!.NomeAsset);
        Assert.Equal(123456, release.Tamanho);
    }

    [Fact]
    public void ExtrairRelease_NaoBateEmCaixaDiferente()
    {
        var release = AutoUpdate.ExtrairRelease(JsonComExe, "tranalsentinel-SETUP.EXE");

        Assert.NotNull(release);
        Assert.Equal("TranalSentinel-Setup.exe", release!.NomeAsset);
    }

    [Fact]
    public void ExtrairRelease_SemNenhumExeDevolveNulo()
    {
        Assert.Null(AutoUpdate.ExtrairRelease(JsonSemExe, "TranalSentinel-Setup.exe"));
    }

    [Fact]
    public void ExtrairRelease_JsonInvalidoDevolveNulo()
    {
        Assert.Null(AutoUpdate.ExtrairRelease("{ isto não é json", "TranalSentinel-Setup.exe"));
    }

    [Fact]
    public void ExtrairRelease_JsonVazioDevolveNulo()
    {
        Assert.Null(AutoUpdate.ExtrairRelease("", "TranalSentinel-Setup.exe"));
    }

    [Fact]
    public void ExtrairRelease_SemTagDevolveNulo()
    {
        Assert.Null(AutoUpdate.ExtrairRelease(JsonSemTag, "TranalSentinel-Setup.exe"));
    }

    [Fact]
    public void ExtrairRelease_TagComPrefixoVVirouVersao()
    {
        var release = AutoUpdate.ExtrairRelease(JsonComExe, "TranalSentinel-Setup.exe");

        Assert.NotNull(release);
        Assert.Equal(new Versao(0, 17, 0), Versao.Tentar(release!.Tag));
    }

    // ---------------- Montagem do resultado ----------------

    [Fact]
    public void Resultado_ComReleaseAcimaDaAtualMarcaQueHaVersaoNova()
    {
        var release = AutoUpdate.ExtrairRelease(JsonComExe, "TranalSentinel-Setup.exe")!;
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
        var release = AutoUpdate.ExtrairRelease(JsonComExe, "TranalSentinel-Setup.exe")!;
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
    public void ContratoDoInstalador_SegueADecisaoDeDistribuicao()
    {
        // O nome é o que o publicar.yml publica e o .iss gera (OutputBaseFilename).
        Assert.Equal("TranalSentinel-Setup.exe", AutoUpdate.NomeInstaladorPadrao);
        Assert.Equal("/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS", AutoUpdate.ArgumentosInstalacaoSilenciosa);
        // A flag do atualizador antigo continua: é por ela que o app v0.36 chega a esta versão.
        Assert.Equal("--atualizar", AutoUpdate.ArgumentoAtualizacao);
    }

    [Theory]
    [InlineData(0, 37, 0, false, true)]    // versão nova
    [InlineData(0, 36, 3, false, false)]   // mesma versão, já migrado: nada a fazer
    [InlineData(0, 36, 3, true, true)]     // mesma versão, instalação antiga: migra agora
    [InlineData(0, 36, 2, true, false)]    // versão menor nunca é oferecida
    [InlineData(0, 36, 2, false, false)]
    public void PrecisaAtualizar_MigraAInstalacaoAntigaSemEsperarAProximaRelease(
        int maior, int menor, int correcao, bool instalacaoAntiga, bool esperado)
    {
        Assert.Equal(esperado, AutoUpdate.PrecisaAtualizar(new Versao(maior, menor, correcao), new Versao(0, 36, 3), instalacaoAntiga));
    }

    [Fact]
    public void PastaUpdate_FicaNoLocalAppData()
    {
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        Assert.StartsWith(local, AutoUpdate.PastaUpdate, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(Path.Combine("Tranal Sentinel", "update", "TranalSentinel-Setup.exe"),
                         AutoUpdate.CaminhoInstaladorBaixado, StringComparison.OrdinalIgnoreCase);
    }
}
