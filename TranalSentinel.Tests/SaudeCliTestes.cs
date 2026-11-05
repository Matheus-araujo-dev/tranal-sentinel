using System.IO;
using System.Text.Json;
using Xunit;

namespace TranalSentinel.Tests;

/// <summary>
/// Testes da linha de comando do TRANAL SAÚDE.
///
/// A comparação e o formato do snapshot são funções puras: nenhum Windows é
/// tocado aqui. Só o teste de ponta a ponta do modo "comparar" grava um
/// arquivo de verdade (na pasta padrão de snapshots) e o apaga no final.
/// </summary>
public class SaudeCliTestes
{
    private static Achado Achado(
        string area,
        string mensagem,
        Severidade severidade = Severidade.Alto,
        string acao = "verifique isto") =>
        new() { Area = area, Mensagem = mensagem, Severidade = severidade, Acao = acao };

    private static RelatorioSaude Relatorio(params Achado[] achados)
    {
        var relatorio = new RelatorioSaude { GeradoEm = new DateTime(2026, 11, 5, 3, 20, 0) };
        foreach (var achado in achados) relatorio.Achados.Add(achado);
        return relatorio;
    }

    // ----------------------------------------------------------------- comparação

    [Fact]
    public void AchadoQueSumiu_ApareceComoResolvido()
    {
        var antes = new List<Achado> { Achado("Hardware", "4 eventos WHEA (erro de CPU/RAM/PCIe)") };
        var depois = new List<Achado>();

        var diferencas = SaudeCli.CompararAchados(antes, depois);

        var unica = Assert.Single(diferencas);
        Assert.Equal(TipoDiferenca.Resolveu, unica.Tipo);
        Assert.Contains("Hardware", unica.Achado);
        Assert.Contains("4 eventos WHEA", unica.Achado);
    }

    [Fact]
    public void AchadoQueApareceu_ApareceComoNovo()
    {
        var antes = new List<Achado>();
        var depois = new List<Achado> { Achado("Disco", "Samsung SSD 990 PRO não está pronto (Erro)") };

        var diferencas = SaudeCli.CompararAchados(antes, depois);

        var unica = Assert.Single(diferencas);
        Assert.Equal(TipoDiferenca.Novo, unica.Tipo);
        Assert.Contains("Disco", unica.Achado);
        Assert.Contains("não está pronto", unica.Achado);
    }

    [Fact]
    public void AchadoIgualNosDois_ApareceComoPersistente()
    {
        var achado = Achado("Lentidão", "12 programas na inicialização automática", Severidade.Medio);
        var antes = new List<Achado> { achado };
        var depois = new List<Achado> { achado };

        var diferencas = SaudeCli.CompararAchados(antes, depois);

        var unica = Assert.Single(diferencas);
        Assert.Equal(TipoDiferenca.Persiste, unica.Tipo);
        Assert.Contains("12 programas", unica.Achado);
    }

    [Fact]
    public void ContagemQueMudou_NaoViraAchadoNovoNemResolvido()
    {
        // "3 travamentos" e "1 travamentos" são o mesmo achado: só a contagem mudou.
        // (é exatamente o texto que a regra de travamentos produz).
        var antes = new List<Achado> { Achado("Travamentos", "3 travamentos sem bugcheck e sem minidump (congelamento total)", Severidade.Critico) };
        var depois = new List<Achado> { Achado("Travamentos", "1 travamentos sem bugcheck e sem minidump (congelamento total)", Severidade.Critico) };

        var diferencas = SaudeCli.CompararAchados(antes, depois);

        var unica = Assert.Single(diferencas);
        Assert.Equal(TipoDiferenca.Persiste, unica.Tipo);
    }

    [Fact]
    public void ContagemDeBugcheckQueMudou_NaoViraAchadoNovoNemResolvido()
    {
        var antes = new List<Achado> { Achado("Travamentos", "3 travamentos (1 com bugcheck, 2 sem botão de energia)", Severidade.Alto) };
        var depois = new List<Achado> { Achado("Travamentos", "2 travamentos (1 com bugcheck, 1 sem botão de energia)", Severidade.Alto) };

        var diferencas = SaudeCli.CompararAchados(antes, depois);

        Assert.Equal(TipoDiferenca.Persiste, Assert.Single(diferencas).Tipo);
    }

    [Fact]
    public void SeveridadeQueMudou_ContinuaPersistente()
    {
        var antes = new List<Achado> { Achado("Hardware", "2 eventos WHEA (erro de CPU/RAM/PCIe)", Severidade.Alto) };
        var depois = new List<Achado> { Achado("Hardware", "2 eventos WHEA (erro de CPU/RAM/PCIe)", Severidade.Medio) };

        var diferencas = SaudeCli.CompararAchados(antes, depois);

        var unica = Assert.Single(diferencas);
        Assert.Equal(TipoDiferenca.Persiste, unica.Tipo);
        Assert.Contains("ALTO", unica.Detalhe);
        Assert.Contains("MÉDIO", unica.Detalhe);
    }

    [Fact]
    public void AcaoReescrita_NaoViraAchadoNovoNemResolvido()
    {
        // O texto da ação pode mudar sem que o achado mude.
        var antes = new List<Achado> { Achado("Segurança", "Defender parado E filtro de kernel do antivírus ausente", Severidade.Critico, "Instale o pacote completo") };
        var depois = new List<Achado> { Achado("Segurança", "Defender parado E filtro de kernel do antivírus ausente", Severidade.Critico, "Reinstale o antivírus") };

        var diferencas = SaudeCli.CompararAchados(antes, depois);

        Assert.Equal(TipoDiferenca.Persiste, Assert.Single(diferencas).Tipo);
    }

    [Fact]
    public void AchadoRepetido_ContaCadaOcorrencia()
    {
        // Dois achados iguais no "antes" e um no "depois": um persiste e um foi resolvido.
        var antes = new List<Achado>
        {
            Achado("Disco", "Netac NVMe SSD 1TB não está pronto (Erro)", Severidade.Critico),
            Achado("Disco", "Netac NVMe SSD 1TB não está pronto (Erro)", Severidade.Critico)
        };
        var depois = new List<Achado> { Achado("Disco", "Netac NVMe SSD 1TB não está pronto (Erro)", Severidade.Critico) };

        var diferencas = SaudeCli.CompararAchados(antes, depois);

        Assert.Equal(2, diferencas.Count);
        Assert.Single(diferencas, d => d.Tipo == TipoDiferenca.Persiste);
        Assert.Single(diferencas, d => d.Tipo == TipoDiferenca.Resolveu);
    }

    [Fact]
    public void SemAchadosNosDoisLados_NaoGeraDiferenca()
    {
        Assert.Empty(SaudeCli.CompararAchados(new List<Achado>(), new List<Achado>()));
    }

    [Fact]
    public void Diferencas_VemAgrupadasPorTipo()
    {
        var antes = new List<Achado>
        {
            Achado("Hardware", "4 eventos WHEA (erro de CPU/RAM/PCIe)"),
            Achado("Lentidão", "12 programas na inicialização automática", Severidade.Medio)
        };
        var depois = new List<Achado>
        {
            Achado("Disco", "Samsung SSD 990 PRO não está pronto (Erro)", Severidade.Critico),
            Achado("Lentidão", "12 programas na inicialização automática", Severidade.Medio)
        };

        var diferencas = SaudeCli.CompararAchados(antes, depois);

        Assert.Equal(3, diferencas.Count);
        Assert.Equal(TipoDiferenca.Resolveu, diferencas[0].Tipo);
        Assert.Equal(TipoDiferenca.Novo, diferencas[1].Tipo);
        Assert.Equal(TipoDiferenca.Persiste, diferencas[2].Tipo);
    }

    // ------------------------------------------------------------------ snapshot

    [Fact]
    public void SnapshotGravadoERelido_ComparaSemErro()
    {
        var relatorio = Relatorio(
            Achado("Hardware", "4 eventos WHEA (erro de CPU/RAM/PCIe)", Severidade.Alto),
            Achado("Lentidão", "12 programas na inicialização automática", Severidade.Medio));

        var snapshot = SnapshotSaude.DeRelatorio(relatorio);
        string json = snapshot.ParaJson();

        var lido = SnapshotSaude.DeJson(json);
        var diferencas = SaudeCli.CompararAchados(lido.ParaAchados(), snapshot.ParaAchados());

        Assert.Equal(2, diferencas.Count);
        Assert.All(diferencas, d => Assert.Equal(TipoDiferenca.Persiste, d.Tipo));
    }

    [Fact]
    public void SnapshotGravadoERelido_GuardaContadoresETravamentos()
    {
        var dados = new ColetaSaude { EventosWhea = 4, Minidumps = 1 };
        dados.Travamentos.Add(new TravamentoInfo
        {
            Data = new DateTime(2026, 10, 7, 23, 43, 38),
            Bugcheck = "0",
            Detalhe = "Sem botão de energia (travou sozinho)"
        });

        var relatorio = new RelatorioSaude { GeradoEm = new DateTime(2026, 11, 5, 3, 20, 0), Dados = dados };
        relatorio.Achados.Add(Achado("Hardware", "4 eventos WHEA (erro de CPU/RAM/PCIe)"));

        var lido = SnapshotSaude.DeJson(SnapshotSaude.DeRelatorio(relatorio).ParaJson());

        Assert.Equal(4, lido.Dados.EventosWhea);
        Assert.Equal(1, lido.Dados.Minidumps);
        Assert.Single(lido.Dados.Travamentos);
        Assert.NotEmpty(lido.Contadores);
        Assert.Contains(lido.Contadores, c => c.Nome == "Erros de hardware (WHEA)" && c.Valor == "4");
    }

    [Fact]
    public void SnapshotGravado_EhJsonIndentadoComAsChavesDoFormato()
    {
        var relatorio = Relatorio(Achado("Hardware", "4 eventos WHEA (erro de CPU/RAM/PCIe)"));
        string json = SnapshotSaude.DeRelatorio(relatorio).ParaJson();

        Assert.Contains("\"achados\"", json);
        Assert.Contains("\"achados\": [", json);
        Assert.Contains("\"mensagem\"", json);
        Assert.Contains("\"severidade\": \"Alto\"", json);
        Assert.Contains('\n', json); // WriteIndented: o JSON sai em várias linhas
    }

    [Fact]
    public void JsonMalformado_DesenhaErroDeJson()
    {
        // A leitura do JSON devolve JsonReaderException, que deriva de JsonException.
        Assert.ThrowsAny<JsonException>(() => SnapshotSaude.DeJson("{isto nao e json"));
    }

    [Fact]
    public void ArquivoQueNaoESnapshot_DesenhaErroClaro()
    {
        var erro = Assert.Throws<FormatException>(() => SnapshotSaude.DeJson("{\"foo\": 1}"));
        Assert.Contains("achados", erro.Message);
    }

    [Fact]
    public void JsonQueNaoEObjeto_DesenhaErroClaro()
    {
        Assert.Throws<FormatException>(() => SnapshotSaude.DeJson("[1, 2, 3]"));
    }

    // ---------------------------------------------------------------- argumentos

    [Theory]
    [InlineData("--saude")]
    [InlineData("--saude xyz")]
    [InlineData("--saude comparar")]
    [InlineData("--saude comparar antes.json")]
    [InlineData("--saude comparar a.json b.json --saida")]
    [InlineData("--saude coletar --saida")]
    [InlineData("--saude coletar --saida pasta --extra")]
    [InlineData("--saude coletar --outro pasta")]
    [InlineData("xyz")]
    public void ComandoDesconhecido_Devolve2(string linha)
    {
        var args = linha.Length == 0 ? Array.Empty<string>() : linha.Split(' ');

        Assert.Equal(2, SaudeCli.Executar(args));
    }

    [Fact]
    public void SemArgumentos_Devolve2()
    {
        Assert.Equal(2, SaudeCli.Executar(Array.Empty<string>()));
    }

    // --------------------------------------------------------------- ponta a ponta

    [Fact]
    public void CompararComArquivoInexistente_Devolve1()
    {
        string inexistente = Path.Combine(Path.GetTempPath(), "tranal-saude-nao-existe-" + Guid.NewGuid().ToString("N") + ".json");

        Assert.Equal(1, SaudeCli.Executar(new[] { "--saude", "comparar", inexistente, inexistente }));
    }

    [Fact]
    public void CompararComJsonInvalido_Devolve1()
    {
        string pasta = Path.Combine(Path.GetTempPath(), "tranal-saude-cli-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(pasta);

        try
        {
            string invalido = Path.Combine(pasta, "invalido.json");
            File.WriteAllText(invalido, "{isto nao e json");

            Assert.Equal(1, SaudeCli.Executar(new[] { "--saude", "comparar", invalido, invalido }));
        }
        finally
        {
            try { Directory.Delete(pasta, true); } catch { }
        }
    }

    [Fact]
    public void CompararDoisSnapshotsValidos_Devolve0()
    {
        string pasta = Path.Combine(Path.GetTempPath(), "tranal-saude-cli-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(pasta);

        try
        {
            var antes = Relatorio(Achado("Hardware", "4 eventos WHEA (erro de CPU/RAM/PCIe)"));
            var depois = Relatorio(
                Achado("Hardware", "4 eventos WHEA (erro de CPU/RAM/PCIe)"),
                Achado("Disco", "Samsung SSD 990 PRO não está pronto (Erro)", Severidade.Critico));

            string arquivoAntes = Path.Combine(pasta, "antes.json");
            string arquivoDepois = Path.Combine(pasta, "depois.json");
            File.WriteAllText(arquivoAntes, SnapshotSaude.DeRelatorio(antes).ParaJson());
            File.WriteAllText(arquivoDepois, SnapshotSaude.DeRelatorio(depois).ParaJson());

            int codigo = SaudeCli.Executar(new[] { "--saude", "comparar", arquivoAntes, arquivoDepois });

            Assert.Equal(0, codigo);
        }
        finally
        {
            try { Directory.Delete(pasta, true); } catch { }
        }
    }

    // ------------------------------------------------------------------ textos

    [Fact]
    public void RelatorioEmTexto_TrazOsAchadosEOContexto()
    {
        var relatorio = Relatorio(Achado("Hardware", "4 eventos WHEA (erro de CPU/RAM/PCIe)", Severidade.Alto));

        string texto = SaudeCli.MontarRelatorio(relatorio);

        Assert.Contains("TRANAL SAÚDE", texto);
        Assert.Contains("4 eventos WHEA", texto);
        Assert.Contains("CONTEXTO DO SISTEMA", texto);
        Assert.Contains("Erros de hardware (WHEA)", texto);
    }

    [Fact]
    public void RelatorioSemAchados_DizQueNaoAchouNada()
    {
        string texto = SaudeCli.MontarRelatorio(Relatorio());

        Assert.Contains("Nenhum problema encontrado.", texto);
    }

    [Fact]
    public void ComparacaoEmTexto_TrazOsTresGrupos()
    {
        var antes = Relatorio(Achado("Hardware", "4 eventos WHEA (erro de CPU/RAM/PCIe)"));
        var depois = Relatorio(
            Achado("Disco", "Samsung SSD 990 PRO não está pronto (Erro)", Severidade.Critico),
            Achado("Hardware", "4 eventos WHEA (erro de CPU/RAM/PCIe)"));

        var diferencas = SaudeCli.CompararAchados(antes.Achados, depois.Achados);
        string texto = SaudeCli.MontarComparacao(
            SnapshotSaude.DeRelatorio(antes),
            SnapshotSaude.DeRelatorio(depois),
            "antes.json",
            "depois.json",
            diferencas);

        Assert.Contains("NOVOS — apareceram", texto);
        Assert.Contains("RESOLVIDOS — sumiram", texto);
        Assert.Contains("PERSISTENTES — continuam", texto);
        Assert.Contains("antes.json", texto);
        Assert.Contains("depois.json", texto);
    }

    [Fact]
    public void ContadoresQueMudaram_SaoMostradosNaComparacao()
    {
        var antes = Relatorio();
        var dadosDepois = new ColetaSaude { EventosWhea = 4 };
        var depois = new RelatorioSaude { GeradoEm = new DateTime(2026, 11, 5, 4, 0, 0), Dados = dadosDepois };

        var mudancas = SaudeCli.CompararContadores(
            SnapshotSaude.DeRelatorio(antes),
            SnapshotSaude.DeRelatorio(depois));

        var mudanca = Assert.Single(mudancas, m => m.Nome == "Erros de hardware (WHEA)");
        Assert.Equal("0", mudanca.Antes);
        Assert.Equal("4", mudanca.Depois);
    }
}
