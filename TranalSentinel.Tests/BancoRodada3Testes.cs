using System.IO;
using Xunit;

namespace TranalSentinel.Tests;

/// <summary>
/// Testes das correções da terceira revisão adversarial do banco de dados.
///
/// <para>
/// Cada teste prende uma falha que a revisão encontrou: a migração que
/// gravava a versão do esquema com a leitura pela metade, o reparo que
/// deixava o arquivo novo órfão na pasta, o reparo que não gravava a
/// versão no banco novo, o aviso que sumia no "ok" da abertura. Sem
/// teste, a mesma falha volta na próxima mudança.
/// </para>
///
/// <para>
/// A classe fica na coleção "Banco": o banco, as pastas do CSV antigo e a
/// trava de instância única são estado global, e o xUnit roda as classes
/// da mesma coleção em série.
/// </para>
/// </summary>
[Collection("Banco")]
public class BancoRodada3Testes : IDisposable
{
    private string? _pastaBanco;
    private string? _pastaLegada;

    public void Dispose()
    {
        BancoDeTeste.Restaurar();

        foreach (string? pasta in new[] { _pastaBanco, _pastaLegada })
        {
            if (pasta == null) continue;
            try
            {
                if (Directory.Exists(pasta)) Directory.Delete(pasta, true);
            }
            catch
            {
                // A pasta é temporária: se o Windows ainda estiver segurando o
                // arquivo, ela some sozinha depois.
            }
        }
    }

    private void UsarPastasTemporarias()
    {
        (_pastaBanco, _pastaLegada) = BancoDeTeste.Isolar();
    }

    private static AmostraSensor Amostra(DateTime momento) => new(
        momento,
        78.5f, 43f, 92.4f, 3200f,
        71f, 100f, 2010f, 2048f,
        87.4f, 12.5f, 0.3f);

    // ------------------------------------------------------------------ leitura pela metade

    [Fact]
    public void MigracaoComOCsvTrancado_NaoGravaAVersaoDoEsquema()
    {
        UsarPastasTemporarias();

        Directory.CreateDirectory(_pastaLegada!);
        File.WriteAllLines(LegadoCsv.ArquivoSensores, new[]
        {
            LegadoCsv.MontarLinhaAmostra(Amostra(DateTime.Today.AddHours(10)))
        });

        // O antivírus com o arquivo aberto: a leitura do CSV falha no
        // começo. A versão do esquema diz "o CSV já entrou" — gravá-la
        // com a leitura pela metade faria o resto do CSV ficar órfão
        // para sempre, sem aviso.
        using (var trava = new FileStream(LegadoCsv.ArquivoSensores, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Banco.Ver();

            var linhas = Banco.Instancia!.Consultar("PRAGMA user_version");

            Assert.Single(linhas);
            Assert.Equal(0, linhas[0].Inteiro(linhas[0].Colunas[0]));
        }
    }

    [Fact]
    public void MigracaoComOCsvTrancado_DeixaOAvisoVisivel()
    {
        UsarPastasTemporarias();

        Directory.CreateDirectory(_pastaLegada!);
        File.WriteAllLines(LegadoCsv.ArquivoSensores, new[]
        {
            LegadoCsv.MontarLinhaAmostra(Amostra(DateTime.Today.AddHours(10)))
        });

        using (var trava = new FileStream(LegadoCsv.ArquivoSensores, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var retrato = Banco.Ver();

            // O banco está de pé (o estado é "ok"), e mesmo assim o aviso
            // precisa aparecer: o estado e o aviso são coisas diferentes.
            // Quando os dois moravam no mesmo lugar, o "ok" da abertura
            // apagava a falha da migração duas linhas depois.
            Assert.Equal("ok", retrato.Estado);
            Assert.Contains("parou no meio", retrato.Aviso);
        }
    }

    [Fact]
    public void MigracaoCompleta_NaoDeixaAviso()
    {
        UsarPastasTemporarias();

        Directory.CreateDirectory(_pastaLegada!);
        File.WriteAllLines(LegadoCsv.ArquivoSensores, new[]
        {
            LegadoCsv.MontarLinhaAmostra(Amostra(DateTime.Today.AddHours(10)))
        });

        var retrato = Banco.Ver();

        // Tudo entrou: nada a avisar. Um aviso falso é ruído — e faz o
        // usuário desconfiar do aviso verdadeiro.
        Assert.Equal("ok", retrato.Estado);
        Assert.Equal("", retrato.Aviso);
    }

    // ------------------------------------------------------------------ reparo

    [Fact]
    public void ReparoQueRecuperouLinhas_GravaAVersaoDoEsquemaNoBancoNovo()
    {
        UsarPastasTemporarias();

        for (int i = 0; i < 300; i++)
            Banco.GravarAmostra(Amostra(DateTime.Today.AddMinutes(i)));

        Banco.GravarUso(new TrocaUso(DateTime.Today.AddHours(10), "chrome", "Painel"));
        Banco.Fechar();

        BancoCorrupcao.CorromperPaginaLegivel(Banco.Caminho, 300, 1);

        var resultado = Banco.Reparar();

        Assert.True(resultado.Sucesso, resultado.Relato);
        Assert.Equal(300, resultado.AmostrasRecuperadas);

        // O banco novo guarda a versão do esquema do banco velho: sem
        // ela, a próxima abertura acharia que o CSV nunca entrou e o
        // importaria de novo — por cima do histórico recuperado.
        Assert.Equal(1, VersaoDoEsquemaDoArquivo());
    }

    [Fact]
    public void ReparoQueNaoRecuperouNada_NaoGravaAVersaoDoEsquema()
    {
        UsarPastasTemporarias();

        for (int i = 0; i < 30; i++)
            Banco.GravarAmostra(Amostra(DateTime.Today.AddMinutes(i)));

        Banco.Fechar();
        BancoCorrupcao.CorromperCabecalho(Banco.Caminho);

        var resultado = Banco.Reparar();

        Assert.True(resultado.Sucesso, resultado.Relato);
        Assert.Equal(0, resultado.AmostrasRecuperadas);

        // Nada foi recuperado: o banco novo fica sem a marca de "o CSV já
        // entrou" — o histórico antigo volta do CSV na próxima abertura,
        // em vez de ficar para sempre fora do app.
        Assert.Equal(0, VersaoDoEsquemaDoArquivo());
    }

    [Fact]
    public void ReparoCancelado_NaoDeixaOBancoNovoOrfaoNaPasta()
    {
        UsarPastasTemporarias();

        for (int i = 0; i < 300; i++)
            Banco.GravarAmostra(Amostra(DateTime.Today.AddMinutes(i)));

        Banco.GravarUso(new TrocaUso(DateTime.Today.AddHours(10), "chrome", "Painel"));
        Banco.Fechar();

        BancoCorrupcao.CorromperPaginaLegivel(Banco.Caminho, 300, 1);

        // O app aberto: o Windows segura o arquivo do banco, e a troca do
        // arquivo no lugar falha no último passo.
        using (var segura = Sqlite.Abrir(Banco.Caminho))
        {
            Assert.NotNull(segura);

            var resultado = Banco.Reparar();

            Assert.False(resultado.Sucesso);

            // A falha é a da troca do arquivo (e não uma recusa antes de
            // começar): o reparo chegou até o fim e foi o Windows que
            // segurou o arquivo.
            Assert.Contains("não foi possível trocar o arquivo do banco de dados", resultado.Relato, StringComparison.OrdinalIgnoreCase);
        }

        // O banco novo não entrou no lugar: ele não serve para nada aqui
        // e ocupa dezenas de megabytes na pasta do histórico.
        Assert.False(File.Exists(Banco.Caminho + ".reparo"));
        Assert.False(File.Exists(Banco.Caminho + ".reparo-wal"));
    }

    [Fact]
    public void ReparoCancelado_DeixaOBancoAntigoNoLugar()
    {
        UsarPastasTemporarias();

        for (int i = 0; i < 300; i++)
            Banco.GravarAmostra(Amostra(DateTime.Today.AddMinutes(i)));

        Banco.GravarUso(new TrocaUso(DateTime.Today.AddHours(10), "chrome", "Painel"));
        Banco.Fechar();

        BancoCorrupcao.CorromperPaginaLegivel(Banco.Caminho, 300, 1);

        using (var segura = Sqlite.Abrir(Banco.Caminho))
        {
            Assert.NotNull(segura);

            var resultado = Banco.Reparar();

            Assert.False(resultado.Sucesso);
            Assert.Contains("foi mantido como está", resultado.Relato);
        }

        // O banco velho continua no lugar: é melhor o banco estragado no
        // lugar do que nenhum.
        Assert.True(File.Exists(Banco.Caminho));
    }

    // ------------------------------------------------------------------ auxiliares

    /// <summary>
    /// A versão do esquema gravada no arquivo do banco, lida direto do
    /// arquivo (sem passar pelo app, que abriria o banco e refaria a
    /// migração).
    /// </summary>
    private static int VersaoDoEsquemaDoArquivo()
    {
        using var banco = Sqlite.Abrir(Banco.Caminho);

        Assert.NotNull(banco);

        var linhas = banco!.Consultar("PRAGMA user_version");

        Assert.Single(linhas);
        return (int)linhas[0].Inteiro(linhas[0].Colunas[0]);
    }
}
