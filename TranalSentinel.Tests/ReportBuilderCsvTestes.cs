using System.Text;
using Xunit;

namespace TranalSentinel.Tests;

/// <summary>Testes da exportação do relatório em CSV (planilha para o Excel em português).</summary>
public class ReportBuilderCsvTestes
{
    private static ReportData Dados() => new ReportData
    {
        Date = new DateTime(2026, 11, 5, 14, 29, 0),
        Machine = "PC-DE-TESTE",
        AppVersion = "0.17.0",
        System = new List<ReportNode>
        {
            new ReportNode("Sistema operacional", null, new List<ReportNode>
            {
                new ReportNode("Nome", "Windows 11 Pro", null),
                new ReportNode("Relógio", "3.5 GHz", null),
            }),
        },
        Gpus = new List<ReportGpu>
        {
            new ReportGpu("NVIDIA GeForce RTX 4070", "12 GB VRAM", new List<ReportNode>
            {
                new ReportNode("Driver", "551.23.1", null),
            }),
        },
        Disks = new List<ReportDisk>
        {
            new ReportDisk("Samsung SSD 990 PRO", "1 TB", "SAUDÁVEL", "Nenhum problema encontrado", new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("Temperatura", "38.4 °C"),
                new KeyValuePair<string, string>("Ciclos de carga", "120"),
            }),
        },
        Power = new List<ReportDisk>
        {
            new ReportDisk("Bateria", "48 Wh", "EM USO", "Carregando", new List<KeyValuePair<string, string>>()),
        },
        Network = new List<ReportDisk>
        {
            new ReportDisk("Latência", "12 ms", "SAUDÁVEL", "Sem perda de pacote", new List<KeyValuePair<string, string>>()),
        },
        Sensors = new List<ReportSensor>
        {
            new ReportSensor("GPU NVIDIA", "Temperatura", "Núcleo", "53.4", "20", "90"),
        },
        Benchmarks = new List<ReportBench>
        {
            new ReportBench(new DateTime(2026, 11, 4, 20, 15, 0), "Disco", "Média 1200 MB/s"),
        },
    };

    [Fact]
    public void CsvTemOsCabecalhosDeCadaBloco()
    {
        var csv = ReportBuilder.Csv(Dados());

        Assert.Contains("Campo;Valor", csv);
        Assert.Contains("Disco;Detalhes;Saúde;Motivos", csv);
        Assert.Contains("Métrica;Valor", csv);
        Assert.Contains("Seção;Item;Valor", csv);
        Assert.Contains("Hardware;Categoria;Sensor;Valor;Mín.;Máx.", csv);
        Assert.Contains("Data;Teste;Resultado", csv);
    }

    [Fact]
    public void CsvTemTodosOsBlocosDoRelatorio()
    {
        var csv = ReportBuilder.Csv(Dados());

        Assert.Contains("Relatório", csv);
        Assert.Contains("Saúde dos discos", csv);
        Assert.Contains("Bateria e energia", csv);
        Assert.Contains("Teste de rede", csv);
        Assert.Contains("Sistema", csv);
        Assert.Contains("Placa de vídeo · NVIDIA GeForce RTX 4070", csv);
        Assert.Contains("Sensores (leitura no momento da exportação)", csv);
        Assert.Contains("Benchmarks e testes recentes", csv);
    }

    [Fact]
    public void CsvTituloDoBlocoVemAntesDoCabecalho()
    {
        var csv = ReportBuilder.Csv(Dados());

        int titulo = csv.IndexOf("Saúde dos discos", StringComparison.Ordinal);
        int cabecalho = csv.IndexOf("Disco;Detalhes;Saúde;Motivos", StringComparison.Ordinal);

        Assert.True(titulo >= 0, "o bloco de discos precisa ter título");
        Assert.True(cabecalho > titulo, "o título do bloco vem antes do cabeçalho das colunas");
    }

    [Fact]
    public void CsvUsaPontoEVirgulaComoSeparador()
    {
        var linhas = ReportBuilder.Csv(Dados()).Split("\r\n");

        Assert.Contains("Samsung SSD 990 PRO;1 TB;SAUDÁVEL;Nenhum problema encontrado", linhas);
    }

    [Fact]
    public void CsvEscreveNumeroComVirgulaDecimal()
    {
        var csv = ReportBuilder.Csv(Dados());

        Assert.Contains("53,4", csv);
        Assert.DoesNotContain("53.4", csv);
        Assert.Contains("38,4 °C", csv);
        Assert.Contains("3,5 GHz", csv);
    }

    [Fact]
    public void CsvDescolaTextoComPontoEVirgulaEaspas()
    {
        var dados = Dados();
        dados.Disks.Add(new ReportDisk("HD externo", "2 TB", "ESTÁVEL", "Atenção; disco \"lento\"", new List<KeyValuePair<string, string>>()));

        var csv = ReportBuilder.Csv(dados);

        Assert.Contains("\"Atenção; disco \"\"lento\"\"\"", csv);
    }

    [Fact]
    public void CsvDescolaValorComQuebraDeLinha()
    {
        var dados = Dados();
        dados.Disks.Add(new ReportDisk("HD externo", "2 TB", "ESTÁVEL", "primeira linha\nsegunda linha", new List<KeyValuePair<string, string>>()));

        var csv = ReportBuilder.Csv(dados);

        Assert.Contains("\"primeira linha\nsegunda linha\"", csv);
    }

    [Fact]
    public void CsvPreservaAcentos()
    {
        var csv = ReportBuilder.Csv(Dados());

        Assert.Contains("Saúde dos discos", csv);
        Assert.Contains("Relógio", csv);
        Assert.Contains("Núcleo", csv);
        Assert.Contains("Mín.", csv);
        Assert.Contains("ç", csv); // o cedilha continua cedilha no UTF-8
    }

    [Fact]
    public void CsvComecaComBom()
    {
        var csv = ReportBuilder.Csv(Dados());

        Assert.Equal('\uFEFF', csv[0]); // BOM: o Excel só lê a acentuação com ele
        Assert.StartsWith("\uFEFF", csv);

        var bytes = Encoding.UTF8.GetBytes(csv);

        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes.Take(3).ToArray());
    }

    [Fact]
    public void CsvUsaADataDoRelatorioENaoADataDoAgora()
    {
        var csv = ReportBuilder.Csv(Dados());

        Assert.Contains("05/11/2026 14:29", csv);
    }

    [Fact]
    public void CsvEhRepetivel()
    {
        var dados = Dados();

        Assert.Equal(ReportBuilder.Csv(dados), ReportBuilder.Csv(dados));
    }

    [Fact]
    public void CsvAchataASaudeDeArvoreEmSecaoItemValor()
    {
        var csv = ReportBuilder.Csv(Dados());

        Assert.Contains("Sistema operacional;Nome;Windows 11 Pro", csv);
        Assert.Contains("Sistema operacional;Relógio;3,5 GHz", csv);
        Assert.Contains("Placa de vídeo;Subtítulo;12 GB VRAM", csv);
        Assert.Contains("Placa de vídeo;Driver;551.23.1", csv);
        Assert.Contains("GPU NVIDIA;Temperatura;Núcleo;53,4;20;90", csv);
    }

    [Fact]
    public void CsvNaoTrocaOVirgulaDeVersaoComDuasCasas()
    {
        // "551.23" é versão de driver, não medida: virar "551,23" corromperia
        // o dado num relatório feito para ser lido por outros.
        var dados = Dados();
        dados.Gpus[0].Sections.Add(new ReportNode("Versão", "551.23", null));

        var csv = ReportBuilder.Csv(dados);

        Assert.Contains("Placa de vídeo;Versão;551.23", csv);
        Assert.DoesNotContain("551,23", csv);
    }

    [Fact]
    public void CsvUsaAMesmaRegraNoNumeroPuroENoNumeroDentroDoTexto()
    {
        // Uma casa vira vírgula nos dois caminhos; duas ou mais ficam. Sem
        // esta regra, "38.45 °C" virava "38,45 °C" enquanto o número puro
        // "38.45" ficava — o mesmo dado, escrito de dois jeitos.
        var dados = Dados();
        dados.System.Add(new ReportNode("Medida", "38.4 °C", null));
        dados.System.Add(new ReportNode("Medida2", "38.45 °C", null));
        dados.System.Add(new ReportNode("Tabela", "38.4", null));
        dados.System.Add(new ReportNode("Tabela2", "38.45", null));

        var csv = ReportBuilder.Csv(dados);

        Assert.Contains("38,4 °C", csv);      // uma casa: vírgula
        Assert.Contains("38.45 °C", csv);     // duas casas: fica
        Assert.Contains("Tabela;38,4", csv);  // uma casa: vírgula
        Assert.Contains("Tabela2;38.45", csv); // duas casas: fica
        Assert.DoesNotContain("38,45", csv);
    }

    [Fact]
    public void CsvNaoTrocaMilharPorDecimal()
    {
        // "12.345 GB" é milhar, não medida: não pode virar "12,345 GB".
        var dados = Dados();
        dados.System.Add(new ReportNode("Memória", "12.345 GB", null));

        var csv = ReportBuilder.Csv(dados);

        Assert.Contains("12.345 GB", csv);
        Assert.DoesNotContain("12,345 GB", csv);
    }

    [Fact]
    public void CsvTemAsMetricasDoDiscoEmBlocoProprio()
    {
        var csv = ReportBuilder.Csv(Dados());

        Assert.Contains("Saúde dos discos · métricas · Samsung SSD 990 PRO", csv);
        Assert.Contains("Temperatura;38,4 °C", csv);
        Assert.Contains("Ciclos de carga;120", csv);
    }
}
