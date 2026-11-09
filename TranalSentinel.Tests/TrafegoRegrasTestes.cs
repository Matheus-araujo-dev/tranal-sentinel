using System.Globalization;
using Xunit;

namespace TranalSentinel.Tests;

/// <summary>
/// Testes da regra pura do tráfego de internet (o ranking por programa, as
/// unidades de byte e os textos do cartão).
///
/// <para>
/// A regra mora em um lugar só e é consumida pela tela, pelo histórico e
/// pela linha de comando. Cada teste aqui prende uma decisão que a tela
/// toma sozinha: a soma dos processos do mesmo programa, o zero que não
/// entra no ranking, a vírgula do número brasileiro e o cartão que não
/// mostra número quando a leitura não deu para ser feita.
/// </para>
/// </summary>
public class TrafegoRegrasTestes
{
    private static readonly DateTime Hoje = new(2026, 11, 9, 14, 32, 11);
    private static readonly DateTime Ontem = new(2026, 11, 8, 0, 0, 0);

    // ------------------------------------------------------------------ ranking

    [Fact]
    public void Ranking_SomaOsProcessosDoMesmoPrograma()
    {
        // O Chrome abre um processo por aba: o nome é o mesmo, e a soma tem
        // que ser por nome (e não por processo).
        var itens = new[]
        {
            new TrafegoPrograma("chrome", 100, 200),
            new TrafegoPrograma("chrome", 50, 25),
        };

        var ranking = TrafegoRegras.Ranking(itens, 10);

        var linha = Assert.Single(ranking);
        Assert.Equal("chrome", linha.Processo);
        Assert.Equal(150, linha.Enviados);
        Assert.Equal(225, linha.Recebidos);
        Assert.Equal(375, linha.Total);
    }

    [Fact]
    public void Ranking_NaoDistingueMaiusculasDeMinusculas()
    {
        var itens = new[]
        {
            new TrafegoPrograma("Chrome", 100, 0),
            new TrafegoPrograma("chrome", 0, 50),
        };

        var linha = Assert.Single(TrafegoRegras.Ranking(itens, 10));
        Assert.Equal(150, linha.Total);
    }

    [Fact]
    public void Ranking_DescartaQuemNaoTrafficou()
    {
        // Um programa com zero byte não é "o 5º que mais usou a rede": não
        // usou.
        var itens = new[]
        {
            new TrafegoPrograma("chrome", 100, 0),
            new TrafegoPrograma("notepad", 0, 0),
        };

        var ranking = TrafegoRegras.Ranking(itens, 10);

        Assert.Single(ranking);
        Assert.Equal("chrome", ranking[0].Processo);
    }

    [Fact]
    public void Ranking_OrdenaPeloTotalEDesempataPeloNome()
    {
        var itens = new[]
        {
            new TrafegoPrograma("zeta", 100, 0),
            new TrafegoPrograma("alfa", 100, 0),
            new TrafegoPrograma("meio", 500, 0),
        };

        var ranking = TrafegoRegras.Ranking(itens, 10);

        Assert.Equal("meio", ranking[0].Processo);
        // O desempate é pelo nome: sem ele a lista trocava de lugar a cada
        // leitura, e "pulava" na tela.
        Assert.Equal("alfa", ranking[1].Processo);
        Assert.Equal("zeta", ranking[2].Processo);
    }

    [Fact]
    public void Ranking_LimitaAoMaximo()
    {
        var itens = Enumerable.Range(0, 25)
            .Select(i => new TrafegoPrograma("programa" + i, i + 1, 0))
            .ToList();

        var ranking = TrafegoRegras.Ranking(itens, TrafegoRegras.MaximoNoRanking);

        Assert.Equal(TrafegoRegras.MaximoNoRanking, ranking.Count);
        Assert.Equal("programa24", ranking[0].Processo);
    }

    [Fact]
    public void Ranking_ComNomeVazioNaoQuebra()
    {
        var itens = new[] { new TrafegoPrograma("   ", 10, 0) };

        var linha = Assert.Single(TrafegoRegras.Ranking(itens, 10));
        Assert.Equal("(sem nome)", linha.Processo);
    }

    [Fact]
    public void Ranking_NegativoNaoEntraNaConta()
    {
        // Número negativo é "não deu para contar" — ele não pode virar byte.
        var itens = new[] { new TrafegoPrograma("chrome", -500, 100) };

        var linha = Assert.Single(TrafegoRegras.Ranking(itens, 10));
        Assert.Equal(0, linha.Enviados);
        Assert.Equal(100, linha.Total);
    }

    // ------------------------------------------------------------------ números

    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(512, "512 B")]
    [InlineData(1024, "1 KB")]
    [InlineData(1536, "1,5 KB")]
    [InlineData(1048576, "1 MB")]
    [InlineData(104857600, "100 MB")]
    [InlineData(1073741824, "1 GB")]
    public void TextoDosBytes_UsaUnidadeDe1024EVirgulaDecimal(long bytes, string esperado)
    {
        Assert.Equal(esperado, TrafegoRegras.TextoDosBytes(bytes));
    }

    [Fact]
    public void TextoDosBytes_NegativoEDash()
    {
        // -1 é "não deu para contar": não é zero.
        Assert.Equal("—", TrafegoRegras.TextoDosBytes(-1));
    }

    [Fact]
    public void Numero_NaoDependeDoLocale()
    {
        CultureInfo anterior = CultureInfo.CurrentCulture;
        try
        {
            // A máquina de integração roda em en-US: o mesmo texto tem que
            // sair em pt-BR nos dois lugares.
            CultureInfo.CurrentCulture = new CultureInfo("en-US");
            Assert.Equal("1,5", TrafegoRegras.Numero(1.5));

            CultureInfo.CurrentCulture = new CultureInfo("pt-BR");
            Assert.Equal("1,5", TrafegoRegras.Numero(1.5));
        }
        finally
        {
            CultureInfo.CurrentCulture = anterior;
        }
    }

    [Fact]
    public void TextoDosBytes_NaoDependeDoLocale()
    {
        CultureInfo anterior = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("en-US");
            Assert.Equal("1,5 KB", TrafegoRegras.TextoDosBytes(1536));
        }
        finally
        {
            CultureInfo.CurrentCulture = anterior;
        }
    }

    [Fact]
    public void Totais_SomaOQueFoiMedido()
    {
        var itens = new[]
        {
            new TrafegoPrograma("chrome", 100, 200),
            new TrafegoPrograma("spotify", 50, 0),
        };

        var (enviados, recebidos, total) = TrafegoRegras.Totais(itens);

        Assert.Equal(150, enviados);
        Assert.Equal(200, recebidos);
        Assert.Equal(350, total);
    }

    // ------------------------------------------------------------------ cartão

    [Fact]
    public void Relatorio_ComLeituraIncompleta_NaoMostraNumero()
    {
        var itens = new[] { new TrafegoPrograma("chrome", 100, 200) };

        var relatorio = TrafegoRegras.Relatorio(itens, Hoje, Hoje, Hoje, false, null, 0);

        Assert.Equal("NÃO DEU PARA LER", relatorio.Cartao.VerdictText);
        Assert.True(relatorio.Cartao.IsAlert);
        Assert.Empty(relatorio.Ranking);

        // Nenhuma métrica vira número: um total pela metade é pior que um
        // cartão que diz que não deu para ler.
        foreach (var metrica in relatorio.Cartao.Metrics)
            Assert.Equal("—", metrica.Value);
    }

    [Fact]
    public void Relatorio_ComErro_MostraOMotivo()
    {
        var relatorio = TrafegoRegras.Relatorio(
            new List<TrafegoPrograma>(), Hoje, Hoje, Hoje, true,
            "A leitura do tráfego precisa de administrador.", 0);

        Assert.Contains("administrador", relatorio.Cartao.Reasons);
        Assert.True(relatorio.Cartao.IsAlert);
        Assert.Equal("SEM MEDIÇÃO", relatorio.Cartao.VerdictText);
    }

    [Fact]
    public void Relatorio_OTotalEASomaDeTodosOsProgramas()
    {
        // O total é de todos os programas — não só dos que estão no
        // ranking (que é limitado a dez).
        var itens = Enumerable.Range(0, 15)
            .Select(i => new TrafegoPrograma("programa" + i, 1000, 0))
            .ToList();

        var relatorio = TrafegoRegras.Relatorio(itens, Hoje, Hoje, Hoje, true, null, 0);

        Assert.Equal(TrafegoRegras.MaximoNoRanking, relatorio.Ranking.Count);

        var total = relatorio.Cartao.Metrics.First(m => m.Key == "TOTAL");
        Assert.Equal("14,6 KB", total.Value);
    }

    [Fact]
    public void Relatorio_ComPacotesPerdidos_AvisaQueOTotalEMenor()
    {
        var itens = new[] { new TrafegoPrograma("chrome", 100, 200) };

        var relatorio = TrafegoRegras.Relatorio(itens, Hoje, Hoje, Hoje, true, null, 42);

        Assert.Contains("42", relatorio.Cartao.Reasons);
        Assert.Contains("menor do que foi o real", relatorio.Cartao.Reasons);
        Assert.True(relatorio.Cartao.IsAlert);
        Assert.Contains(relatorio.Cartao.Metrics, m => m.Key == "PACOTES DEIXADOS DE FORA");
    }

    [Fact]
    public void Relatorio_ComPacotesPerdidosDesconhecidos_NaoFingeQueFoiZero()
    {
        var itens = new[] { new TrafegoPrograma("chrome", 100, 200) };

        var relatorio = TrafegoRegras.Relatorio(itens, Hoje, Hoje, Hoje, true, null, -1);

        Assert.Contains("Não deu para contar", relatorio.Cartao.Reasons);
        Assert.True(relatorio.Cartao.IsAlert);
        Assert.DoesNotContain(relatorio.Cartao.Metrics, m => m.Key == "PACOTES DEIXADOS DE FORA");
    }

    [Fact]
    public void Relatorio_OAvisoEntraNaExplicacao()
    {
        var itens = new[] { new TrafegoPrograma("chrome", 100, 200) };

        var relatorio = TrafegoRegras.Relatorio(
            itens, Hoje, Hoje, Hoje, true, null, 0,
            "O total acima é só o que foi medido depois que o app subiu.");

        Assert.Contains("só o que foi medido depois", relatorio.Cartao.Reasons);
    }

    [Fact]
    public void Relatorio_SemNadaMedido_NaoDizQueNaoTeveTrafego()
    {
        var relatorio = TrafegoRegras.Relatorio(
            new List<TrafegoPrograma>(), Hoje, Hoje, Hoje, true, null, 0);

        Assert.Contains("Nenhum programa registrou tráfego", relatorio.Cartao.Reasons);
        Assert.Contains("O app só mede enquanto está rodando", relatorio.Cartao.Reasons);
        Assert.Equal("0 B", relatorio.Cartao.VerdictText);
    }

    [Fact]
    public void Relatorio_OTituloFalaDoDia()
    {
        var relatorio = TrafegoRegras.Relatorio(
            new List<TrafegoPrograma>(), Hoje, Hoje, Hoje, true, null, 0);

        Assert.Equal("TRÁFEGO DE HOJE", relatorio.Cartao.Title);
        Assert.Contains("14:32:11", relatorio.Cartao.Meta);
    }

    [Fact]
    public void Relatorio_DiaPassado_NAoFalaDeAgora()
    {
        var relatorio = TrafegoRegras.Relatorio(
            new List<TrafegoPrograma>(), Ontem, Hoje, Hoje, true, null, 0);

        Assert.Equal("TRÁFEGO DE ONTEM", relatorio.Cartao.Title);
        Assert.Contains("08/11/2026", relatorio.Cartao.Meta);
        Assert.DoesNotContain("14:32:11", relatorio.Cartao.Meta);
    }

    [Theory]
    [InlineData(0, "HOJE")]
    [InlineData(1, "ONTEM")]
    [InlineData(4, "05/11/2026")]
    public void RotuloDoDia(int diasAtras, string esperado)
    {
        Assert.Equal(esperado, TrafegoRegras.RotuloDoDia(Hoje.AddDays(-diasAtras), Hoje));
    }

    [Fact]
    public void Linhas_TrazOsTextosProntos()
    {
        var ranking = TrafegoRegras.Ranking(
            new[] { new TrafegoPrograma("chrome", 1000, 2000) }, 10);

        var linha = Assert.Single(TrafegoRegras.Linhas(ranking));

        Assert.Equal("chrome", linha.Processo);
        Assert.Equal("2,9 KB", linha.Total);
        Assert.Equal("2 KB recebidos · 1000 B enviados", linha.Detalhe);
    }

    [Fact]
    public void TextoDoDetalhe_TrazRecebidoEEnviado()
    {
        var item = new TrafegoPrograma("chrome", 1000, 2000);

        Assert.Equal("2 KB recebidos · 1000 B enviados", TrafegoRegras.TextoDoDetalhe(item));
        Assert.Equal("2,9 KB", TrafegoRegras.TextoDoTotal(item));
    }

    [Fact]
    public void Relatorio_ComMaisDeDezProgramas_OCartaoContaTodos()
    {
        // O ranking da tela mostra os dez maiores, mas a metrica PROGRAMAS
        // conta todos: um cartao que dissesse "10 programas" quando 25
        // usaram a rede seria um numero errado.
        var itens = Enumerable.Range(0, 25)
            .Select(i => new TrafegoPrograma("programa" + i, 1000, 0))
            .ToList();

        var relatorio = TrafegoRegras.Relatorio(
            itens, DateTime.Today, DateTime.Today, DateTime.Now, true, null, 0);

        var programas = relatorio.Cartao.Metrics.First(m => m.Key == "PROGRAMAS");

        Assert.Equal("25 programas", programas.Value);
        Assert.Contains("mais 22 programas usaram a rede", relatorio.Cartao.Reasons);
        Assert.Equal(TrafegoRegras.MaximoNoRanking, relatorio.Ranking.Count);
    }

    [Fact]
    public void ContarProgramas_NaoContaQuemNaoTrafegou()
    {
        // Quem nao usou a rede nao e um "programa que usou a rede":
        // o zero nao entra na conta.
        var itens = new[]
        {
            new TrafegoPrograma("chrome", 1000, 0),
            new TrafegoPrograma("opera", 0, 0),
        };

        Assert.Equal(1, TrafegoRegras.ContarProgramas(itens));
    }

    [Fact]
    public void Relatorio_ComUmProgramaSo_DizUmPrograma()
    {
        var itens = new[] { new TrafegoPrograma("chrome", 1000, 0) };

        var relatorio = TrafegoRegras.Relatorio(
            itens, DateTime.Today, DateTime.Today, DateTime.Now, true, null, 0);

        var programas = relatorio.Cartao.Metrics.First(m => m.Key == "PROGRAMAS");

        Assert.Equal("1 programa", programas.Value);
    }


    [Fact]
    public void NotaDaMedicao_CitaONumeroMedidoEOPorcentual()
    {
        // A nota da tela e os numeros medidos nao podem divergir: um
        // texto que falasse de outra medicao seria uma mentira na tela.
        string nota = TrafegoRegras.NotaDaMedicao;

        Assert.Contains(
            TrafegoRegras.BytesMedidosNaMedicao.ToString(CultureInfo.InvariantCulture), nota);
        Assert.Contains("+0,14", nota);

        // E o percentual tem de bater com os numeros.
        double real = 100.0 *
            (TrafegoRegras.BytesMedidosNaMedicao - TrafegoRegras.BytesBaixadosNaMedicao) /
            TrafegoRegras.BytesBaixadosNaMedicao;

        Assert.True(Math.Abs(real - TrafegoRegras.DiferencaDaMedicaoEmPercento) < 0.005,
            "O percentual da nota (" + TrafegoRegras.DiferencaDaMedicaoEmPercento +
            "%) nao bate com os numeros medidos (" + real + "%).");
    }

    [Fact]
    public void Relatorio_DeDiaGuardado_SemTrafego_NaoFalaDoDiaDeHoje()
    {
        // O cartao de ontem nao pode dizer "desde que o app comecou a
        // medir (hoje e 14:32)": o numero veio do banco, e o app so
        // mede enquanto esta rodando.
        var relatorio = TrafegoRegras.Relatorio(
            new List<TrafegoPrograma>(), DateTime.Today.AddDays(-1), DateTime.Today,
            DateTime.Now, true, null, 0);

        Assert.DoesNotContain("hoje é", relatorio.Cartao.Reasons);
        Assert.DoesNotContain("O app só mede enquanto está rodando", relatorio.Cartao.Reasons);
        Assert.Contains("neste dia", relatorio.Cartao.Reasons);
    }

    [Fact]
    public void Relatorio_DeHoje_SemTrafego_FalaDaMedicaoAoVivo()
    {
        var relatorio = TrafegoRegras.Relatorio(
            new List<TrafegoPrograma>(), DateTime.Today, DateTime.Today,
            DateTime.Now, true, null, 0);

        Assert.Contains("O app só mede enquanto está rodando", relatorio.Cartao.Reasons);
    }

    [Fact]
    public void NumeroInteiro_ContaGrande_NaoPerdeCasa()
    {
        // A contagem de pacotes descartados e um long no banco: passar
        // por um double a perderia acima de 2^53, e um int a cortaria
        // em 2.147.483.647.
        Assert.Equal("3000000000", TrafegoRegras.NumeroInteiro(3000000000L));
        Assert.Equal("0", TrafegoRegras.NumeroInteiro(0));
        Assert.Equal("-1", TrafegoRegras.NumeroInteiro(-1));
    }

    [Fact]
    public void Relatorio_ComMuitosDescarte_NaoSaturaEmInt()
    {
        var relatorio = TrafegoRegras.Relatorio(
            new List<TrafegoPrograma>(), DateTime.Today, DateTime.Today,
            DateTime.Now, true, null, 3000000000L);

        var descartados = relatorio.Cartao.Metrics
            .First(m => m.Key == "PACOTES DEIXADOS DE FORA");

        Assert.Equal("3000000000", descartados.Value);
    }

}
