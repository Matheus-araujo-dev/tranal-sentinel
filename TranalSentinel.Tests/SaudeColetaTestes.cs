using System;
using Xunit;

namespace TranalSentinel.Tests;

/// <summary>
/// Testes da leitura do instante em que a máquina parou (evento 6008) e do
/// seu casamento com o travamento (Kernel-Power 41).
///
/// <para>
/// É o que ancora a retrospectiva: o 41 é registrado no boot seguinte, e o
/// dono costuma só reiniciar o PC horas depois. O 6008 traz a hora real do
/// desligamento — sem ele, o app diria "não estava rodando" num congelamento
/// em que ele estava.
/// </para>
/// </summary>
public class SaudeColetaTestes
{
    private static EventoWindows Evento6008(string hora, string data) => new()
    {
        Data = new DateTime(2026, 10, 29, 12, 36, 27, DateTimeKind.Local),
        Id = 6008,
        Provider = "EventLog",
        Dados = new[] { hora, data, "", "", "30350" }
    };

    [Fact]
    public void Desligamento_LeAHoraEDaData()
    {
        DateTime? instante = SaudeColeta.LerInstanteDesligamento(Evento6008("12:35:22", "29/10/2026"));

        Assert.NotNull(instante);
        Assert.Equal(new DateTime(2026, 10, 29, 12, 35, 22), instante!.Value);
    }

    [Fact]
    public void Desligamento_TiraAsMarcasDeDirecaoDoWindows()
    {
        // O Windows escreve a data com marcas de direção invisíveis no meio.
        var e = Evento6008("12:35:22", "‎29/‎10/‎2026");

        DateTime? instante = SaudeColeta.LerInstanteDesligamento(e);

        Assert.NotNull(instante);
        Assert.Equal(new DateTime(2026, 10, 29, 12, 35, 22), instante!.Value);
    }

    [Fact]
    public void Desligamento_DataAmbiguaSegueOPadraoDoWindows()
    {
        // 05/06/2026: dia e mês são dois números pequenos. Quem decide é o
        // padrão curto da configuração regional (aqui, dd/MM/yyyy).
        DateTime? instante = SaudeColeta.LerInstanteDesligamento(Evento6008("10:00:00", "05/06/2026"));

        Assert.NotNull(instante);
        Assert.Equal(5, instante!.Value.Day);
        Assert.Equal(6, instante!.Value.Month);
    }

    [Fact]
    public void Desligamento_QuandoODiaEDoMesSaoGrandesNaoHaDuvida()
    {
        // 25/10/2026: 25 só pode ser dia.
        DateTime? instante = SaudeColeta.LerInstanteDesligamento(Evento6008("10:00:00", "25/10/2026"));

        Assert.NotNull(instante);
        Assert.Equal(25, instante!.Value.Day);
        Assert.Equal(10, instante!.Value.Month);
    }

    [Fact]
    public void Desligamento_TextoRuimDevolveNulo()
    {
        Assert.Null(SaudeColeta.LerInstanteDesligamento(Evento6008("", "")));
        Assert.Null(SaudeColeta.LerInstanteDesligamento(Evento6008("12:35:22", "sem data")));
        Assert.Null(SaudeColeta.LerInstanteDesligamento(Evento6008("12:35:22", "31/02/2026")));
        Assert.Null(SaudeColeta.LerInstanteDesligamento(Evento6008("nao e hora", "29/10/2026")));
    }

    [Fact]
    public void DesligamentoDoBoot_AchaOEventoDoMesmoBoot()
    {
        var desligamentos = new List<(DateTime Boot, DateTime? Instante)>
        {
            (new DateTime(2026, 10, 15, 10, 46, 23), new DateTime(2026, 10, 15, 10, 43, 38)),
            (new DateTime(2026, 10, 29, 12, 36, 27), new DateTime(2026, 10, 29, 12, 35, 22)),
        };

        DateTime? instante = SaudeColeta.DesligamentoDoBoot(new DateTime(2026, 10, 29, 12, 36, 10), desligamentos);

        Assert.NotNull(instante);
        Assert.Equal(new DateTime(2026, 10, 29, 12, 35, 22), instante!.Value);
    }

    [Fact]
    public void DesligamentoDoBoot_SemEventoCorrespondenteDevolveNulo()
    {
        var desligamentos = new List<(DateTime Boot, DateTime? Instante)>
        {
            (new DateTime(2026, 10, 15, 10, 46, 23), new DateTime(2026, 10, 15, 10, 43, 38)),
        };

        // O boot é de outro dia: não há desligamento correspondente.
        Assert.Null(SaudeColeta.DesligamentoDoBoot(new DateTime(2026, 10, 29, 12, 36, 10), desligamentos));
    }
}
