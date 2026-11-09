using System.IO;
using Xunit;

namespace TranalSentinel.Tests;

/// <summary>
/// Testes do histórico de tráfego de internet no banco de dados.
///
/// <para>
/// O banco do tráfego é o espelho do total conhecido: o app grava o total
/// do dia, e a linha é reescrita a cada vez. Cada teste aqui prende uma
/// decisão que, errada, faz o dia mostrar o dobro do tráfego (somar em vez
/// de reescrever) ou apagar o que já estava salvo (gravar sem a base).
///
/// <para>
/// A classe fica na coleção "Banco": o banco é estado global, e o xUnit
/// roda as classes da mesma coleção em série.
/// </para>
/// </summary>
[Collection("Banco")]
public class TrafegoBancoTestes : IDisposable
{
    private string? _pastaBanco;
    private string? _pastaLegada;

    private static readonly DateTime Dia = new(2026, 11, 9, 0, 0, 0);

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

    // ------------------------------------------------------------------ gravação

    [Fact]
    public void GravarTrafego_EscreveOTotalDoDia()
    {
        UsarPastasTemporarias();

        var itens = new[]
        {
            new TrafegoPrograma("chrome", 1000, 2000),
            new TrafegoPrograma("spotify", 500, 0),
        };

        Assert.True(Banco.GravarTrafego(Dia, itens));

        var leitura = Banco.LerTrafego(Dia);

        Assert.True(leitura.Completa);
        Assert.Equal(2, leitura.Linhas.Count);

        var chrome = leitura.Linhas.First(l => l.Processo == "chrome");
        Assert.Equal(1000, chrome.Enviados);
        Assert.Equal(2000, chrome.Recebidos);
    }

    [Fact]
    public void GravarTrafego_ReescreveEmVezDeSomar()
    {
        UsarPastasTemporarias();

        // O app grava o total do dia, e não um pedaço novo dele: somar as
        // duas gravagens faria o dia mostrar o dobro do tráfego.
        Banco.GravarTrafego(Dia, new[] { new TrafegoPrograma("chrome", 1000, 0) });
        Banco.GravarTrafego(Dia, new[] { new TrafegoPrograma("chrome", 1500, 0) });

        var leitura = Banco.LerTrafego(Dia);

        var chrome = Assert.Single(leitura.Linhas);
        Assert.Equal(1500, chrome.Enviados);
    }

    [Fact]
    public void GravarTrafego_NaoDistingueMaiusculasDeMinusculas()
    {
        UsarPastasTemporarias();

        Banco.GravarTrafego(Dia, new[] { new TrafegoPrograma("Chrome", 1000, 0) });
        Banco.GravarTrafego(Dia, new[] { new TrafegoPrograma("chrome", 1500, 0) });

        var leitura = Banco.LerTrafego(Dia);

        // O "Chrome" e o "chrome" são o mesmo programa: duas linhas fariam
        // a tela mostrar o mesmo programa duas vezes.
        var linha = Assert.Single(leitura.Linhas);
        Assert.Equal(1500, linha.Enviados);
    }

    [Fact]
    public void GravarTrafego_NaoGuardaNumeroNegativo()
    {
        UsarPastasTemporarias();

        Banco.GravarTrafego(Dia, new[] { new TrafegoPrograma("chrome", -500, 100) });

        var linha = Assert.Single(Banco.LerTrafego(Dia).Linhas);
        Assert.Equal(0, linha.Enviados);
        Assert.Equal(100, linha.Recebidos);
    }

    [Fact]
    public void GravarTrafego_ComListaVazia_NaoApagaOQueEstavaSalvo()
    {
        UsarPastasTemporarias();

        Banco.GravarTrafego(Dia, new[] { new TrafegoPrograma("chrome", 1000, 0) });
        Banco.GravarTrafego(Dia, new List<TrafegoPrograma>());

        // Um dia sem tráfego não apaga o dia: o programa que usou a rede
        // antes continua no histórico.
        Assert.Single(Banco.LerTrafego(Dia).Linhas);
    }

    [Fact]
    public void GravarTrafego_EmDiasDiferentes_NaoMistura()
    {
        UsarPastasTemporarias();

        Banco.GravarTrafego(Dia, new[] { new TrafegoPrograma("chrome", 1000, 0) });
        Banco.GravarTrafego(Dia.AddDays(-1), new[] { new TrafegoPrograma("chrome", 2000, 0) });

        Assert.Equal(1000, Banco.LerTrafego(Dia).Linhas[0].Enviados);
        Assert.Equal(2000, Banco.LerTrafego(Dia.AddDays(-1)).Linhas[0].Enviados);
    }

    // ------------------------------------------------------------------ leitura

    [Fact]
    public void LerTrafego_DiaSemNada_ELeituraCompleta()
    {
        UsarPastasTemporarias();

        var leitura = Banco.LerTrafego(Dia);

        // Lista vazia com leitura completa é "não teve tráfego nesse dia" —
        // e não "não deu para ler".
        Assert.True(leitura.Completa);
        Assert.Empty(leitura.Linhas);
    }

    [Fact]
    public void LerTrafego_VemOrdenadoPeloMaiorConsumidor()
    {
        UsarPastasTemporarias();

        Banco.GravarTrafego(Dia, new[]
        {
            new TrafegoPrograma("chrome", 100, 0),
            new TrafegoPrograma("spotify", 9000, 0),
            new TrafegoPrograma("code", 500, 0),
        });

        var leitura = Banco.LerTrafego(Dia);

        Assert.Equal("spotify", leitura.Linhas[0].Processo);
        Assert.Equal("code", leitura.Linhas[1].Processo);
        Assert.Equal("chrome", leitura.Linhas[2].Processo);
    }

    [Fact]
    public void DiasComTrafego_DoMaisNovoParaOMaisAntigo()
    {
        UsarPastasTemporarias();

        Banco.GravarTrafego(Dia, new[] { new TrafegoPrograma("chrome", 100, 0) });
        Banco.GravarTrafego(Dia.AddDays(-2), new[] { new TrafegoPrograma("chrome", 100, 0) });
        Banco.GravarTrafego(Dia.AddDays(-1), new[] { new TrafegoPrograma("chrome", 100, 0) });

        var dias = Banco.DiasComTrafego();

        Assert.Equal(3, dias.Count);
        Assert.Equal(Dia, dias[0]);
        Assert.Equal(Dia.AddDays(-1), dias[1]);
        Assert.Equal(Dia.AddDays(-2), dias[2]);
    }

    // ------------------------------------------------------------------ poda

    [Fact]
    public void ApagarTrafegoAntigos_CortaOsDiasVelhos()
    {
        UsarPastasTemporarias();

        Banco.GravarTrafego(Dia, new[] { new TrafegoPrograma("chrome", 100, 0) });
        Banco.GravarTrafego(Dia.AddDays(-40), new[] { new TrafegoPrograma("chrome", 100, 0) });

        int apagados = Banco.ApagarTrafegoAntigos(Dia.AddDays(-30));

        Assert.Equal(1, apagados);
        Assert.Single(Banco.LerTrafego(Dia).Linhas);
        Assert.Empty(Banco.LerTrafego(Dia.AddDays(-40)).Linhas);
    }

    [Fact]
    public void ApagarTrafegoAntigos_CortaComOPrazoDoBanco()
    {
        UsarPastasTemporarias();

        var agora = DateTime.Now;

        Banco.GravarTrafego(agora, new[] { new TrafegoPrograma("chrome", 100, 0) });
        Banco.GravarTrafego(agora.AddDays(-40), new[] { new TrafegoPrograma("chrome", 100, 0) });

        Banco.ApagarTrafegoAntigos(agora.AddDays(-Banco.DiasRetidosTrafego));

        Assert.Single(Banco.LerTrafego(agora).Linhas);
        Assert.Empty(Banco.LerTrafego(agora.AddDays(-40)).Linhas);
    }

    // ------------------------------------------------------------------ pacotes perdidos

    [Fact]
    public void TrafegoPerdidos_GuardaELe()
    {
        UsarPastasTemporarias();

        Assert.True(Banco.GravarTrafegoPerdidos(Dia, 42));
        Assert.Equal(42, Banco.LerTrafegoPerdidos(Dia));
    }

    [Fact]
    public void TrafegoPerdidos_DeDiaQueNaoExiste_EZero()
    {
        UsarPastasTemporarias();

        // Zero é "nenhum pacote foi descartado" — e não "não deu para
        // contar". As duas coisas precisam ser diferentes.
        Assert.Equal(0, Banco.LerTrafegoPerdidos(Dia));
    }

    [Fact]
    public void TrafegoPerdidos_ReescreveEmVezDeSomar()
    {
        UsarPastasTemporarias();

        Banco.GravarTrafegoPerdidos(Dia, 42);
        Banco.GravarTrafegoPerdidos(Dia, 50);

        Assert.Equal(50, Banco.LerTrafegoPerdidos(Dia));
    }

    [Fact]
    public void TrafegoPerdidos_NegativoNaoEntra()
    {
        UsarPastasTemporarias();

        Banco.GravarTrafegoPerdidos(Dia, -10);

        Assert.Equal(0, Banco.LerTrafegoPerdidos(Dia));
    }

    // ------------------------------------------------------------------ limpeza

    [Fact]
    public void Limpar_ApagaTambemOTrafego()
    {
        UsarPastasTemporarias();

        Banco.GravarTrafego(Dia, new[] { new TrafegoPrograma("chrome", 100, 0) });
        Banco.GravarTrafegoPerdidos(Dia, 7);

        Assert.True(Banco.Limpar());

        Assert.Empty(Banco.LerTrafego(Dia).Linhas);
        Assert.Equal(0, Banco.LerTrafegoPerdidos(Dia));
    }

    // ------------------------------------------------------------------ reparo

    [Fact]
    public void Reparar_ComBancoSemAsTabelasDeTrafego_NaoECancelado()
    {
        UsarPastasTemporarias();

        for (int i = 0; i < 300; i++)
            Banco.GravarAmostra(new AmostraSensor(DateTime.Today.AddMinutes(i),
                70f, 40f, 90f, 3200f, 60f, 50f, 2000f, 2048f, 80f, 10f, 1f));

        Banco.GravarUso(new TrocaUso(DateTime.Today.AddHours(10), "chrome", "Painel"));

        // Um banco de antes da v0.29: as tabelas do tráfego não existem.
        // Isso NÃO é falha de leitura — e o reparo não pode ser cancelado
        // por causa disso.
        Banco.Instancia?.Executar("DROP TABLE trafego");
        Banco.Instancia?.Executar("DROP TABLE trafego_perdidos");
        Banco.Fechar();

        BancoCorrupcao.CorromperPaginaLegivel(Banco.Caminho, 300, 1);

        var resultado = Banco.Reparar();

        Assert.True(resultado.Sucesso, resultado.Relato);
        Assert.Equal(300, resultado.AmostrasRecuperadas);

        // O relato não pode inventar uma perda de tráfego: nunca houve
        // tráfego guardado nesse banco.
        Assert.DoesNotContain("registros de tráfego", resultado.Relato);
    }

    [Fact]
    public void Reparar_ComBancoCorrompido_MantemOTrafegoOuContaAPerda()
    {
        UsarPastasTemporarias();

        for (int i = 0; i < 300; i++)
            Banco.GravarAmostra(new AmostraSensor(DateTime.Today.AddMinutes(i),
                70f, 40f, 90f, 3200f, 60f, 50f, 2000f, 2048f, 80f, 10f, 1f));

        Banco.GravarUso(new TrocaUso(DateTime.Today.AddHours(10), "chrome", "Painel"));

        // Many programs across several days: the traffic is spread over
        // more pages, and the corruption of a single page does not take
        // everything with it.
        for (int dia = 0; dia < 10; dia++)
        {
            Banco.GravarTrafego(Dia.AddDays(-dia), new[]
            {
                new TrafegoPrograma("chrome", 1000 * (dia + 1), 2000 * (dia + 1)),
                new TrafegoPrograma("opera", 500 * (dia + 1), 0),
            });
        }

        Banco.GravarTrafegoPerdidos(Dia, 3);

        // A conta é feita com o banco aberto (a corruption precisa do
        // arquivo solto).
        int antes = ContarLinhasDoTrafego();
        Banco.Fechar();

        BancoCorrupcao.CorromperPaginaLegivel(Banco.Caminho, 300, 1);

        var resultado = Banco.Reparar();

        Assert.True(resultado.Sucesso, resultado.Relato);

        int depois = ContarLinhasDoTrafego();

        // O reparo não pode perder o tráfego em silêncio: ou ele volta, ou
        // o relato diz quantos se perderam.
        Assert.Contains("tráfego", resultado.Relato);

        if (depois < antes)
            Assert.Contains("Se perderam", resultado.Relato);

        Assert.True(depois <= antes, $"O reparo inventou linhas de tráfego: {antes} -> {depois}.");
    }

    /// <summary>
    /// Quantas linhas de tráfego o banco tem (em todos os dias).
    /// </summary>
    private static int ContarLinhasDoTrafego()
    {
        var banco = Banco.Instancia;
        if (banco == null) return 0;

        var linhas = banco.Consultar("SELECT COUNT(*) AS total FROM trafego");
        return linhas.Count > 0 ? (int)linhas[0].Inteiro("total") : 0;
    }

    // ------------------------------------------------------------------ relato do reparo

    [Fact]
    public void RelatoDoSalvo_ComOTrafego_ContaOTrafego()
    {
        string texto = Banco.RelatoDoSalvo(191, 10, 5, 2);

        Assert.Contains("191 amostras de sensor", texto);
        Assert.Contains("10 registros de uso", texto);
        Assert.Contains("5 registros de tráfego foram salvos", texto);
        Assert.Contains("2 contagens de pacotes descartados foram salvas", texto);
    }

    [Fact]
    public void RelatoDaPerda_ComOTrafego_ContaAPerdaDoTrafego()
    {
        string texto = Banco.RelatoDaPerda(300, 250, 60, 50, 20, 18, 3, 3);

        Assert.Contains("Se perderam 50 amostras de sensor", texto);
        Assert.Contains("Se perderam 10 registros de uso", texto);
        Assert.Contains("Se perderam 2 registros de tráfego", texto);
    }

    [Fact]
    public void RelatoDaPerda_SemOTrafegoNoBancoVelho_NaoInventaPerda()
    {
        // Um banco de antes da v0.29: null nas contagens do tráfego quer
        // dizer "não havia tabela", e não "não deu para contar".
        string texto = Banco.RelatoDaPerda(300, 300, 60, 60, null, null, null, null);

        Assert.DoesNotContain("tráfego", texto);
        Assert.DoesNotContain("Se perderam", texto);
    }

    [Fact]
    public void RelatoDaPerda_ComContagemDoTrafegoQueNaoDeu_NaoInventaNumero()
    {
        string texto = Banco.RelatoDaPerda(300, 300, 60, 60, -1, 0, 3, 3);

        Assert.DoesNotContain("-1", texto);
        Assert.Contains("Não deu para contar os registros de tráfego do banco antigo", texto);
    }
}
