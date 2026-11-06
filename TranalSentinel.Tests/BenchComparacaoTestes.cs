using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace TranalSentinel.Tests;

/// <summary>
/// Testes da comparação de benchmarks entre PCs: o formato de intercâmbio e
/// a regra da comparação.
/// </summary>
public class BenchComparacaoTestes
{
    private static BenchMaquina Maquina(string cpu = "AMD Ryzen 7 5700X", string gpu = "NVIDIA GeForce RTX 3060", double memoria = 32, string so = "Windows 11 Pro")
        => new() { Nome = "PC-TESTE", Cpu = cpu, Gpu = gpu, MemoriaGb = memoria, Sistema = so };

    private static BenchArquivo Arquivo(BenchMaquina maquina, params BenchResultado[] resultados)
        => new() { App = "TRANAL SENTINEL 0.25.0", ExportadoEm = new DateTime(2026, 11, 6, 10, 0, 0), Maquina = maquina, Resultados = resultados.ToList() };

    [Fact]
    public void MesmoHardwareNaoGeraAviso()
    {
        var local = Maquina();
        var importado = Arquivo(Maquina());

        RelatorioComparacao relatorio = BenchComparacao.Comparar(local, importado, Array.Empty<BenchResultado>());

        Assert.True(relatorio.Comparavel);
        Assert.Empty(relatorio.Diferencas);
    }

    [Fact]
    public void CpuDiferenteGeraAviso()
    {
        RelatorioComparacao relatorio = BenchComparacao.Comparar(
            Maquina(),
            Arquivo(Maquina(cpu: "Intel Core i5-13400")),
            Array.Empty<BenchResultado>());

        Assert.False(relatorio.Comparavel);
        Assert.Contains(relatorio.Diferencas, d => d.Contains("CPU diferente"));
    }

    [Fact]
    public void MemoriaEBibliotecaPequenaNaoGeramAviso()
    {
        // 32 GB contra 31,9 GB é a mesma máquina (o Windows arredonda).
        RelatorioComparacao relatorio = BenchComparacao.Comparar(
            Maquina(memoria: 32),
            Arquivo(Maquina(memoria: 31.9)),
            Array.Empty<BenchResultado>());

        Assert.True(relatorio.Comparavel);
    }

    [Fact]
    public void MemoriaBemDiferenteGeraAviso()
    {
        RelatorioComparacao relatorio = BenchComparacao.Comparar(
            Maquina(memoria: 32),
            Arquivo(Maquina(memoria: 16)),
            Array.Empty<BenchResultado>());

        Assert.Contains(relatorio.Diferencas, d => d.Contains("Memória diferente"));
    }

    [Fact]
    public void ComparaOMelhorResultadoDeCadaTeste()
    {
        var locais = new List<BenchResultado>
        {
            new() { Chave = "gpu", Nota = 100, Data = new DateTime(2026, 1, 1) },
            new() { Chave = "gpu", Nota = 150, Data = new DateTime(2026, 2, 1) },
            new() { Chave = "gpu", Nota = 120, Data = new DateTime(2026, 3, 1) }
        };

        var importado = Arquivo(Maquina(), new BenchResultado { Chave = "gpu", Nota = 100 });

        RelatorioComparacao relatorio = BenchComparacao.Comparar(Maquina(), importado, locais);

        Assert.Single(relatorio.Linhas);
        Assert.Equal(150, relatorio.Linhas[0].NotaLocal);
        Assert.Equal(100, relatorio.Linhas[0].NotaImportada);
        Assert.Equal(50, relatorio.Linhas[0].DiferencaPercentual, 3);
        Assert.Equal("mais rápido", relatorio.Linhas[0].Veredito);
    }

    [Fact]
    public void DiferencaPequenaContaComoEmpate()
    {
        var locais = new List<BenchResultado> { new() { Chave = "gpu", Nota = 102 } };
        var importado = Arquivo(Maquina(), new BenchResultado { Chave = "gpu", Nota = 100 });

        RelatorioComparacao relatorio = BenchComparacao.Comparar(Maquina(), importado, locais);

        Assert.Equal("empate", relatorio.Linhas[0].Veredito);
    }

    [Fact]
    public void PCMaisLentoFicaMarcado()
    {
        var locais = new List<BenchResultado> { new() { Chave = "gpu", Nota = 80 } };
        var importado = Arquivo(Maquina(), new BenchResultado { Chave = "gpu", Nota = 100 });

        RelatorioComparacao relatorio = BenchComparacao.Comparar(Maquina(), importado, locais);

        Assert.Equal("mais lento", relatorio.Linhas[0].Veredito);
        Assert.Equal(-20, relatorio.Linhas[0].DiferencaPercentual, 3);
    }

    [Fact]
    public void TesteQueSoExisteEmUmLadoFicaSeparado()
    {
        var locais = new List<BenchResultado> { new() { Chave = "mem", Nota = 500 } };
        var importado = Arquivo(Maquina(), new BenchResultado { Chave = "gpu", Nota = 100 });

        RelatorioComparacao relatorio = BenchComparacao.Comparar(Maquina(), importado, locais);

        Assert.Empty(relatorio.Linhas);
        Assert.Contains("Memória e carregamento", relatorio.SoLocal);
        Assert.Contains("Cena de jogo (GPU)", relatorio.SoImportado);
    }

    [Fact]
    public void ORenderViraDuasChavesDeMultiESingle()
    {
        var renders = new[]
        {
            new BenchRun(new DateTime(2026, 5, 1), 1234.5, 567.8, false, "AMD Ryzen 7 5700X")
        };

        List<BenchResultado> resultados = BenchIntercambio.Resultados(renders, Array.Empty<BenchRecord>());

        Assert.Equal(2, resultados.Count);
        Assert.Contains(resultados, r => r.Chave == "render:multi" && r.Nota == 1234.5);
        Assert.Contains(resultados, r => r.Chave == "render:single" && r.Nota == 567.8);
        Assert.All(resultados, r => Assert.Contains("5700X", r.Resumo));
    }

    [Fact]
    public void ORenderTemOCpuDaExecucaoNoResumo()
    {
        var renders = new[]
        {
            new BenchRun(new DateTime(2026, 5, 1), 1234.5, null, false, "Intel Core i5-13400")
        };

        List<BenchResultado> resultados = BenchIntercambio.Resultados(renders, Array.Empty<BenchRecord>());

        BenchResultado multi = resultados.Single(r => r.Chave == "render:multi");
        Assert.Contains("13400", multi.Resumo);
    }

    [Fact]
    public void ArquivoExportadoVoltaIgualAoImportado()
    {
        string caminho = Path.Combine(Path.GetTempPath(), $"tranal-bench-{Guid.NewGuid():N}.tranalbench.json");

        try
        {
            var renders = new[] { new BenchRun(new DateTime(2026, 5, 1), 1234.5, 567.8, false, "AMD Ryzen 7 5700X") };
            var registros = new[] { new BenchRecord(new DateTime(2026, 5, 2), "gpu", 4321, "2.500 pts") };

            BenchArquivo original = BenchIntercambio.Montar(renders, registros, Maquina());
            BenchIntercambio.Gravar(original, caminho);

            BenchArquivo? lido = BenchIntercambio.Ler(caminho, out string? erro);

            Assert.NotNull(lido);
            Assert.Null(erro);
            Assert.Equal(BenchIntercambio.Formato, lido!.Formato);
            Assert.Equal(BenchIntercambio.Versao, lido.Versao);
            Assert.Equal("AMD Ryzen 7 5700X", lido.Maquina.Cpu);
            Assert.Equal(3, lido.Resultados.Count);

            // E a comparação entre os dois lados funciona com o arquivo em disco.
            RelatorioComparacao relatorio = BenchComparacao.Comparar(
                Maquina(),
                lido,
                BenchIntercambio.Resultados(renders, registros));

            Assert.Equal(3, relatorio.Linhas.Count);
            Assert.All(relatorio.Linhas, l => Assert.Equal("empate", l.Veredito));
        }
        finally
        {
            if (File.Exists(caminho)) File.Delete(caminho);
        }
    }

    [Fact]
    public void ArquivoDeOutroFormatoEReceusado()
    {
        string caminho = Path.Combine(Path.GetTempPath(), $"tranal-bench-{Guid.NewGuid():N}.tranalbench.json");

        try
        {
            File.WriteAllText(caminho, """{ "Formato": "outro-coisa", "Versao": 1, "Resultados": [] }""");

            BenchArquivo? lido = BenchIntercambio.Ler(caminho, out string? erro);

            Assert.Null(lido);
            Assert.Contains("não é de benchmarks", erro);
        }
        finally
        {
            if (File.Exists(caminho)) File.Delete(caminho);
        }
    }

    [Fact]
    public void VersaoDoFormatoMaisNovaQueOAppEReceusada()
    {
        string caminho = Path.Combine(Path.GetTempPath(), $"tranal-bench-{Guid.NewGuid():N}.tranalbench.json");

        try
        {
            File.WriteAllText(caminho, $$"""{ "Formato": "tranal-bench", "Versao": {{BenchIntercambio.Versao + 1}}, "Resultados": [] }""");

            BenchArquivo? lido = BenchIntercambio.Ler(caminho, out string? erro);

            Assert.Null(lido);
            Assert.Contains("Atualize o TRANAL SENTINEL", erro);
        }
        finally
        {
            if (File.Exists(caminho)) File.Delete(caminho);
        }
    }

    [Fact]
    public void ArquivoInexistenteNaoQuebra()
    {
        BenchArquivo? lido = BenchIntercambio.Ler(Path.Combine(Path.GetTempPath(), "nao-existe-tranal.json"), out string? erro);

        Assert.Null(lido);
        Assert.NotNull(erro);
    }

    [Fact]
    public void ONomeDoTesteVemDoCatalogo()
    {
        Assert.Equal("Cena de jogo (GPU)", BenchCatalogo.Nome("gpu"));
        Assert.Equal("Ray tracing (GPU) · medium 1080", BenchCatalogo.Nome("rt:medium:1080"));
        Assert.Equal("Render (CPU) · multi-core", BenchCatalogo.Nome("render:multi"));
        Assert.Equal("Render (CPU) · single-core", BenchCatalogo.Nome("render:single"));
        Assert.Equal("teste-desconhecido", BenchCatalogo.Nome("teste-desconhecido"));
    }

    [Fact]
    public void TestesComDetalhesNaoRepetemONome()
    {
        // O render de multi-core e o de single-core (e os dois ray tracing)
        // precisam de rótulos diferentes: senão a comparação mostra duas
        // linhas com o mesmo nome.
        Assert.NotEqual(BenchCatalogo.Nome("render:multi"), BenchCatalogo.Nome("render:single"));
        Assert.NotEqual(BenchCatalogo.Nome("rt:medium:1080"), BenchCatalogo.Nome("rt:ultra:2160"));
    }
}
