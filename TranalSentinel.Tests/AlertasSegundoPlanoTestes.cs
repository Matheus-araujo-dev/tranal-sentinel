using System.Globalization;
using LibreHardwareMonitor.Hardware;
using Xunit;

namespace TranalSentinel.Tests;

/// <summary>
/// Testes do vigia em segundo plano: temperatura do disco e alertas.
///
/// <para>
/// O que estes testes cobrem: o alerta de disco tem que vir do disco
/// (e não da GPU ou da placa-mãe), e o vigia não pode repetir o aviso nem
/// ficar preso com ele depois que a temperatura volta ao normal.
///
/// </para>
/// <para>
/// E, agora que a regra virou configurável: cada regra avisa uma vez por
/// episódio, respeita o tempo mínimo, ignora sensor ausente e operação
/// inválida, e a bateria só avisa fora da tomada.
/// </para>
/// </summary>
public class AlertasSegundoPlanoTestes
{
    private static SensorRow Linha(HardwareType hardware, SensorType tipo, string nome, float? valor)
    {
        var linha = new SensorRow
        {
            Hardware = hardware.ToString(),
            Type = tipo,
            HardwareType = hardware,
            Name = nome,
        };
        linha.Update(valor, null, null);
        return linha;
    }

    private static RegraAlerta Regra(string id, string sensor, string operacao, double limite, int segundosMinimos = 0) => new()
    {
        Id = id,
        Sensor = sensor,
        Operacao = operacao,
        Limite = limite,
        SegundosMinimos = segundosMinimos,
        Ativa = true,
        Severidade = Severidade.Medio,
    };

    [Fact]
    public void TemperaturaDoDiscoPEscolhidaDoDisco()
    {
        var linhas = new[]
        {
            Linha(HardwareType.Storage, SensorType.Temperature, "Temperatura", 55f),
            Linha(HardwareType.Storage, SensorType.Temperature, "Temperatura 2", 48f),
        };

        Assert.Equal(55f, MonitorAlertas.TemperaturaMaximaDisco(linhas));
    }

    [Fact]
    public void TemperaturaDaGpuNaoViraTemperaturaDeDisco()
    {
        // Sensor de GPU ou de CPU não é disco: se contasse, o alerta de
        // disco dispararia por causa da GPU.
        var linhas = new[]
        {
            Linha(HardwareType.GpuAmd, SensorType.Temperature, "Core", 90f),
            Linha(HardwareType.Cpu, SensorType.Temperature, "Core Max", 70f),
            Linha(HardwareType.Storage, SensorType.Temperature, "Temperatura", 40f),
        };

        Assert.Equal(40f, MonitorAlertas.TemperaturaMaximaDisco(linhas));
    }

    [Fact]
    public void SemSensorDeDiscoNaoHaTemperatura()
    {
        var linhas = new[]
        {
            Linha(HardwareType.GpuAmd, SensorType.Temperature, "Core", 90f),
        };

        Assert.Null(MonitorAlertas.TemperaturaMaximaDisco(linhas));
    }

    [Fact]
    public void SensorDeDiscoQueNaoETemperaturaNaoConta()
    {
        // Carga de leitura do disco não é temperatura.
        var linhas = new[]
        {
            Linha(HardwareType.Storage, SensorType.Load, "Leitura", 90f),
        };

        Assert.Null(MonitorAlertas.TemperaturaMaximaDisco(linhas));
    }

    [Fact]
    public void ListaVaziaNaoQuebra()
    {
        Assert.Null(MonitorAlertas.TemperaturaMaximaDisco(Array.Empty<SensorRow>()));
    }

    [Fact]
    public void AlertaDeDiscoSomeQuandoODiscoEsfria()
    {
        var monitor = new MonitorAlertas();
        var avisos = new List<Alerta>();
        monitor.Alertar += a => avisos.Add(a);

        var regra = Regra("disco", "disco-temp", ">=", 60);

        monitor.Verificar(DateTime.Now, new[] { regra }, new ContextoSensores { TemperaturaDisco = 65 });
        Assert.Single(avisos);
        Assert.Equal("regra:disco", avisos[0].Id);

        monitor.Verificar(DateTime.Now, new[] { regra }, new ContextoSensores { TemperaturaDisco = 40 });
        Assert.Empty(monitor.Ativos);
    }

    // ------------------------------------------------------- regras configuráveis

    [Fact]
    public void RegraAvisaQuandoACondicaoVale()
    {
        var monitor = new MonitorAlertas();
        var avisos = new List<Alerta>();
        monitor.Alertar += a => avisos.Add(a);

        var novos = monitor.Verificar(DateTime.Now, new[] { Regra("gpu", "gpu-temp", ">=", 85) },
            new ContextoSensores { TemperaturaGpu = 90 });

        Assert.Single(novos);
        Assert.Single(avisos);
        Assert.Equal("regra:gpu", avisos[0].Id);
        Assert.Equal(Severidade.Medio, avisos[0].Severidade);
    }

    [Fact]
    public void RegraNaoRepeteEnquantoACondicaoVale()
    {
        var monitor = new MonitorAlertas();
        var avisos = new List<Alerta>();
        monitor.Alertar += a => avisos.Add(a);

        var regra = Regra("cpu", "cpu-temp", ">=", 85);

        monitor.Verificar(DateTime.Now, new[] { regra }, new ContextoSensores { TemperaturaCpu = 90 });
        monitor.Verificar(DateTime.Now, new[] { regra }, new ContextoSensores { TemperaturaCpu = 95 });

        Assert.Single(avisos);
        Assert.Single(monitor.Ativos);
    }

    [Fact]
    public void RegraVoltaAAvisarDepoisQueACondicaoPassa()
    {
        var monitor = new MonitorAlertas();
        var avisos = new List<Alerta>();
        monitor.Alertar += a => avisos.Add(a);

        var regra = Regra("cpu", "cpu-temp", ">=", 85);

        monitor.Verificar(DateTime.Now, new[] { regra }, new ContextoSensores { TemperaturaCpu = 90 });
        monitor.Verificar(DateTime.Now, new[] { regra }, new ContextoSensores { TemperaturaCpu = 60 });
        Assert.Empty(monitor.Ativos);

        monitor.Verificar(DateTime.Now, new[] { regra }, new ContextoSensores { TemperaturaCpu = 90 });
        Assert.Equal(2, avisos.Count);
    }

    [Fact]
    public void TempoMinimoNaoAvisaAntesDeCompletar()
    {
        var monitor = new MonitorAlertas();
        var avisos = new List<Alerta>();
        monitor.Alertar += a => avisos.Add(a);

        var regra = Regra("cpu", "cpu-temp", ">=", 85, segundosMinimos: 30);
        var inicio = new DateTime(2026, 1, 1, 12, 0, 0);

        monitor.Verificar(inicio, new[] { regra }, new ContextoSensores { TemperaturaCpu = 90 });
        Assert.Empty(avisos);

        monitor.Verificar(inicio.AddSeconds(20), new[] { regra }, new ContextoSensores { TemperaturaCpu = 90 });
        Assert.Empty(avisos);
    }

    [Fact]
    public void TempoMinimoAvisaDepoisDeCompletar()
    {
        var monitor = new MonitorAlertas();
        var avisos = new List<Alerta>();
        monitor.Alertar += a => avisos.Add(a);

        var regra = Regra("cpu", "cpu-temp", ">=", 85, segundosMinimos: 30);
        var inicio = new DateTime(2026, 1, 1, 12, 0, 0);

        monitor.Verificar(inicio, new[] { regra }, new ContextoSensores { TemperaturaCpu = 90 });
        monitor.Verificar(inicio.AddSeconds(30), new[] { regra }, new ContextoSensores { TemperaturaCpu = 90 });

        Assert.Single(avisos);
    }

    [Fact]
    public void TempoMinimoRecomecaDepoisQueACondicaoPassa()
    {
        var monitor = new MonitorAlertas();
        var avisos = new List<Alerta>();
        monitor.Alertar += a => avisos.Add(a);

        var regra = Regra("cpu", "cpu-temp", ">=", 85, segundosMinimos: 30);
        var inicio = new DateTime(2026, 1, 1, 12, 0, 0);

        monitor.Verificar(inicio, new[] { regra }, new ContextoSensores { TemperaturaCpu = 90 });
        monitor.Verificar(inicio.AddSeconds(20), new[] { regra }, new ContextoSensores { TemperaturaCpu = 60 });  // passou
        monitor.Verificar(inicio.AddSeconds(40), new[] { regra }, new ContextoSensores { TemperaturaCpu = 90 });  // novo episódio

        Assert.Empty(avisos);   // o tempo mínimo voltou a zero
    }

    [Fact]
    public void RegraInativaNaoAvisa()
    {
        var monitor = new MonitorAlertas();
        var regra = Regra("cpu", "cpu-temp", ">=", 85);
        regra.Ativa = false;

        var novos = monitor.Verificar(DateTime.Now, new[] { regra }, new ContextoSensores { TemperaturaCpu = 95 });

        Assert.Empty(novos);
        Assert.Empty(monitor.Ativos);
    }

    [Fact]
    public void SensorAusenteNaoAvisa()
    {
        var monitor = new MonitorAlertas();

        var novos = monitor.Verificar(DateTime.Now, new[] { Regra("gpu", "gpu-temp", ">=", 85) },
            new ContextoSensores { TemperaturaGpu = null });

        Assert.Empty(novos);
    }

    [Fact]
    public void SensorDesconhecidoNaoAvisa()
    {
        var monitor = new MonitorAlertas();

        var novos = monitor.Verificar(DateTime.Now, new[] { Regra("x", "sensor-que-nao-existe", ">=", 1) },
            new ContextoSensores { TemperaturaCpu = 95 });

        Assert.Empty(novos);
    }

    [Fact]
    public void OperacaoInvalidaEhIgnorada()
    {
        var monitor = new MonitorAlertas();
        var avisos = new List<Alerta>();
        monitor.Alertar += a => avisos.Add(a);

        var novos = monitor.Verificar(DateTime.Now, new[] { Regra("cpu", "cpu-temp", ">", 85) },
            new ContextoSensores { TemperaturaCpu = 95 });

        Assert.Empty(novos);
        Assert.Empty(avisos);
    }

    [Fact]
    public void BateriaSoAvisaQuandoNaoEstaCarregando()
    {
        var monitor = new MonitorAlertas();
        var avisos = new List<Alerta>();
        monitor.Alertar += a => avisos.Add(a);

        var regra = Regra("bateria", "bateria", "<=", 20);

        monitor.Verificar(DateTime.Now, new[] { regra }, new ContextoSensores { Bateria = 10, Carregando = true });
        Assert.Empty(avisos);

        monitor.Verificar(DateTime.Now, new[] { regra }, new ContextoSensores { Bateria = 10, Carregando = false });
        Assert.Single(avisos);
        Assert.Equal("regra:bateria", avisos[0].Id);
    }

    [Fact]
    public void SemBateriaNaoHaAlerta()
    {
        var monitor = new MonitorAlertas();

        var novos = monitor.Verificar(DateTime.Now, new[] { Regra("bateria", "bateria", "<=", 20) },
            new ContextoSensores { Bateria = null, Carregando = false });

        Assert.Empty(novos);
    }

    // ------------------------------------------------------- as ventoinhas

    private static ContextoSensores ContextoDaVentoinha(float rpm) => new()
    {
        Ventoinhas = new[] { new ContextoVentoinha("ITE IT8655E|Fan #1", "Fan #1 (ITE IT8655E)", rpm) },
    };

    [Fact]
    public void RegraDeVentoinhaAvisaComONomeDaVentoinha()
    {
        var monitor = new MonitorAlertas();
        var avisos = new List<Alerta>();
        monitor.Alertar += a => avisos.Add(a);

        var regra = Regra("vent", MonitorAlertas.PrefixoVentoinha + "ITE IT8655E|Fan #1", "<=", 0);

        monitor.Verificar(DateTime.Now, new[] { regra }, ContextoDaVentoinha(0f));

        Assert.Single(avisos);

        // O nome da ventoinha, e não a chave interna ("ventoinha:hardware|
        // nome"): a chave existe para amarrar a regra, e não é texto para o
        // usuário ler.
        Assert.Contains("Fan #1 (ITE IT8655E)", avisos[0].Texto);
        Assert.Contains("RPM", avisos[0].Texto);
        Assert.DoesNotContain(MonitorAlertas.PrefixoVentoinha, avisos[0].Texto);
    }

    [Fact]
    public void ResolverSensor_ComChaveDeVentoinha_DevolveONomeEUnidade()
    {
        var (rotulo, unidade, dica) = MonitorAlertas.ResolverSensor(
            MonitorAlertas.PrefixoVentoinha + "ITE IT8655E|Fan #1",
            ContextoDaVentoinha(0f).Ventoinhas);

        Assert.Equal("Fan #1 (ITE IT8655E)", rotulo);
        Assert.Equal("RPM", unidade);
        Assert.NotEqual("", dica);
    }

    [Fact]
    public void ResolverSensor_ComVentoinhaForaDoContexto_UsaONomeDoSensor()
    {
        // A ventoinha não está dando leitura agora: o id não pode vazar
        // para a tela — o nome do sensor ainda serve.
        var (rotulo, unidade, _) = MonitorAlertas.ResolverSensor(
            MonitorAlertas.PrefixoVentoinha + "ITE IT8655E|Fan #1",
            Array.Empty<ContextoVentoinha>());

        Assert.Equal("Fan #1", rotulo);
        Assert.Equal("RPM", unidade);
    }

    [Fact]
    public void NumeroDoAlertaDeVentoinhaNaoDependeDaCulturaDoWindows()
    {
        var cultura = CultureInfo.CurrentCulture;

        try
        {
            var monitor = new MonitorAlertas();
            var avisos = new List<Alerta>();
            monitor.Alertar += a => avisos.Add(a);

            var regra = Regra("vent", MonitorAlertas.PrefixoVentoinha + "ITE IT8655E|Fan #1", "<=", 0.5);

            CultureInfo.CurrentCulture = new CultureInfo("en-US");
            monitor.Verificar(DateTime.Now, new[] { regra }, ContextoDaVentoinha(0.4f));

            // O número sai pelo formatador da casa: o mesmo evento não pode
            // sair diferente em PCs de culturas diferentes.
            Assert.Single(avisos);
            Assert.Contains("0,4", avisos[0].Texto);
            Assert.Contains("0,5", avisos[0].Texto);
            Assert.DoesNotContain("0.4", avisos[0].Texto);
        }
        finally
        {
            CultureInfo.CurrentCulture = cultura;
        }
    }

    [Fact]
    public void ComparacaoMenorOuIgualFunciona()
    {
        var monitor = new MonitorAlertas();

        var novos = monitor.Verificar(DateTime.Now, new[] { Regra("clock", "cpu-clock", "<=", 2000) },
            new ContextoSensores { ClockCpu = 1800 });

        Assert.Single(novos);
    }

    [Fact]
    public void AsRegrasPadraoSaoOsLimitesDeHoje()
    {
        // A regressão que este teste trava: os três avisos que o app já dava
        // (CPU 85 °C, disco 60 °C, bateria 20 %) têm que continuar igual.
        var regras = RegrasAlertaPadrao.Criar();

        Assert.Equal(3, regras.Count);

        var cpu = regras.Single(r => r.Id == "padrao-cpu");
        Assert.Equal("cpu-temp", cpu.Sensor);
        Assert.Equal(">=", cpu.Operacao);
        Assert.Equal(85, cpu.Limite);
        Assert.Equal(Severidade.Alto, cpu.Severidade);

        var disco = regras.Single(r => r.Id == "padrao-disco");
        Assert.Equal("disco-temp", disco.Sensor);
        Assert.Equal(">=", disco.Operacao);
        Assert.Equal(60, disco.Limite);
        Assert.Equal(Severidade.Medio, disco.Severidade);

        var bateria = regras.Single(r => r.Id == "padrao-bateria");
        Assert.Equal("bateria", bateria.Sensor);
        Assert.Equal("<=", bateria.Operacao);
        Assert.Equal(20, bateria.Limite);
        Assert.Equal(Severidade.Medio, bateria.Severidade);

        Assert.All(regras, r => Assert.True(r.Ativa));
    }

    [Fact]
    public void AsRegrasPadraoAvisamComoAntes()
    {
        // O caminho de sempre: a CPU a 90 °C avisa com o título de sempre.
        var monitor = new MonitorAlertas();
        var avisos = new List<Alerta>();
        monitor.Alertar += a => avisos.Add(a);

        var contexto = new ContextoSensores { TemperaturaCpu = 90, TemperaturaDisco = 65, Bateria = 10, Carregando = false };
        var novos = monitor.Verificar(DateTime.Now, RegrasAlertaPadrao.Criar(), contexto);

        Assert.Equal(3, novos.Count);
        Assert.Contains(novos, a => a.Titulo == "CPU muito quente");
        Assert.Contains(novos, a => a.Titulo == "Disco quente");
        Assert.Contains(novos, a => a.Titulo == "Bateria acabando");
    }

    // -------------------------------------------------------------------
    // Mudar a regra: o episódio anterior vale e o aviso antigo sai da tela.
    // -------------------------------------------------------------------

    [Fact]
    public void MudarOLimiteEncerraOVersoAnterior()
    {
        var monitor = new MonitorAlertas();
        var regra = Regra("r1", "cpu-temp", ">=", 85);
        var contexto = new ContextoSensores { TemperaturaCpu = 90 };

        var primeiro = monitor.Verificar(DateTime.Now, new[] { regra }, contexto);
        Assert.Single(primeiro);
        Assert.Single(monitor.Ativos);

        // O usuário baixa o limite para 70: a regra passa a vigiar outra
        // coisa. O aviso antigo tem que sair (o texto dele mentia sobre o
        // limite) e o novo tem que entrar.
        regra.Limite = 70;
        var segundo = monitor.Verificar(DateTime.Now, new[] { regra }, contexto);

        Assert.Single(segundo);
        Assert.Contains("70", segundo[0].Texto);
        Assert.Single(monitor.Ativos);
        Assert.Contains("70", monitor.Ativos[0].Texto);
    }

    [Fact]
    public void MudarOSensorEncerraOVersoAnterior()
    {
        var monitor = new MonitorAlertas();
        var regra = Regra("r1", "cpu-temp", ">=", 85);
        var contexto = new ContextoSensores { TemperaturaCpu = 90, TemperaturaGpu = 90 };

        monitor.Verificar(DateTime.Now, new[] { regra }, contexto);
        Assert.Single(monitor.Ativos);

        // O usuário passa a vigiar a GPU: o aviso antigo (da CPU) sai.
        regra.Sensor = "gpu-temp";
        var segundo = monitor.Verificar(DateTime.Now, new[] { regra }, contexto);

        Assert.Single(segundo);
        Assert.Contains("GPU", segundo[0].Titulo + segundo[0].Texto);
        Assert.Single(monitor.Ativos);
    }

    [Fact]
    public void OAvisoAtivoAcompanhaOValorDeAgora()
    {
        var monitor = new MonitorAlertas();
        var regra = Regra("r1", "cpu-temp", ">=", 85);

        monitor.Verificar(DateTime.Now, new[] { regra }, new ContextoSensores { TemperaturaCpu = 90 });
        Assert.Contains("90", monitor.Ativos[0].Texto);

        // O valor subiu: o aviso continua o mesmo (não avisa de novo), mas o
        // texto mostra o valor de agora.
        var contexto = new ContextoSensores { TemperaturaCpu = 95 };
        var novos = monitor.Verificar(DateTime.Now, new[] { regra }, contexto);

        Assert.Empty(novos);
        Assert.Single(monitor.Ativos);
        Assert.Contains("95", monitor.Ativos[0].Texto);
    }

    [Fact]
    public void RegraRemovidaNaoDeixaContagemPendenteParaTras()
    {
        var monitor = new MonitorAlertas();
        var regra = Regra("r1", "cpu-temp", ">=", 85, segundosMinimos: 60);
        var contexto = new ContextoSensores { TemperaturaCpu = 90 };
        var inicio = DateTime.Now;

        // 30 s de condição: ainda não pode avisar (o tempo mínimo é 60 s).
        monitor.Verificar(inicio, new[] { regra }, contexto);
        Assert.Empty(monitor.Ativos);

        // O usuário apaga a regra e depois a recria com o mesmo Id.
        monitor.Verificar(inicio.AddSeconds(30), Array.Empty<RegraAlerta>(), contexto);
        monitor.Verificar(inicio.AddSeconds(70), new[] { regra }, contexto);

        // Se a contagem antiga tivesse sobrado, já teria avisado.
        Assert.Empty(monitor.Ativos);
    }

    [Fact]
    public void OTextoDoAvisoNaoDependeDaCulturaDoWindows()
    {
        // O CI roda em en-US e o PC de desenvolvimento em pt-BR: o mesmo
        // evento não pode sair escrito de dois jeitos. O número do aviso
        // sai pelo formatador da casa (vírgula decimal, sempre).
        var culturaOriginal = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("pt-BR");
            var monitor = new MonitorAlertas();
            var novos = monitor.Verificar(DateTime.Now,
                new[] { Regra("r1", "cpu-temp", ">=", 85) },
                new ContextoSensores { TemperaturaCpu = 86.5f });

            Assert.Single(novos);
            Assert.Contains("86,5", novos[0].Texto);

            CultureInfo.CurrentCulture = new CultureInfo("en-US");
            var outro = new MonitorAlertas();
            var novos2 = outro.Verificar(DateTime.Now,
                new[] { Regra("r1", "cpu-temp", ">=", 85) },
                new ContextoSensores { TemperaturaCpu = 86.5f });

            Assert.Single(novos2);
            Assert.Contains("86,5", novos2[0].Texto);
            Assert.DoesNotContain("86.5", novos2[0].Texto);
        }
        finally
        {
            CultureInfo.CurrentCulture = culturaOriginal;
        }
    }

    [Fact]
    public void ORotuloDaBateriaEOMesmoNoAvisoENaTela()
    {
        var monitor = new MonitorAlertas();
        var novos = monitor.Verificar(DateTime.Now,
            new[] { Regra("rb", MonitorAlertas.SensorBateria, "<=", 20) },
            new ContextoSensores { Bateria = 10, Carregando = false });

        Assert.Single(novos);
        Assert.Contains(MonitorAlertas.BateriaRotulo, novos[0].Texto);
    }

    // ------------------------------------------------------- o instante do episódio

    [Fact]
    public void AlertaGuardaDesdeQuandoACondicaoComecou()
    {
        // O "por que disparou" olha o histórico antes do disparo: o instante
        // que o app guarda é o começo da condição, não o do aviso.
        var monitor = new MonitorAlertas();
        var avisos = new List<Alerta>();
        monitor.Alertar += a => avisos.Add(a);

        var regra = Regra("cpu", "cpu-temp", ">=", 85);
        var inicio = new DateTime(2026, 1, 1, 12, 0, 0);

        monitor.Verificar(inicio, new[] { regra }, new ContextoSensores { TemperaturaCpu = 90 });

        Assert.Single(avisos);
        Assert.Equal(inicio, avisos[0].Desde);
    }

    [Fact]
    public void AlertaQueContinuaMantemOMesmoDesde()
    {
        var monitor = new MonitorAlertas();
        var regra = Regra("cpu", "cpu-temp", ">=", 85);
        var inicio = new DateTime(2026, 1, 1, 12, 0, 0);

        monitor.Verificar(inicio, new[] { regra }, new ContextoSensores { TemperaturaCpu = 90 });
        monitor.Verificar(inicio.AddSeconds(30), new[] { regra }, new ContextoSensores { TemperaturaCpu = 95 });

        var ativo = Assert.Single(monitor.Ativos);
        Assert.Equal(inicio, ativo.Desde);
    }

    [Fact]
    public void EpisodioNovoTemDesdeNovo()
    {
        var monitor = new MonitorAlertas();
        var regra = Regra("cpu", "cpu-temp", ">=", 85);
        var inicio = new DateTime(2026, 1, 1, 12, 0, 0);

        monitor.Verificar(inicio, new[] { regra }, new ContextoSensores { TemperaturaCpu = 90 });
        monitor.Verificar(inicio.AddSeconds(20), new[] { regra }, new ContextoSensores { TemperaturaCpu = 60 });   // passou
        monitor.Verificar(inicio.AddSeconds(40), new[] { regra }, new ContextoSensores { TemperaturaCpu = 90 });   // novo episódio

        var ativo = Assert.Single(monitor.Ativos);
        Assert.Equal(inicio.AddSeconds(40), ativo.Desde);
    }
}
