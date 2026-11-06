using System;
using System.IO;
using Xunit;

namespace TranalSentinel.Tests;

/// <summary>
/// Testes da regra de alerta guardada no PC do usuário.
///
/// <para>
/// O que estes testes cobrem: as regras têm que sobreviver a uma ida e
/// volta no <c>settings.json</c> (o usuário não pode perder a configuração
/// quando o app fecha) e a lista vazia tem que continuar vazia — lista
/// vazia é o usuário apagando tudo, não "nunca configurado".
///
/// </para>
/// <para>
/// Estes testes mexem na pasta das configurações de verdade, então ela é
/// redirecionada para uma pasta temporária. Como a pasta é um estado
/// global, a classe fica na coleção do xUnit: duas classes que usam o mesmo
/// redirecionamento não podem rodar em paralelo.
/// </para>
/// </summary>
[Collection("Configuracoes")]
public class RegraAlertaTestes : IDisposable
{
    private readonly string _pasta;

    public RegraAlertaTestes()
    {
        _pasta = Path.Combine(Path.GetTempPath(), "tranal-testes-alertas-" + Guid.NewGuid().ToString("N"));
        AppSettings.PastaOverride = _pasta;
    }

    public void Dispose()
    {
        AppSettings.PastaOverride = null;
        try { if (Directory.Exists(_pasta)) System.IO.Directory.Delete(_pasta, true); }
        catch { /* a pasta temporária vai embora sozinha */ }
    }

    [Fact]
    public void RegrasSobrevivemAUmaIdaEVoltaNoSettings()
    {
        var s = new AppSettings
        {
            Alertas = new List<RegraAlerta>
            {
                new() { Id = "r1", Sensor = "gpu-temp", Operacao = ">=", Limite = 90, SegundosMinimos = 120, Ativa = true, Severidade = Severidade.Critico, Rotulo = "GPU no limite" },
                new() { Id = "r2", Sensor = "ram-uso", Operacao = ">=", Limite = 75, SegundosMinimos = 0, Ativa = false, Severidade = Severidade.Baixo, Rotulo = null },
            }
        };

        s.Save();
        var lida = AppSettings.Load();

        Assert.NotNull(lida.Alertas);
        Assert.Equal(2, lida.Alertas!.Count);

        var primeira = lida.Alertas[0];
        Assert.Equal("r1", primeira.Id);
        Assert.Equal("gpu-temp", primeira.Sensor);
        Assert.Equal(">=", primeira.Operacao);
        Assert.Equal(90, primeira.Limite);
        Assert.Equal(120, primeira.SegundosMinimos);
        Assert.True(primeira.Ativa);
        Assert.Equal(Severidade.Critico, primeira.Severidade);
        Assert.Equal("GPU no limite", primeira.Rotulo);

        var segunda = lida.Alertas[1];
        Assert.Equal("r2", segunda.Id);
        Assert.False(segunda.Ativa);
        Assert.Equal(Severidade.Baixo, segunda.Severidade);
        Assert.Null(segunda.Rotulo);
    }

    [Fact]
    public void QuemNuncaConfigurouNaoTemListaDeRegras()
    {
        // Null = "nunca configurado" (o app usa as regras padrão).
        var s = new AppSettings();
        s.Save();

        var lida = AppSettings.Load();

        Assert.Null(lida.Alertas);
    }

    [Fact]
    public void ListaVaziaDeRegrasContinuaVazia()
    {
        // O usuário apagou tudo: a lista vazia tem que voltar vazia (e não
        // virar "nunca configurado").
        var s = new AppSettings { Alertas = new List<RegraAlerta>() };
        s.Save();

        var lida = AppSettings.Load();

        Assert.NotNull(lida.Alertas);
        Assert.Empty(lida.Alertas!);
    }

    [Fact]
    public void AsRegrasPadraoTambemSobrevivemAoArquivo()
    {
        var s = new AppSettings { Alertas = RegrasAlertaPadrao.Criar() };
        s.Save();

        var lida = AppSettings.Load();

        Assert.NotNull(lida.Alertas);
        Assert.Equal(3, lida.Alertas!.Count);
        Assert.Equal(85, lida.Alertas.Single(r => r.Id == "padrao-cpu").Limite);
        Assert.Equal(60, lida.Alertas.Single(r => r.Id == "padrao-disco").Limite);
        Assert.Equal(20, lida.Alertas.Single(r => r.Id == "padrao-bateria").Limite);
    }

    [Fact]
    public void RegraNovaTemOsCamposDoJeito()
    {
        var regra = new RegraAlerta();

        Assert.Equal("", regra.Id);
        Assert.Equal("", regra.Sensor);
        Assert.Equal(">=", regra.Operacao);
        Assert.Equal(0, regra.Limite);
        Assert.Equal(0, regra.SegundosMinimos);
        Assert.True(regra.Ativa);
        Assert.Equal(Severidade.Medio, regra.Severidade);
        Assert.Null(regra.Rotulo);
    }

    [Fact]
    public void GravacaoAoMesmoTempoNaoDeixaOArquivoPelaMetade()
    {
        // A tela e a vigia gravam as configurações de threads diferentes. Se
        // as duas gravarem ao mesmo tempo, o arquivo pode ficar pela metade
        // e o usuário perde tudo (a leitura engole o erro e volta ao padrão).
        var s = new AppSettings
        {
            Alertas = new List<RegraAlerta>
            {
                new() { Id = "r1", Sensor = "cpu-temp", Operacao = ">=", Limite = 85, Ativa = true, Severidade = Severidade.Medio },
            },
            LightTheme = true,
        };

        Parallel.For(0, 40, _ => s.Save());

        var lida = AppSettings.Load();
        Assert.NotNull(lida.Alertas);
        Assert.Single(lida.Alertas!);
        Assert.Equal("r1", lida.Alertas![0].Id);
        Assert.True(lida.LightTheme);
    }
}
