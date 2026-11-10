using System;
using System.IO;
using TranalSentinel;
using Xunit;

namespace TranalSentinel.Tests;

/// <summary>
/// Testes das configurações.
///
/// <para>
/// O que estes testes cobrem: gravar as configurações nunca pode derrubar o
/// app (disco cheio, arquivo bloqueado), e um número não finito (uma nota de
/// benchmark que deu NaN) não pode estragar o arquivo inteiro — o valor vai
/// para o disco como literal nomeado e volta na leitura.
/// </para>
///
/// <para>
/// Estes testes mexem na pasta das configurações de verdade, então ela é
/// redirecionada para uma pasta temporária. Como a pasta é um estado global,
/// a classe fica numa coleção do xUnit: duas classes que usam o mesmo
/// redirecionamento não podem rodar em paralelo.
/// </para>
/// </summary>
[Collection("Configuracoes")]
public class AppSettingsTestes : IDisposable
{
    private readonly string _pasta;

    public AppSettingsTestes()
    {
        _pasta = Path.Combine(Path.GetTempPath(), "tranal-testes-settings-" + Guid.NewGuid().ToString("N"));
        AppSettings.PastaOverride = _pasta;
    }

    public void Dispose()
    {
        AppSettings.PastaOverride = null;
        try { if (Directory.Exists(_pasta)) Directory.Delete(_pasta, true); }
        catch { /* a pasta temporária vai embora sozinha */ }
    }

    [Fact]
    public void Save_GravaOArquivo()
    {
        var s = new AppSettings { OverlayScale = 1.5 };
        s.Save();

        Assert.True(File.Exists(Path.Combine(_pasta, "settings.json")));
    }

    [Fact]
    public void Save_ComNumeroNaoFinito_NaoEstouraEVoltaNaLeitura()
    {
        // A regressão: um NaN na nota do benchmark fazia o JsonSerializer
        // estourar — e o Save() era chamado sem proteção em vários lugares.
        var s = new AppSettings
        {
            BenchRecords = new List<BenchRecord>
            {
                new(DateTime.Now, "gpu", double.NaN, "nota quebrada")
            }
        };

        s.Save();   // não pode lançar

        var lida = AppSettings.Load();
        Assert.Single(lida.BenchRecords);
        Assert.True(double.IsNaN(lida.BenchRecords[0].Score));
    }

    [Fact]
    public void Save_ComInfinito_NaoEstoura()
    {
        var s = new AppSettings
        {
            BenchRecords = new List<BenchRecord>
            {
                new(DateTime.Now, "sim", double.PositiveInfinity, "nota quebrada")
            }
        };

        s.Save();

        var lida = AppSettings.Load();
        Assert.Single(lida.BenchRecords);
        Assert.True(double.IsPositiveInfinity(lida.BenchRecords[0].Score));
    }

    [Fact]
    public void Save_ComPastaInvalida_NaoEstoura()
    {
        // A "pasta" das configurações é, na verdade, um arquivo: o CreateDirectory
        // tem que falhar. O Save() tem que engolir o erro (gravar configuração
        // é uma conveniência; derrubar o app, não).
        string pastaFalsa = Path.Combine(_pasta, "arquivo", "no-lugar");
        Directory.CreateDirectory(pastaFalsa);
        string arquivo = Path.Combine(pastaFalsa, "gugu.txt");
        File.WriteAllText(arquivo, "sou um arquivo, nao uma pasta");
        AppSettings.PastaOverride = arquivo;

        var s = new AppSettings { OverlayScale = 2 };
        s.Save();   // não pode lançar
    }

    [Fact]
    public void Load_SemArquivo_DevolvePadrao()
    {
        var lida = AppSettings.Load();

        Assert.NotNull(lida);
        Assert.Equal(1.0, lida.OverlayScale);
    }

    [Fact]
    public void ConfiguracoesDoPainel_VoltamAoAbrirOApp()
    {
        var s = new AppSettings
        {
            PainelVisivel = true,
            PainelSempreNoTopo = false,
            PainelX = 120.5,
            PainelY = 64.0
        };
        s.Save();

        var lido = AppSettings.Load();
        Assert.True(lido.PainelVisivel);
        Assert.False(lido.PainelSempreNoTopo);
        Assert.Equal(120.5, lido.PainelX);
        Assert.Equal(64.0, lido.PainelY);
    }
}
