using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace TranalSentinel.Tests;

/// <summary>
/// Testes da nota de saúde do PC e do histórico dela.
///
/// <para>
/// O histórico mexe em arquivo, então a pasta é redirecionada para uma pasta
/// temporária: o teste não pode escrever no log de verdade do usuário. Como
/// o redirecionamento é estado global, a classe fica na coleção do xUnit que
/// roda em série.
/// </para>
/// </summary>
[Collection("Configuracoes")]
public class SaudeNotaTestes : IDisposable
{
    private readonly string _pasta;

    public SaudeNotaTestes()
    {
        _pasta = Path.Combine(Path.GetTempPath(), "tranal-testes-saude-nota-" + Guid.NewGuid().ToString("N"));
        SaudeNotaHistorico.PastaOverride = _pasta;
    }

    public void Dispose()
    {
        SaudeNotaHistorico.PastaOverride = null;
        try { if (Directory.Exists(_pasta)) Directory.Delete(_pasta, true); }
        catch { /* a pasta temporária vai embora sozinha */ }
    }

    private static List<Achado> Achados(params Severidade[] severidades) =>
        severidades.Select(s => new Achado { Severidade = s }).ToList();

    // ------------------------------------------------------------------- nota

    [Fact]
    public void SemAchados_Nota100EConceitoA()
    {
        var nota = SaudeNota.Calcular(new List<Achado>());

        Assert.Equal(100, nota.Nota);
        Assert.Equal("A", nota.Conceito);
        Assert.Contains("nota 100 (A)", nota.Resumo);
    }

    [Theory]
    [InlineData(Severidade.Critico, 20)]
    [InlineData(Severidade.Alto, 10)]
    [InlineData(Severidade.Medio, 5)]
    [InlineData(Severidade.Baixo, 2)]
    [InlineData(Severidade.Informativo, 0)]
    public void CadaSeveridade_DescontaOPesoCerto(Severidade severidade, int desconto)
    {
        var nota = SaudeNota.Calcular(Achados(severidade));

        Assert.Equal(desconto, SaudeNota.Desconto(severidade));
        Assert.Equal(100 - desconto, nota.Nota);
    }

    [Fact]
    public void VinteCriticos_NotaZeroEConceitoE()
    {
        var achados = Achados(Enumerable.Repeat(Severidade.Critico, 20).ToArray());

        var nota = SaudeNota.Calcular(achados);

        Assert.Equal(0, nota.Nota);
        Assert.Equal("E", nota.Conceito);
    }

    [Fact]
    public void NotaNuncaPassaDe100()
    {
        // Informativo não desconta nada: mesmo com muitos achados, a nota
        // fica em 100 (e nunca acima).
        var achados = Achados(Enumerable.Repeat(Severidade.Informativo, 50).ToArray());

        Assert.Equal(100, SaudeNota.Calcular(achados).Nota);
        Assert.Equal(100, SaudeNota.CalcularNota(achados));
    }

    [Theory]
    [InlineData(100, "A")]
    [InlineData(90, "A")]
    [InlineData(89, "B")]
    [InlineData(75, "B")]
    [InlineData(74, "C")]
    [InlineData(60, "C")]
    [InlineData(59, "D")]
    [InlineData(40, "D")]
    [InlineData(39, "E")]
    [InlineData(0, "E")]
    public void Conceito_RespeitaOsLimiares(int nota, string conceito)
    {
        Assert.Equal(conceito, SaudeNota.Conceito(nota));
    }

    [Fact]
    public void Resumo_EmUmaFraseComAContagemEANota()
    {
        var nota = SaudeNota.Calcular(Achados(Severidade.Critico, Severidade.Alto));

        Assert.Equal(70, nota.Nota);
        Assert.Equal("C", nota.Conceito);
        Assert.Contains("1 crítico(s)", nota.Resumo);
        Assert.Contains("1 alto(s)", nota.Resumo);
        Assert.Contains("nota 70 (C)", nota.Resumo);
    }

    [Fact]
    public void Resumo_SemAchados_DizQueNaoAchouNada()
    {
        var nota = SaudeNota.Calcular(new List<Achado>());

        Assert.Contains("Nenhum problema encontrado", nota.Resumo);
    }

    // -------------------------------------------------------------- tendência

    [Theory]
    [InlineData(80, 80, "estável")]
    [InlineData(85, 80, "subiu 5 pontos")]
    [InlineData(81, 80, "subiu 1 ponto")]
    [InlineData(77, 80, "caiu 3 pontos")]
    [InlineData(79, 80, "caiu 1 ponto")]
    public void Tendencia_DescreveADirecao(int atual, int anterior, string esperado)
    {
        Assert.Equal(esperado, SaudeNota.Tendencia(atual, anterior));
    }

    [Fact]
    public void Tendencia_SemNotaAnterior_DizQueNaoHaHistorico()
    {
        Assert.Equal("sem histórico", SaudeNota.Tendencia(80, null));
    }

    [Fact]
    public void TendenciaDe_ComparaComANotaAnteriorMaisRecente()
    {
        SaudeNotaHistorico.Gravar(new DateTime(2026, 11, 3), 90);
        SaudeNotaHistorico.Gravar(new DateTime(2026, 11, 4), 70);

        Assert.Equal("subiu 10 pontos", SaudeNotaHistorico.TendenciaDe(80));
    }

    // --------------------------------------------------------------- histórico

    [Fact]
    public void Historico_GravaELe()
    {
        SaudeNotaHistorico.Gravar(new DateTime(2026, 11, 3), 90);
        SaudeNotaHistorico.Gravar(new DateTime(2026, 11, 4), 75);

        var entradas = SaudeNotaHistorico.Ler();

        Assert.Equal(2, entradas.Count);
        Assert.Equal(new DateTime(2026, 11, 3), entradas[0].Dia);
        Assert.Equal(90, entradas[0].Nota);
        Assert.Equal(new DateTime(2026, 11, 4), entradas[1].Dia);
        Assert.Equal(75, entradas[1].Nota);
    }

    [Fact]
    public void Historico_UmDiaPorLinha()
    {
        SaudeNotaHistorico.Gravar(new DateTime(2026, 11, 3), 90);
        SaudeNotaHistorico.Gravar(new DateTime(2026, 11, 4), 75);

        var linhas = File.ReadAllLines(SaudeNotaHistorico.Arquivo);

        Assert.Equal(2, linhas.Length);
        Assert.Contains("2026-11-03", linhas[0]);
        Assert.Contains("2026-11-04", linhas[1]);
    }

    [Fact]
    public void Historico_NaoDuplicaOMesmoDia()
    {
        SaudeNotaHistorico.Gravar(new DateTime(2026, 11, 5), 80);
        SaudeNotaHistorico.Gravar(new DateTime(2026, 11, 5), 60);

        var entradas = SaudeNotaHistorico.Ler();

        var doDia = Assert.Single(entradas, e => e.Dia == new DateTime(2026, 11, 5));
        Assert.Equal(60, doDia.Nota);   // atualizou, não duplicou
    }

    [Fact]
    public void Historico_LeAsUltimasN()
    {
        for (int dia = 1; dia <= 5; dia++)
            SaudeNotaHistorico.Gravar(new DateTime(2026, 11, dia), 50 + dia);

        var entradas = SaudeNotaHistorico.Ler(2);

        Assert.Equal(2, entradas.Count);
        Assert.Equal(new DateTime(2026, 11, 4), entradas[0].Dia);
        Assert.Equal(new DateTime(2026, 11, 5), entradas[1].Dia);
    }

    [Fact]
    public void Historico_FicaNaPastaRedirecionada_EOuNaoNoLogDoUsuario()
    {
        string arquivoDoUsuario = Path.Combine(AppSettings.Folder, "log", "tranal-saude-nota.log");
        bool existiaAntes = File.Exists(arquivoDoUsuario);

        SaudeNotaHistorico.GravarHoje(88);

        Assert.StartsWith(_pasta, SaudeNotaHistorico.Arquivo);
        Assert.True(File.Exists(SaudeNotaHistorico.Arquivo));
        // O log de verdade do usuário não foi tocado.
        Assert.Equal(existiaAntes, File.Exists(arquivoDoUsuario));
    }

    [Fact]
    public void Historico_ComPastaInvalida_NaoEstoura()
    {
        // A "pasta" do histórico é, na verdade, um arquivo: o CreateDirectory
        // tem que falhar. Gravar e ler não podem estourar (o histórico é
        // acessório; derrubar o app, não).
        string pastaFalsa = Path.Combine(_pasta, "arquivo", "no-lugar");
        Directory.CreateDirectory(pastaFalsa);
        string arquivo = Path.Combine(pastaFalsa, "gugu.txt");
        File.WriteAllText(arquivo, "sou um arquivo, nao uma pasta");
        SaudeNotaHistorico.PastaOverride = arquivo;

        SaudeNotaHistorico.Gravar(DateTime.Today, 70);   // não pode lançar
        var entradas = SaudeNotaHistorico.Ler();         // nem na leitura

        Assert.Empty(entradas);
    }

    [Fact]
    public void Historico_ComArquivoLixo_NaoEstoura()
    {
        Directory.CreateDirectory(_pasta);
        File.WriteAllLines(SaudeNotaHistorico.Arquivo, new[]
        {
            "isto nao e uma linha valida",
            "2026-13-45;abc",
            "2026-11-05;77",
        });

        var entradas = SaudeNotaHistorico.Ler();

        var entrada = Assert.Single(entradas);
        Assert.Equal(new DateTime(2026, 11, 5), entrada.Dia);
        Assert.Equal(77, entrada.Nota);
    }
}
