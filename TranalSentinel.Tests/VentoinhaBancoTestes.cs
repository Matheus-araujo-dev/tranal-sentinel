using System.IO;
using LibreHardwareMonitor.Hardware;
using Xunit;

namespace TranalSentinel.Tests;

/// <summary>
/// Testes do histórico das ventoinhas no banco de dados.
///
/// <para>
/// O banco das ventoinhas guarda duas coisas: o <b>recorde</b> de cada
/// ventoinha (que não tem prazo de poda — é o que sobra quando o histórico
/// diário passa) e o <b>agregado de cada dia</b> (que é podado). Os testes
/// prendem as regras que fazem a diferença entre eles.
///
/// <para>
/// A classe fica na coleção "Banco": o banco é estado global, e o xUnit roda
/// as classes da mesma coleção em série.
/// </para>
/// </summary>
[Collection("Banco")]
public class VentoinhaBancoTestes : IDisposable
{
    private string? _pastaBanco;

    private static readonly DateTime Dia = new(2026, 11, 9, 0, 0, 0);

    public void Dispose()
    {
        BancoDeTeste.Restaurar();

        if (_pastaBanco == null) return;
        try
        {
            if (Directory.Exists(_pastaBanco)) Directory.Delete(_pastaBanco, true);
        }
        catch
        {
            // A pasta é temporária: se o Windows ainda estiver segurando o
            // arquivo, ela some sozinha depois.
        }
    }

    private void UsarPastasTemporarias()
    {
        (_pastaBanco, _) = BancoDeTeste.Isolar();
    }

    private static VentoinhaRegistro Registro(double rpm = 3392, double? temperatura = 85) =>
        new("ITE IT8655E|Fan #1", "ITE IT8655E", "Fan #1", rpm, temperatura,
            new DateTime(2026, 11, 9, 12, 0, 0), new DateTime(2026, 11, 9, 14, 0, 0), 91);

    // ------------------------------------------------------------------ o recorde

    [Fact]
    public void GravarVentoinha_ELerVentoinhas_VoltaComOQueFoiGravado()
    {
        UsarPastasTemporarias();

        Assert.True(Banco.GravarVentoinha(Registro()));

        var leitura = Banco.LerVentoinhas();

        Assert.True(leitura.Completa);
        Assert.Single(leitura.Linhas);

        var registro = leitura.Linhas[0];
        Assert.Equal("ITE IT8655E|Fan #1", registro.Id);
        Assert.Equal("ITE IT8655E", registro.Hardware);
        Assert.Equal("Fan #1", registro.Nome);
        Assert.Equal(3392, registro.RpmRecorde);
        Assert.Equal(85, registro.TemperaturaDoRecorde);
        Assert.Equal(new DateTime(2026, 11, 9, 12, 0, 0), registro.QuandoRecorde);
        Assert.Equal(new DateTime(2026, 11, 9, 14, 0, 0), registro.QuandoUltimo);
        Assert.Equal(91, registro.TemperaturaMaxima);
    }

    [Fact]
    public void GravarVentoinha_ReescreveORegistro_NaoSoma()
    {
        UsarPastasTemporarias();

        // O recorde é um espelho do que o app sabe: gravar duas vezes não
        // pode somar os RPM (o registro tem chave, e a linha é reescrita).
        Assert.True(Banco.GravarVentoinha(Registro(3392)));
        Assert.True(Banco.GravarVentoinha(Registro(3900)));

        var leitura = Banco.LerVentoinhas();

        Assert.Single(leitura.Linhas);
        Assert.Equal(3900, leitura.Linhas[0].RpmRecorde);
    }

    [Fact]
    public void GravarVentoinha_ComTemperaturaNula_GravaDoJeitoQuePode()
    {
        UsarPastasTemporarias();

        var registro = new VentoinhaRegistro("id", "hw", "Fan #2", 1200, null,
            new DateTime(2026, 11, 9, 12, 0, 0), new DateTime(2026, 11, 9, 14, 0, 0), null);

        Assert.True(Banco.GravarVentoinha(registro));

        var leitura = Banco.LerVentoinhas();

        Assert.Single(leitura.Linhas);
        Assert.Null(leitura.Linhas[0].TemperaturaDoRecorde);
        Assert.Null(leitura.Linhas[0].TemperaturaMaxima);
    }

    // ------------------------------------------------------------------ o dia

    [Fact]
    public void GravarVentoinhasDoDia_ELerVentoinhasDoDia_VoltaComOQueFoiGravado()
    {
        UsarPastasTemporarias();

        var dia = new VentoinhaDoDia("ITE IT8655E|Fan #1", 3392, 85, 91, 3000, 80, 143);

        Assert.True(Banco.GravarVentoinhasDoDia(Dia, new[] { dia }));

        var leitura = Banco.LerVentoinhasDoDia(Dia);

        Assert.True(leitura.Completa);
        Assert.Single(leitura.Linhas);
        Assert.Equal(3392, leitura.Linhas[0].RpmMaximo);
        Assert.Equal(85, leitura.Linhas[0].TemperaturaNoMaximo);
        Assert.Equal(91, leitura.Linhas[0].TemperaturaMaxima);
        Assert.Equal(143, leitura.Linhas[0].Amostras);
    }

    [Fact]
    public void GravarVentoinhasDoDia_ReescreveODia_NaoSomaAsAmostras()
    {
        UsarPastasTemporarias();

        // O agregado do dia é reescrito: o que chega é o total do dia, e
        // somar por cima contaria a mesma medição duas vezes.
        Banco.GravarVentoinhasDoDia(Dia, new[] { new VentoinhaDoDia("id", 3392, 85, 91, 3000, 80, 143) });
        Banco.GravarVentoinhasDoDia(Dia, new[] { new VentoinhaDoDia("id", 3392, 85, 91, 3000, 80, 200) });

        var leitura = Banco.LerVentoinhasDoDia(Dia);

        Assert.Single(leitura.Linhas);
        Assert.Equal(200, leitura.Linhas[0].Amostras);
    }

    [Fact]
    public void GravarVentoinhasDoDia_SemNada_NaoEFalha()
    {
        UsarPastasTemporarias();

        // Um dia sem ventoinha é um dia sem linhas — não é falha.
        Assert.True(Banco.GravarVentoinhasDoDia(Dia, Array.Empty<VentoinhaDoDia>()));
    }

    // ------------------------------------------------------------------ o histórico

    [Fact]
    public void LerHistoricoVentoinhas_TrazONomeDaVentoinha()
    {
        UsarPastasTemporarias();

        Banco.GravarVentoinha(Registro());
        Banco.GravarVentoinhasDoDia(Dia, new[]
        {
            new VentoinhaDoDia("ITE IT8655E|Fan #1", 3392, 85, 91, 3000, 80, 143),
        });

        var leitura = Banco.LerHistoricoVentoinhas(Dia.AddDays(-30));

        Assert.True(leitura.Completa);
        Assert.Single(leitura.Linhas);
        Assert.Equal("Fan #1", leitura.Linhas[0].Nome);
        Assert.Equal("ITE IT8655E", leitura.Linhas[0].Hardware);
        Assert.Equal(Dia, leitura.Linhas[0].Dia);
        Assert.Equal(3392, leitura.Linhas[0].RpmMaximo);
    }

    [Fact]
    public void LerHistoricoVentoinhas_AntesDoDia_NaoTrazNada()
    {
        UsarPastasTemporarias();

        Banco.GravarVentoinhasDoDia(Dia, new[]
        {
            new VentoinhaDoDia("id", 3392, 85, 91, 3000, 80, 143),
        });

        var leitura = Banco.LerHistoricoVentoinhas(Dia.AddDays(1));

        Assert.True(leitura.Completa);
        Assert.Empty(leitura.Linhas);
    }

    // ------------------------------------------------------------------ a poda

    [Fact]
    public void ApagarVentoinhasAntigas_CortaODiaEMantemORecorde()
    {
        UsarPastasTemporarias();

        Banco.GravarVentoinha(Registro());
        Banco.GravarVentoinhasDoDia(Dia, new[]
        {
            new VentoinhaDoDia("ITE IT8655E|Fan #1", 3392, 85, 91, 3000, 80, 143),
        });

        int apagados = Banco.ApagarVentoinhasAntigas(Dia.AddDays(1));

        Assert.Equal(1, apagados);
        Assert.Empty(Banco.LerVentoinhasDoDia(Dia).Linhas);

        // O recorde fica: é ele que sobrevive quando o histórico diário passa.
        Assert.Single(Banco.LerVentoinhas().Linhas);
    }

    // ------------------------------------------------------------------ o reparo

    [Fact]
    public void Reparar_ComBancoEstragado_NaoPerdeAsVentoinhasEmSilencio()
    {
        UsarPastasTemporarias();

        for (int i = 0; i < 300; i++)
            Banco.GravarAmostra(new AmostraSensor(Dia.AddMinutes(i),
                70f, 40f, 90f, 3200f, 60f, 50f, 2000f, 2048f, 80f, 10f, 1f));

        Banco.GravarUso(new TrocaUso(Dia.AddHours(10), "chrome", "Painel"));

        Assert.True(Banco.GravarVentoinha(Registro()));

        // O histórico diário espalhado por vários dias: a corrupção de uma
        // página só não leva tudo junto (o mesmo cuidado do teste do
        // tráfego).
        for (int dia = 0; dia < 10; dia++)
        {
            Assert.True(Banco.GravarVentoinhasDoDia(Dia.AddDays(-dia), new[]
            {
                new VentoinhaDoDia("ITE IT8655E|Fan #1", 3392 - dia, 85, 91, 3000, 80, 143),
            }));
        }

        Banco.Fechar();
        BancoCorrupcao.CorromperPaginaLegivel(Banco.Caminho, 300, 1);

        var resultado = Banco.Reparar();

        Assert.True(resultado.Sucesso, resultado.Relato);

        // O reparo conhece as tabelas das ventoinhas: sem isso, elas seriam
        // perdidas em silêncio (e o relato contaria só as outras).
        Assert.Contains("ventoinha", resultado.Relato);

        var registros = Banco.LerVentoinhas();
        Assert.True(registros.Completa);

        var dias = Banco.LerVentoinhasDoDia(Dia);
        Assert.True(dias.Completa);

        // O reparo não pode perder o histórico das ventoinhas em silêncio:
        // ou ele volta, ou o relato diz quantos se perderam.
        bool voltaram = registros.Linhas.Count > 0 || dias.Linhas.Count > 0;
        bool contou = resultado.Relato.Contains("ventoinha") && resultado.Relato.Contains("perderam");
        Assert.True(voltaram || contou, resultado.Relato);
    }

    [Fact]
    public void Reparar_ComBancoSemAsTabelasDeVentoinha_NaoECancelado()
    {
        UsarPastasTemporarias();

        for (int i = 0; i < 300; i++)
            Banco.GravarAmostra(new AmostraSensor(Dia.AddMinutes(i),
                70f, 40f, 90f, 3200f, 60f, 50f, 2000f, 2048f, 80f, 10f, 1f));

        Banco.GravarUso(new TrocaUso(Dia.AddHours(10), "chrome", "Painel"));

        // Um banco de antes da v0.30: as tabelas das ventoinhas não existem.
        // Isso NÃO é falha de leitura — e o reparo não pode ser cancelado
        // por causa disso.
        Banco.Instancia?.Executar("DROP TABLE ventoinha");
        Banco.Instancia?.Executar("DROP TABLE ventoinha_dia");
        Banco.Fechar();

        BancoCorrupcao.CorromperPaginaLegivel(Banco.Caminho, 300, 1);

        var resultado = Banco.Reparar();

        Assert.True(resultado.Sucesso, resultado.Relato);

        // O relato não pode inventar uma perda de ventoinhas: nunca houve
        // histórico de ventoinha guardado nesse banco.
        Assert.DoesNotContain("registros de ventoinha foram salvos", resultado.Relato);
    }

    // ------------------------------------------------------------------ o retrato

    [Fact]
    public void Ver_ContaAsVentoinhasQueTemRecorde()
    {
        UsarPastasTemporarias();

        Banco.GravarVentoinha(Registro());

        Assert.Equal(1, Banco.Ver().Ventoinhas);
    }

    [Fact]
    public void Ver_ComOCorrompido_ContaAsVentoinhasComoNaoDeuParaContar()
    {
        UsarPastasTemporarias();

        Banco.GravarVentoinha(Registro());

        Banco.Fechar();
        BancoCorrupcao.CorromperCabecalho(Banco.Caminho);

        var diagnostico = Banco.Ver();

        // -1 é "não deu para contar" — 0 seria um número inventado ao lado
        // de "não deu para contar as amostras".
        Assert.Equal(-1, diagnostico.Amostras);
        Assert.Equal(-1, diagnostico.Ventoinhas);
    }

    // ------------------------------------------------------------------ a base do dia

    [Fact]
    public void MonitorVentoinhas_ComOAppReaberto_NaoApagaAParteDoDiaJaGravada()
    {
        UsarPastasTemporarias();

        // O app rodou de manhã e gravou o agregado do dia.
        Banco.GravarVentoinhasDoDia(DateTime.Today, new[]
        {
            new VentoinhaDoDia("ITE IT8655E|Fan #1", 3392, 85, 91, 3000, 80, 143),
        });

        // O app foi fechado e aberto de novo.
        var monitor = new MonitorVentoinhas();
        Assert.True(monitor.Carregar());

        var linha = new SensorRow
        {
            Id = "ITE IT8655E|Fan #1",
            Hardware = "ITE IT8655E",
            Name = "Fan #1",
            Type = SensorType.Fan,
            HardwareType = HardwareType.SuperIO,
        };
        linha.Update(3000, null, null);

        monitor.Registrar(new[] { linha }, 85f, null, DateTime.Now);
        monitor.Gravar();

        // A base do dia tem de vir do banco: sem ela, a gravação seguinte
        // reescreveria o dia pela metade (e a manhã sumiria em silêncio).
        var dia = Banco.LerVentoinhasDoDia(DateTime.Today);

        Assert.True(dia.Completa);
        Assert.Single(dia.Linhas);
        Assert.Equal(3392, dia.Linhas[0].RpmMaximo);
        Assert.Equal(144, dia.Linhas[0].Amostras);
    }

    [Fact]
    public void MonitorVentoinhas_SemABaseDoDia_NaoGravaODiaParaNaoApagarOQueJaEstavaSalvo()
    {
        UsarPastasTemporarias();

        Banco.GravarVentoinhasDoDia(DateTime.Today, new[]
        {
            new VentoinhaDoDia("ITE IT8655E|Fan #1", 3392, 85, 91, 3000, 80, 143),
        });

        Banco.Fechar();
        BancoCorrupcao.CorromperCabecalho(Banco.Caminho);

        var monitor = new MonitorVentoinhas();
        monitor.Carregar();

        var linha = new SensorRow
        {
            Id = "ITE IT8655E|Fan #1",
            Hardware = "ITE IT8655E",
            Name = "Fan #1",
            Type = SensorType.Fan,
            HardwareType = HardwareType.SuperIO,
        };
        linha.Update(3000, null, null);

        monitor.Registrar(new[] { linha }, 85f, null, DateTime.Now);

        // Sem a base do banco, gravar o agregado de agora apagaria o que já
        // estava salvo do dia. O app espera a base aparecer — e diz que
        // está esperando.
        Assert.False(monitor.Gravar());
        Assert.Contains("não está gravando o dia", monitor.UltimoErro);
    }

    [Fact]
    public void MonitorVentoinhas_ComOHistoricoIndisponivel_NaoApagaODia()
    {
        UsarPastasTemporarias();

        Banco.GravarVentoinhasDoDia(DateTime.Today, new[]
        {
            new VentoinhaDoDia("ITE IT8655E|Fan #1", 3392, 85, 91, 3000, 80, 143),
        });

        Banco.Fechar();
        BancoCorrupcao.CorromperCabecalho(Banco.Caminho);

        var monitor = new MonitorVentoinhas();

        // O app avisa que não deu para ler e não apaga nada: o dia
        // guardado continua no arquivo (mesmo estragado) até o reparo.
        Assert.False(monitor.Carregar());
        Assert.NotEqual("", monitor.UltimoErro);
    }
}
