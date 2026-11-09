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
/// Os testes que gravam apontam o banco de dados para uma pasta temporária
/// (<see cref="Banco.PastaOverride"/>): nada aqui toca o %APPDATA% do
/// usuário. Como essa pasta é estado global, a classe fica na coleção
/// "Banco" — o xUnit executa as classes da mesma coleção em série.
/// </para>
/// </summary>
[Collection("Banco")]
public class UsoRegrasTestes : IDisposable
{
    private string? _pasta;
    private string? _pastaLegada;

    private string UsarPastaTemporaria()
    {
        (_pasta, _pastaLegada) = BancoDeTeste.Isolar();
        return _pasta;
    }

    public void Dispose()
    {
        BancoDeTeste.Restaurar();
        UsoAmostrador.ZerarEstado();

        foreach (string? pasta in new[] { _pasta, _pastaLegada })
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

        var sessoes = UsoRegras.MontarSessoes(trocas, Momento(0, 0), Momento(11, 30));

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

        var sessoes = UsoRegras.MontarSessoes(trocas, Momento(0, 0), Momento(10, 0));

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

        var sessoes = UsoRegras.MontarSessoes(trocas, Momento(0, 0), Momento(11, 30));

        Assert.Equal(2, sessoes.Count);
        Assert.Equal("code", sessoes[0].Processo);
        Assert.Equal(TimeSpan.FromHours(1), sessoes[0].Duracao);
        Assert.Equal("chrome", sessoes[1].Processo);
        Assert.Equal(TimeSpan.FromMinutes(30), sessoes[1].Duracao);
    }

    [Fact]
    public void SemTrocasNaoHaSessao()
    {
        Assert.Empty(UsoRegras.MontarSessoes(new List<TrocaUso>(), Momento(0, 0), Momento(11, 30)));
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

        var relatorio = UsoRegras.Calcular(trocas, Momento(0, 0), Momento(11, 0), new List<AmostraSensor>());

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

        var relatorio = UsoRegras.Calcular(trocas, Momento(0, 0), Momento(10, 30), new List<AmostraSensor>());

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

        var relatorio = UsoRegras.Calcular(trocas, Momento(0, 0), Momento(11, 0), new List<AmostraSensor>());

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

        var relatorio = UsoRegras.Calcular(trocas, Momento(0, 0), Momento(11, 0), amostras);

        Assert.Equal("code", relatorio.QuemFezOCalor);
        Assert.Equal(85f, relatorio.TempMaximaCpu);
    }

    [Fact]
    public void SemHistoricoDeSensoresNaoSeDiceQuemFezOCalor()
    {
        var trocas = new List<TrocaUso> { Troca(10, 0, "chrome") };

        var relatorio = UsoRegras.Calcular(trocas, Momento(0, 0), Momento(11, 0), new List<AmostraSensor>());

        Assert.Equal("", relatorio.QuemFezOCalor);
        Assert.Null(relatorio.TempMaximaCpu);
    }

    [Fact]
    public void RelatorioVazioNaoErra()
    {
        var relatorio = UsoRegras.Calcular(new List<TrocaUso>(), Momento(0, 0), Momento(11, 0), new List<AmostraSensor>());

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
    public void ExplicacaoDoAlertaComHistoricoNaoLidoNaoExiste()
    {
        var trocas = new Leitura<TrocaUso>(new List<TrocaUso> { Troca(10, 0, "chrome") }, true);
        var amostras = new Leitura<AmostraSensor>(
            new List<AmostraSensor> { Amostra(10, 40, 81.5f), Amostra(10, 41, 82f) }, true);

        // O histórico deu para ler: a explicação existe.
        var comDados = UsoRegras.ExplicarAlerta(Momento(10, 42), amostras, trocas);
        Assert.NotNull(comDados);
        Assert.True(comDados!.TemDados);

        // O histórico NÃO deu para ler: não há explicação. A explicação
        // inventada ("o app não estava gravando sensores nesse momento")
        // seria uma afirmação falsa sobre o histórico de quem está usando
        // o computador — e ela ficaria em cache, errada, mesmo depois do
        // banco consertado.
        var leituraQuebrada = new Leitura<AmostraSensor>(new List<AmostraSensor>(), false);
        var semExplicacao = UsoRegras.ExplicarAlerta(Momento(10, 42), leituraQuebrada, trocas);

        Assert.Null(semExplicacao);

        // O mesmo vale quando quem não pôde ser lido é o histórico de uso.
        var trocasQuebradas = new Leitura<TrocaUso>(new List<TrocaUso>(), false);
        Assert.Null(UsoRegras.ExplicarAlerta(Momento(10, 42), amostras, trocasQuebradas));
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

    // ------------------------------------------------------------------ histórico

    [Fact]
    public void GravarELer_DevolveAsTrocasDoDia()
    {
        UsarPastaTemporaria();

        UsoHistorico.Gravar(new TrocaUso(Momento(10, 0), "chrome", "Painel"));
        UsoHistorico.Gravar(new TrocaUso(Momento(10, 30), "code", "projeto.cs"));

        var lidas = UsoHistorico.Ler(Momento(10, 0)).Linhas;

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

        var lidas = UsoHistorico.Ler(Momento(10, 0).AddDays(1)).Linhas;

        Assert.Single(lidas);
        Assert.Equal("code", lidas[0].Processo);
    }

    [Fact]
    public void LerSemArquivoNaoErra()
    {
        UsarPastaTemporaria();

        Assert.Empty(UsoHistorico.Ler(Momento(10, 0)).Linhas);
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
    public void PodarApagaOQueEAntigo()
    {
        UsarPastaTemporaria();

        var antigo = Momento(10, 0).AddDays(-40);
        UsoHistorico.Gravar(new TrocaUso(antigo, "chrome", "Painel"));
        UsoHistorico.Gravar(new TrocaUso(Momento(10, 0), "code", "Painel"));

        UsoHistorico.Podar();

        Assert.Empty(UsoHistorico.Ler(antigo).Linhas);
        Assert.Single(UsoHistorico.Ler(Momento(10, 0)).Linhas);
    }

    [Fact]
    public void GravarComPastaInvalidaNaoErra()
    {
        // Pasta que não pode ser criada: o registro de uso é um extra e não
        // pode derrubar o app.
        Banco.PastaOverride = "Z:\\\\inexistente\\pasta\\que\\nao\\existe";
        Banco.Fechar();

        try
        {
            Assert.False(UsoHistorico.Gravar(new TrocaUso(Momento(10, 0), "chrome", "Painel")));
        }
        finally
        {
            BancoDeTeste.Restaurar();
        }
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
    public void PeriodoDeHojeVaiAteAgora()
    {
        var agora = Momento(15, 0);

        var periodo = UsoRegras.Periodo(Momento(14, 0), agora, new List<AmostraSensor>(), new List<TrocaUso>());

        Assert.Equal(agora, periodo.Fim);
    }

    [Fact]
    public void PeriodoPassadoVaiDaPrimeiraEvidenciaAteAUltima()
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

        var periodo = UsoRegras.Periodo(MomentoDia(1, 0, 0), Momento(15, 0), amostras, new List<TrocaUso>());

        Assert.Equal(MomentoDia(1, 8, 0), periodo.Inicio);
        Assert.Equal(MomentoDia(1, 9, 0), periodo.Fim);
    }

    [Fact]
    public void PeriodoPassadoSemSensorUsaATroca()
    {
        var trocas = new List<TrocaUso> { TrocaEm(MomentoDia(1, 8, 0), "chrome") };

        var periodo = UsoRegras.Periodo(MomentoDia(1, 0, 0), Momento(15, 0), new List<AmostraSensor>(), trocas);

        Assert.Equal(MomentoDia(1, 8, 0), periodo.Inicio);
        Assert.Equal(MomentoDia(1, 8, 0), periodo.Fim);
    }

    [Fact]
    public void PeriodoPassadoSemNadaNaoContaNada()
    {
        // Sem amostra e sem troca, o app não rodou naquele dia: o período
        // fica vazio em vez de fingir que o dia inteiro foi observado.
        var inicio = MomentoDia(1, 0, 0);

        var periodo = UsoRegras.Periodo(inicio, Momento(15, 0), new List<AmostraSensor>(), new List<TrocaUso>());

        Assert.Equal(inicio, periodo.Inicio);
        Assert.Equal(inicio, periodo.Fim);
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
        var periodo = UsoRegras.Periodo(MomentoDia(1, 0, 0), agora, amostras, trocas);
        var relatorio = UsoRegras.Calcular(trocas, periodo.Inicio, periodo.Fim, amostras);

        var uso = Assert.Single(relatorio.PorApp);
        Assert.Equal("chrome", uso.Processo);
        Assert.Equal(TimeSpan.FromHours(1), uso.Tempo);
    }

    [Fact]
    public void SessaoQueAtravessaAMeiaNoiteNaoPerdeTempo()
    {
        // O app rodou a noite toda com o chrome em frente e o dia só teve
        // uma troca, às 9h. Sem a troca que atravessa a meia-noite, a
        // noite inteira ficaria sem dono.
        var trocas = new List<TrocaUso>
        {
            TrocaEm(MomentoDia(1, 23, 0), "chrome"),   // a troca que atravessa a meia-noite
            TrocaEm(MomentoDia(0, 9, 0), "code"),
        };

        var amostras = new List<AmostraSensor>
        {
            AmostraEm(MomentoDia(0, 0, 5), 55f),
            AmostraEm(MomentoDia(0, 8, 55), 60f),
        };

        var agora = Momento(9, 30);
        var periodo = UsoRegras.Periodo(MomentoDia(0, 0, 0), agora, amostras, trocas);
        var relatorio = UsoRegras.Calcular(trocas, periodo.Inicio, periodo.Fim, amostras);

        var chrome = Assert.Single(relatorio.PorApp, u => u.Processo == "chrome");
        Assert.Equal(TimeSpan.FromHours(8) + TimeSpan.FromMinutes(55), chrome.Tempo);
    }

    [Fact]
    public void NoiteComOPcDesligadoNaoEntraNoTempoDeHoje()
    {
        // O PC ficou desligado a noite. O app só voltou às 10h: as horas
        // em que ele não existia não podem virar tempo de programa.
        var trocas = new List<TrocaUso>
        {
            TrocaEm(MomentoDia(1, 23, 0), "chrome"),
            TrocaEm(MomentoDia(0, 10, 0), "code"),
        };

        var amostras = new List<AmostraSensor>
        {
            AmostraEm(MomentoDia(0, 10, 3), 55f),
            AmostraEm(MomentoDia(0, 10, 10), 60f),
        };

        var agora = Momento(10, 20);
        var periodo = UsoRegras.Periodo(MomentoDia(0, 0, 0), agora, amostras, trocas);
        var relatorio = UsoRegras.Calcular(trocas, periodo.Inicio, periodo.Fim, amostras);

        Assert.DoesNotContain(relatorio.PorApp, u => u.Processo == "chrome");

        var code = Assert.Single(relatorio.PorApp);
        Assert.Equal("code", code.Processo);
        Assert.Equal(TimeSpan.FromMinutes(20), code.Tempo);
    }
    [Fact]
    public void LerVariosTrazATrocaQueAtravessaAMeiaNoite()
    {
        // A regra do núcleo: o primeiro dia (o mais antigo) traz também a
        // última troca do dia anterior. Sem ela, um aviso que começou de
        // madrugada não sabe dizer quem estava em frente.
        UsarPastaTemporaria();

        UsoHistorico.Gravar(new TrocaUso(MomentoDia(1, 23, 0), "chrome", "Painel"));
        UsoHistorico.Gravar(new TrocaUso(MomentoDia(0, 9, 0), "code", "Projeto"));

        var trocas = UsoHistorico.LerVarios(new[] { MomentoDia(1, 0, 0), MomentoDia(0, 0, 0) }).Linhas;

        Assert.Equal(2, trocas.Count);
        Assert.Equal("chrome", trocas[0].Processo);
        Assert.Equal("code", trocas[1].Processo);
        Assert.True(trocas[0].Momento < trocas[1].Momento);
    }

    [Fact]
    public void OMonitorApareceNoHistoricoComONomeQueOUsuarioConhece()
    {
        // O nome do executavel ("TranalSentinel") nao aparece em lugar
        // nenhum da tela.
        Assert.Equal("TRANAL SENTINEL", UsoJanela.NomeDoApp);
    }
}