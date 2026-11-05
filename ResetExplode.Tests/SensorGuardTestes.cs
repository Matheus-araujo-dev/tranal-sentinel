using Xunit;

namespace ResetExplode.Tests;

/// <summary>
/// Testes da guarda dos sensores.
///
/// O que é testado aqui é a decisão pura (nível de segurança a partir dos
/// crashes do app). Nada toca o Windows: os crashes são montados na mão.
/// </summary>
public class SensorGuardTestes
{
    private static readonly DateTime Agora = new(2026, 11, 5, 12, 0, 0);

    private static CrashDoApp Crash(DateTime quando, string codigo = "c0000005", string app = "ResetExplode.exe")
        => new(quando, codigo, app, app);

    // ---------------- NormalizarCodigo ----------------

    [Theory]
    [InlineData("c0000005", "0xc0000005")]      // como o Windows grava (hex sem 0x)
    [InlineData("C0000005", "0xc0000005")]
    [InlineData("0xc0000005", "0xc0000005")]
    [InlineData("80131623", "0x80131623")]
    [InlineData("0x80131623", "0x80131623")]
    [InlineData("3221225477", "0xc0000005")]     // o mesmo código em decimal
    [InlineData("2148734499", "0x80131623")]
    [InlineData("00000000", "0x00000000")]
    [InlineData("", "")]
    [InlineData(null, "")]
    [InlineData("   ", "")]
    public void NormalizarCodigo_FormataComoEsperado(string? entrada, string esperada)
    {
        Assert.Equal(esperada, SensorGuard.NormalizarCodigo(entrada));
    }

    [Theory]
    [InlineData("c0000005", true)]      // access violation
    [InlineData("80131623", true)]      // falha do motor de execução
    [InlineData("0xc0000005", true)]
    [InlineData("3221225477", true)]
    [InlineData("80000003", false)]     // breakpoint
    [InlineData("e0434352", false)]     // exceção do .NET (dá para pegar em C#)
    [InlineData("", false)]
    [InlineData(null, false)]
    public void EhFalhaNativa_ReconheceSoAsFalhasQueMatamOProcesso(string? codigo, bool esperado)
    {
        Assert.Equal(esperado, SensorGuard.EhFalhaNativa(codigo));
    }

    // ---------------- ContarCrashes ----------------

    [Fact]
    public void ContarCrashes_ContaOsDentroDaJanela()
    {
        var crashes = new[]
        {
            Crash(Agora.AddHours(-2)),
            Crash(Agora.AddHours(-5)),
            Crash(Agora.AddHours(-40)),   // fora da janela
        };

        Assert.Equal(2, SensorGuard.ContarCrashes(crashes, Agora));
    }

    [Fact]
    public void ContarCrashes_IgnoraExcecaoQueDaParaPegar()
    {
        var crashes = new[] { Crash(Agora.AddHours(-1), "e0434352") };

        Assert.Equal(0, SensorGuard.ContarCrashes(crashes, Agora));
    }

    [Fact]
    public void ContarCrashes_NaoContaOQueEstaNoFuturo()
    {
        var crashes = new[] { Crash(Agora.AddMinutes(10)) };

        Assert.Equal(0, SensorGuard.ContarCrashes(crashes, Agora));
    }

    [Fact]
    public void ContarCrashes_ReabilitarZeraAJanela()
    {
        var crashes = new[]
        {
            Crash(Agora.AddHours(-2)),
            Crash(Agora.AddMinutes(-10)),
        };

        // Reabilitou há 15 minutos: só o crash posterior conta.
        Assert.Equal(1, SensorGuard.ContarCrashes(crashes, Agora, Agora.AddMinutes(-15)));
    }

    [Fact]
    public void ContarCrashes_JanelaPadraoDe24Horas()
    {
        Assert.Equal(1, SensorGuard.ContarCrashes(new[] { Crash(Agora.AddHours(-23)) }, Agora));
        Assert.Equal(0, SensorGuard.ContarCrashes(new[] { Crash(Agora.AddHours(-25)) }, Agora));
    }

    // ---------------- Nivel ----------------

    [Fact]
    public void Nivel_SemCrashFicaCompleto()
    {
        Assert.Equal(NivelSensor.Completo, SensorGuard.Nivel(Array.Empty<CrashDoApp>(), Agora));
    }

    [Fact]
    public void Nivel_UmCrashDesligaSoAGpu()
    {
        var crashes = new[] { Crash(Agora.AddHours(-1)) };

        Assert.Equal(NivelSensor.SemSensoresGpu, SensorGuard.Nivel(crashes, Agora));
    }

    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(7)]
    public void Nivel_DoisOuMaisCrashesDesligamTudo(int quantos)
    {
        var crashes = Enumerable.Range(0, quantos)
            .Select(i => Crash(Agora.AddHours(-1 - i)))
            .ToArray();

        Assert.Equal(NivelSensor.SemSensores, SensorGuard.Nivel(crashes, Agora));
    }

    [Fact]
    public void Nivel_DoisCrashesDesligamSoAGpu()
    {
        // A CPU nunca falhou: manter os sensores dela é melhor do que perder tudo.
        var crashes = Enumerable.Range(0, 2).Select(i => Crash(Agora.AddHours(-1 - i))).ToArray();

        Assert.Equal(NivelSensor.SemSensoresGpu, SensorGuard.Nivel(crashes, Agora));
    }

    [Fact]
    public void Nivel_PreferenciaDoUsuarioDesligaAGpu()
    {
        Assert.Equal(NivelSensor.SemSensoresGpu,
            SensorGuard.Nivel(Array.Empty<CrashDoApp>(), Agora, gpuDesligadaNasPreferencias: true));
    }

    [Fact]
    public void Nivel_PreferenciaNaoMudaOQueEstaoDesligadosPorCrash()
    {
        var crashes = Enumerable.Range(0, 3).Select(i => Crash(Agora.AddHours(-1 - i))).ToArray();

        // O pior caso vence: com 3 crashes, desliga tudo mesmo com a preferência da GPU.
        Assert.Equal(NivelSensor.SemSensores,
            SensorGuard.Nivel(crashes, Agora, gpuDesligadaNasPreferencias: true));
    }

    [Fact]
    public void Nivel_CrashForaDaJanelaNaoDesligaNada()
    {
        var crashes = new[] { Crash(Agora.AddDays(-3)) };

        Assert.Equal(NivelSensor.Completo, SensorGuard.Nivel(crashes, Agora));
    }

    // ---------------- UsarLhm / UsarGpuAdl ----------------

    [Theory]
    [InlineData(NivelSensor.Completo, true, true)]
    [InlineData(NivelSensor.SemSensoresGpu, true, false)]
    [InlineData(NivelSensor.SemSensores, false, false)]
    public void Nivel_DefineOQuePodeSerUsado(NivelSensor nivel, bool usarLhm, bool usarGpu)
    {
        Assert.Equal(usarLhm, SensorGuard.UsarLhm(nivel));
        Assert.Equal(usarGpu, SensorGuard.UsarGpuAdl(nivel));
    }

    // ---------------- Mensagem ----------------

    [Fact]
    public void Mensagem_CompletoNaoTemNada()
    {
        Assert.Equal("", SensorGuard.Mensagem(NivelSensor.Completo, 0));
    }

    [Theory]
    [InlineData(NivelSensor.SemSensoresGpu, 1)]
    [InlineData(NivelSensor.SemSensores, 2)]
    public void Mensagem_ExplicaOQueFoiFeito(NivelSensor nivel, int crashes)
    {
        string msg = SensorGuard.Mensagem(nivel, crashes);

        Assert.False(string.IsNullOrWhiteSpace(msg));
        Assert.Contains("travou", msg, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(crashes.ToString(), msg);
    }
}

/// <summary>Testes da leitura dos crashes no log de eventos.</summary>
public class SensorCrashesTestes
{
    private static EventoWindows Evento(params string[] dados)
        => new() { Data = new DateTime(2026, 11, 4, 22, 55, 56), Id = 1000, Provider = "Application Error", Dados = dados };

    [Fact]
    public void Converter_LeONomeDoAppEOcodigoDeExcecao()
    {
        // Ordem real do EventData do evento 1000 do Windows.
        var ev = Evento("ResetExplode.exe", "0.16.0.0", "6ada6f8e",
                        "ResetExplode.exe", "0.16.0.0", "6ada6f8e",
                        "c0000005", "0002203f9", "6052", "0", "C:\\app.exe", "C:\\app.exe", "id");

        var crash = SensorCrashes.Converter(ev, "ResetExplode");

        Assert.NotNull(crash);
        Assert.Equal("ResetExplode.exe", crash!.Aplicativo);
        Assert.Equal("ResetExplode.exe", crash.Modulo);
        Assert.Equal("0xc0000005", crash.CodigoExcecao);
        Assert.Equal(new DateTime(2026, 11, 4, 22, 55, 56), crash.Momento);
    }

    [Fact]
    public void Converter_IgnoraEventoDeOutroAplicativo()
    {
        var ev = Evento("RevoUninPro.exe", "5.5.4.0", "6ab36695",
                        "RevoUninPro.exe", "5.5.4.0", "6ab36695",
                        "c0000005", "0007e8c9", "18716", "0", "C:\\revo.exe", "C:\\revo.exe", "id");

        Assert.Null(SensorCrashes.Converter(ev, "ResetExplode"));
    }

    [Fact]
    public void Converter_IgnoraEventoSemNomeDeApp()
    {
        Assert.Null(SensorCrashes.Converter(Evento(), "ResetExplode"));
    }

    [Fact]
    public void Converter_AceitaOAppPeloNomeDoExecutavel()
    {
        var ev = Evento("ResetExplode.exe", "0.16.0.0", "6ada6f8e",
                        "ResetExplode.exe", "0.16.0.0", "6ada6f8e",
                        "80131623", "000123edd", "9748", "0", "C:\\app.exe", "C:\\app.exe", "id");

        var crash = SensorCrashes.Converter(ev, "ResetExplode");

        Assert.NotNull(crash);
        Assert.Equal("0x80131623", crash!.CodigoExcecao);
    }
}
