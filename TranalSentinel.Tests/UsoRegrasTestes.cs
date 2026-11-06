using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace TranalSentinel.Tests;

/// <summary>
/// Testes do histórico de uso (que programa ficou em frente) e da regra que
/// o interpreta.
///
/// <para>
/// Os testes que gravam arquivo apontam o histórico para uma pasta
/// temporária (<see cref="UsoHistorico.PastaOverride"/>): nada aqui toca o
/// %APPDATA% do usuário. Como essa pasta é estado global, a classe fica na
/// coleção "Uso" — o xUnit executa as classes da mesma coleção em série.
/// </para>
/// </summary>
[Collection("Uso")]
public class UsoRegrasTestes : IDisposable
{
    private string? _pasta;

    private string UsarPastaTemporaria()
    {
        _pasta = Path.Combine(Path.GetTempPath(), "tranal-uso-" + Guid.NewGuid());
        UsoHistorico.PastaOverride = _pasta;
        return _pasta;
    }

    public void Dispose()
    {
        UsoHistorico.PastaOverride = null;
        UsoAmostrador.ZerarEstado();
        if (_pasta == null) return;
        try
        {
            if (Directory.Exists(_pasta)) Directory.Delete(_pasta, true);
        }
        catch
        {
            // A pasta é temporária: se o Windows ainda estiver segurando o
            // arquivo, ela some sozinha depois.
        }
    }

    /// <summary>
    /// O dia dos testes: hoje. Um dia fixo no código viraria uma bomba
    /// relógio — o histórico só guarda 30 dias, e um dia antigo seria
    /// podado no meio do teste.
    /// </summary>
    private static readonly DateTime Dia = DateTime.Today;

    private static DateTime Momento(int hora, int minuto) =>
        new(Dia.Year, Dia.Month, Dia.Day, hora, minuto, 0, DateTimeKind.Local);

    private static TrocaUso Troca(int hora, int minuto, string processo, string titulo = "") =>
        new(Momento(hora, minuto), processo, titulo);

    private static AmostraSensor Amostra(int hora, int minuto, float? tempCpu) => new(
        Momento(hora, minuto),
        tempCpu, 50f, 90f, 4000f,
        60f, 30f, 2000f, 1000f,
        50f, 0f, 0f);

    // ------------------------------------------------------------------ sessoes

    [Fact]
    public void TrocasViramSessoesComADuracaoCerta()
    {
        var trocas = new List<TrocaUso>
        {
            Troca(10, 0, "chrome"),
            Troca(10, 30, "code"),
            Troca(11, 0, "explorer")
        };

        var sessoes = UsoRegras.MontarSessoes(trocas, Momento(11, 30));

        Assert.Equal(3, sessoes.Count);
        Assert.Equal(TimeSpan.FromMinutes(30), sessoes[0].Duracao);
        Assert.Equal(TimeSpan.FromMinutes(30), sessoes[1].Duracao);
        Assert.Equal(TimeSpan.FromMinutes(30), sessoes[2].Duracao);
        Assert.Equal("chrome", sessoes[0].Processo);
        Assert.Equal("explorer", sessoes[2].Processo);
    }

    [Fact]
    public void SessaoComDuracaoZeroOuNegativaEDescartada()
    {
        // Relógio que andou para trás não pode virar "tempo em que o app
        // ficou aberto": uma troca com hora do futuro não vira sessão.
        var trocas = new List<TrocaUso>
        {
            Troca(11, 0, "chrome")
        };

        var sessoes = UsoRegras.MontarSessoes(trocas, Momento(10, 0));

        Assert.Empty(sessoes);
    }

    [Fact]
    public void TrocaForaDeOrdemEOrdenadaPeloHorario()
    {
        // O app grava em ordem cronológica; quando o relógio do Windows
        // anda para trás, a linha fora de ordem entra no lugar dela pelo
        // horário. Nenhuma sessão sai com duração zero ou negativa.
        var trocas = new List<TrocaUso>
        {
            Troca(11, 0, "chrome"),
            Troca(10, 0, "code")
        };

        var sessoes = UsoRegras.MontarSessoes(trocas, Momento(11, 30));

        Assert.Equal(2, sessoes.Count);
        Assert.Equal("code", sessoes[0].Processo);
        Assert.Equal(TimeSpan.FromHours(1), sessoes[0].Duracao);
        Assert.Equal("chrome", sessoes[1].Processo);
        Assert.Equal(TimeSpan.FromMinutes(30), sessoes[1].Duracao);
    }

    [Fact]
    public void SemTrocasNaoHaSessao()
    {
        Assert.Empty(UsoRegras.MontarSessoes(new List<TrocaUso>(), Momento(11, 30)));
    }

    // ------------------------------------------------------------------ relatorio

    [Fact]
    public void RelatorioMostraORankingEOPercentual()
    {
        var trocas = new List<TrocaUso>
        {
            Troca(10, 0, "chrome"),
            Troca(10, 30, "code"),
            Troca(10, 45, "chrome")
        };

        var relatorio = UsoRegras.Calcular(trocas, Momento(11, 0), new List<AmostraSensor>());

        Assert.Equal(2, relatorio.PorApp.Count);
        Assert.Equal("chrome", relatorio.PorApp[0].Processo);
        Assert.Equal(TimeSpan.FromMinutes(45), relatorio.PorApp[0].Tempo);
        Assert.Equal(75.0, relatorio.PorApp[0].Percentual, 2);
        Assert.Equal(25.0, relatorio.PorApp[1].Percentual, 2);
        Assert.Equal(TimeSpan.FromMinutes(60), relatorio.Total);
    }

    [Fact]
    public void RelatorioMostraAsMaioresSessoes()
    {
        var trocas = new List<TrocaUso>
        {
            Troca(9, 0, "code", "projeto.cs"),
            Troca(10, 0, "chrome", "noticias"),
            Troca(10, 10, "code", "projeto.cs")
        };

        var relatorio = UsoRegras.Calcular(trocas, Momento(10, 30), new List<AmostraSensor>());

        // A regra mostra até as 5 sessões mais longas; aqui há 3 sessões, e as
        // três entram.
        Assert.Equal(3, relatorio.MaioresSessoes.Count);
        Assert.Equal(TimeSpan.FromHours(1), relatorio.MaioresSessoes[0].Duracao);
        Assert.Equal("code", relatorio.MaioresSessoes[0].Processo);
        Assert.Equal("projeto.cs", relatorio.MaioresSessoes[0].Titulo);
    }

    [Fact]
    public void RelatorioDizOQueEstaEmDestaque()
    {
        var trocas = new List<TrocaUso>
        {
            Troca(10, 0, "chrome"),
            Troca(10, 30, "code")
        };

        var relatorio = UsoRegras.Calcular(trocas, Momento(11, 0), new List<AmostraSensor>());

        Assert.Equal("code", relatorio.EmDestaque);
    }

    [Fact]
    public void QuemFezOCalorEOCProgramaEmFrenteNoPicoDeTemperatura()
    {
        var trocas = new List<TrocaUso>
        {
            Troca(10, 0, "chrome"),
            Troca(10, 30, "code")
        };

        var amostras = new List<AmostraSensor>
        {
            Amostra(10, 10, 60f),
            Amostra(10, 40, 85f),   // o pico: o code já estava em frente
            Amostra(10, 50, 70f)
        };

        var relatorio = UsoRegras.Calcular(trocas, Momento(11, 0), amostras);

        Assert.Equal("code", relatorio.QuemFezOCalor);
        Assert.Equal(85f, relatorio.TempMaximaCpu);
    }

    [Fact]
    public void SemHistoricoDeSensoresNaoSeDiceQuemFezOCalor()
    {
        var trocas = new List<TrocaUso> { Troca(10, 0, "chrome") };

        var relatorio = UsoRegras.Calcular(trocas, Momento(11, 0), new List<AmostraSensor>());

        Assert.Equal("", relatorio.QuemFezOCalor);
        Assert.Null(relatorio.TempMaximaCpu);
    }

    [Fact]
    public void RelatorioVazioNaoErra()
    {
        var relatorio = UsoRegras.Calcular(new List<TrocaUso>(), Momento(11, 0), new List<AmostraSensor>());

        Assert.Empty(relatorio.PorApp);
        Assert.Empty(relatorio.MaioresSessoes);
        Assert.Equal(TimeSpan.Zero, relatorio.Total);
        Assert.Equal("", relatorio.EmDestaque);
    }

    // ------------------------------------------------------------------ explicacao do alerta

    [Fact]
    public void ExplicacaoDoAlertaMostraOsSensoresEOProgramaEmFrente()
    {
        var trocas = new List<TrocaUso>
        {
            Troca(10, 0, "chrome"),
            Troca(10, 30, "code")
        };

        var amostras = new List<AmostraSensor>
        {
            Amostra(10, 40, 81.5f),
            Amostra(10, 41, 82f)
        };

        var explicacao = UsoRegras.ExplicarAlerta(Momento(10, 42), amostras, trocas);

        Assert.True(explicacao.TemDados);
        Assert.Contains("CPU a 82 °C", explicacao.Resumo);
        Assert.Contains("code", explicacao.Resumo);
    }

    [Fact]
    public void ExplicacaoDoAlertaSemSensoresNaoInventaNumero()
    {
        var trocas = new List<TrocaUso> { Troca(10, 0, "chrome") };

        var explicacao = UsoRegras.ExplicarAlerta(Momento(10, 42), new List<AmostraSensor>(), trocas);

        Assert.False(explicacao.TemDados);
        Assert.Contains("chrome", explicacao.Resumo);
        Assert.DoesNotContain("°C", explicacao.Resumo);
    }

    [Fact]
    public void ProgramaEmDizQuemEstavaEmFrenteNaHora()
    {
        var trocas = new List<TrocaUso>
        {
            Troca(10, 0, "chrome"),
            Troca(10, 30, "code")
        };

        Assert.Equal("chrome", UsoRegras.ProgramaEm(Momento(10, 29), trocas));
        Assert.Equal("code", UsoRegras.ProgramaEm(Momento(10, 31), trocas));
        Assert.Equal("", UsoRegras.ProgramaEm(Momento(9, 0), trocas));
    }

    // ------------------------------------------------------------------ arquivo

    [Fact]
    public void LinhaMontadaELida_VoltaAMesmaTroca()
    {
        var original = new TrocaUso(Momento(14, 31), "chrome", "Painel - TRANAL");

        var lida = UsoHistorico.LerLinha(UsoHistorico.MontarLinha(original));

        Assert.NotNull(lida);
        Assert.Equal(original.Momento, lida!.Momento);
        Assert.Equal(original.Processo, lida.Processo);
        Assert.Equal(original.Titulo, lida.Titulo);
    }

    [Fact]
    public void TituloComSeparadorEDeQuebraDeLinhaNaoQuebraOCsv()
    {
        var original = new TrocaUso(Momento(14, 31), "code", "a;b\r\nc");

        var linha = UsoHistorico.MontarLinha(original);
        var lida = UsoHistorico.LerLinha(linha);

        Assert.Equal(3, linha.Split(';').Length);
        Assert.NotNull(lida);

        // O que importa é que o título não quebra o CSV: nem separador, nem
        // quebra de linha sobram dentro do campo.
        Assert.DoesNotContain(';', lida!.Titulo);
        Assert.DoesNotContain('\r', lida.Titulo);
        Assert.DoesNotContain('\n', lida.Titulo);
        Assert.Contains("a b", lida.Titulo);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("isto nao e uma linha")]
    [InlineData("2026-11-06T14:31:22.500")]
    [InlineData("2026-11-06 14:31:22;chrome;Painel")]
    public void LinhaInvalida_DevolveNulo(string linha)
    {
        Assert.Null(UsoHistorico.LerLinha(linha));
    }

    [Fact]
    public void GravarELer_DevolveAsTrocasDoDia()
    {
        UsarPastaTemporaria();

        UsoHistorico.Gravar(new TrocaUso(Momento(10, 0), "chrome", "Painel"));
        UsoHistorico.Gravar(new TrocaUso(Momento(10, 30), "code", "projeto.cs"));

        var lidas = UsoHistorico.Ler(Momento(10, 0));

        Assert.Equal(2, lidas.Count);
        Assert.Equal("chrome", lidas[0].Processo);
        Assert.Equal("code", lidas[1].Processo);
    }

    [Fact]
    public void LerDeOutroDiaNaoDevolveAsTrocasDeHoje()
    {
        UsarPastaTemporaria();

        // O histórico é um arquivo por dia: o arquivo de um dia não pode
        // devolver a troca que foi gravada em outro.
        UsoHistorico.Gravar(new TrocaUso(Momento(10, 0), "chrome", "Painel"));
        UsoHistorico.Gravar(new TrocaUso(Momento(10, 0).AddDays(1), "code", "Painel"));

        var lidas = UsoHistorico.Ler(Momento(10, 0).AddDays(1));

        Assert.Single(lidas);
        Assert.Equal("code", lidas[0].Processo);
    }

    [Fact]
    public void LerSemArquivoNaoErra()
    {
        UsarPastaTemporaria();

        Assert.Empty(UsoHistorico.Ler(Momento(10, 0)));
        Assert.Empty(UsoHistorico.DiasComUso());
    }

    [Fact]
    public void DiasComUsoListaODiaQueTemArquivo()
    {
        UsarPastaTemporaria();

        UsoHistorico.Gravar(new TrocaUso(Momento(10, 0), "chrome", "Painel"));

        var dias = UsoHistorico.DiasComUso();

        Assert.Single(dias);
        Assert.Equal(Momento(10, 0).Date, dias[0]);
    }

    [Fact]
    public void PodarApagaODiaAntigo()
    {
        UsarPastaTemporaria();

        string antigo = UsoHistorico.ArquivoDe(DateTime.Now.Date.AddDays(-40));
        Directory.CreateDirectory(UsoHistorico.Pasta);
        File.WriteAllText(antigo, "2026-10-01T10:00:00.000;chrome;Painel");
        File.SetLastWriteTime(antigo, DateTime.Now.AddDays(-40));

        UsoHistorico.Podar();

        Assert.False(File.Exists(antigo));
    }

    [Fact]
    public void GravarComPastaInvalidaNaoErra()
    {
        // Pasta que não pode ser criada: o registro de uso é um extra e não
        // pode derrubar o app.
        UsoHistorico.PastaOverride = "Z:\\\\inexistente\\pasta\\que\\nao\\existe";

        UsoHistorico.Gravar(new TrocaUso(Momento(10, 0), "chrome", "Painel"));
    }

    // ------------------------------------------------- o fim do período do dia

    /// <summary>Um instante de um dia qualquer (0 = hoje, 1 = ontem).</summary>
    private static DateTime MomentoDia(int diasAtras, int hora, int minuto) =>
        Dia.AddDays(-diasAtras).Date.AddHours(hora).AddMinutes(minuto);

    private static TrocaUso TrocaEm(DateTime momento, string processo, string titulo = "") =>
        new(momento, processo, titulo);

    private static AmostraSensor AmostraEm(DateTime momento, float? tempCpu) => new(
        momento,
        tempCpu, 50f, 90f, 4000f,
        60f, 30f, 2000f, 1000f,
        50f, 0f, 0f);

    [Fact]
    public void FimDoDiaDeHojeEAgora()
    {
        var agora = Momento(15, 0);

        var fim = UsoRegras.FimDoDia(Momento(14, 0), agora, new List<AmostraSensor>(), new List<TrocaUso>());

        Assert.Equal(agora, fim);
    }

    [Fact]
    public void FimDoDiaPassadoParaNaUltimaAmostraDeSensor()
    {
        // O app rodou das 8h às 9h ontem. O período não pode ir até a
        // meia-noite: as horas em que o app estava fechado não são tempo
        // de ninguém.
        var amostras = new List<AmostraSensor>
        {
            AmostraEm(MomentoDia(1, 8, 0), 80f),
            AmostraEm(MomentoDia(1, 8, 30), 81f),
            AmostraEm(MomentoDia(1, 9, 0), 82f),
        };

        var fim = UsoRegras.FimDoDia(MomentoDia(1, 0, 0), Momento(15, 0), amostras, new List<TrocaUso>());

        Assert.Equal(MomentoDia(1, 9, 0), fim);
    }

    [Fact]
    public void FimDoDiaPassadoSemSensorUsaAUltimaTroca()
    {
        var trocas = new List<TrocaUso> { TrocaEm(MomentoDia(1, 8, 0), "chrome") };

        var fim = UsoRegras.FimDoDia(MomentoDia(1, 0, 0), Momento(15, 0), new List<AmostraSensor>(), trocas);

        Assert.Equal(MomentoDia(1, 8, 0), fim);
    }

    [Fact]
    public void FimDoDiaPassadoSemNadaNaoContaNada()
    {
        // Sem amostra e sem troca, o app não rodou naquele dia: o período
        // fica vazio em vez de fingir que o dia inteiro foi observado.
        var inicio = MomentoDia(1, 0, 0);

        var fim = UsoRegras.FimDoDia(inicio, Momento(15, 0), new List<AmostraSensor>(), new List<TrocaUso>());

        Assert.Equal(inicio, fim);
    }

    [Fact]
    public void DiaPassadoNaoViraSessaoDeAteMeiaNoite()
    {
        // O defeito que a revisão adversarial achou: com o período indo
        // até a meia-noite, o chrome ficava com 15 horas de "tempo em
        // frente" — sendo que o app rodou só das 8h às 9h.
        var trocas = new List<TrocaUso> { TrocaEm(MomentoDia(1, 8, 0), "chrome") };
        var amostras = new List<AmostraSensor> { AmostraEm(MomentoDia(1, 9, 0), 80f) };

        var agora = Momento(15, 0);
        var fim = UsoRegras.FimDoDia(MomentoDia(1, 0, 0), agora, amostras, trocas);
        var relatorio = UsoRegras.Calcular(trocas, fim, amostras);

        var uso = Assert.Single(relatorio.PorApp);
        Assert.Equal("chrome", uso.Processo);
        Assert.Equal(TimeSpan.FromHours(1), uso.Tempo);
    }
}
