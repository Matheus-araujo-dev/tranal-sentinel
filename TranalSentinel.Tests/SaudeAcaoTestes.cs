using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using TranalSentinel;
using Xunit;

namespace TranalSentinel.Tests;

/// <summary>
/// Testes das ações do TRANAL SAÚDE: quais são oferecidas, como são
/// executadas e como voltam atrás.
///
/// <para>
/// O que estes testes cobrem: serviço de segurança nunca é tocado, o modo
/// original fica guardado para desfazer, backup forjado não desliga nada,
/// ação sem administrador não mexe no sistema, e o aviso de programa travado
/// não repete nem some do nada.
/// </para>
///
/// <para>
/// Estes testes gravam as configurações (o executor salva o backup para
/// desfazer), então a pasta é redirecionada para uma pasta temporária: o
/// teste não pode mexer no <c>settings.json</c> de verdade do usuário. Como a
/// pasta é um estado global, a classe fica na coleção do xUnit que roda em
/// série.
/// </para>
/// </summary>
[Collection("Configuracoes")]
public class SaudeAcaoTestes : IDisposable
{
    private readonly string _pasta;

    public SaudeAcaoTestes()
    {
        _pasta = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "tranal-testes-acoes-" + Guid.NewGuid().ToString("N"));
        AppSettings.PastaOverride = _pasta;
    }

    public void Dispose()
    {
        AppSettings.PastaOverride = null;
        try { if (System.IO.Directory.Exists(_pasta)) System.IO.Directory.Delete(_pasta, true); }
        catch { /* a pasta temporária vai embora sozinha */ }
    }

    // --------------------------------------------------------- suspending de serviços

    private static ServicoInfo Servico(string nome, string caminho, string modo = "Auto") =>
        new() { Nome = nome, Caminho = caminho, ModoInicializacao = modo };

    /// <summary>
    /// Monta um serviço para ser entregue direto ao executor — é assim que se
    /// testa a rede de segurança (o que a lista monta não passa por ela).
    /// </summary>
    private static ServicoSuspendivel Susp(string nome, string caminho) =>
        new() { Nome = nome, Caminho = caminho, ModoInicializacao = "Auto" };

    [Fact]
    public void Listar_NaoIncluiServicoDoWindows()
    {
        var lista = ServicosSuspendiveis.Listar(new[]
        {
            Servico("Wsearch", @"C:\Windows\System32\SearchIndexer.exe"),
        });

        Assert.Empty(lista);
    }

    [Fact]
    public void Listar_NaoIncluiServicoDeSeguranca()
    {
        var lista = ServicosSuspendiveis.Listar(new[]
        {
            Servico("McAfeeMPE", @"C:\Program Files\McAfee\SomeService.exe"),
            Servico("WDNisSvc", @"C:\Program Files\Windows Defender\NisSrv.exe"),
        });

        Assert.Empty(lista);
    }

    [Fact]
    public void Listar_NaoIncluiServicoDesativado()
    {
        var lista = ServicosSuspendiveis.Listar(new[]
        {
            Servico("AdobeARCSService", @"C:\Program Files\Adobe\ARC.exe", "Disabled"),
        });

        Assert.Empty(lista);
    }

    [Fact]
    public void Listar_NaoIncluiServicoDeSuporteNemDeIntegracaoDoApp()
    {
        var lista = ServicosSuspendiveis.Listar(new[]
        {
            Servico("Audiosrv", @"C:\Windows\System32\audiodg.exe"),
            Servico("RealtekAudioService", @"C:\Program Files\Realtek\Audio\RtkAudioservice.exe"),
            Servico("OpenRGB", @"E:\Program Files\OpenRGB\OpenRGB.exe"),
            Servico("LghubUpdaterService", @"C:\Program Files\LGHUB\lghub_agent.exe"),
        });

        Assert.Empty(lista);
    }

    [Fact]
    public void Listar_MarcaOsConhecidosEDeixaOsDesconhecidosParaOUsuarioEscolher()
    {
        var lista = ServicosSuspendiveis.Listar(new[]
        {
            Servico("AdobeARCSService", @"C:\Program Files\Adobe\Reader\Arc.exe"),
            Servico("MeuPrograma", @"C:\Program Files\Meu\App.exe"),
        });

        var adobe = lista.Single(s => s.Nome == "AdobeARCSService");
        var meu = lista.Single(s => s.Nome == "MeuPrograma");

        Assert.True(adobe.Conhecido);
        Assert.True(adobe.Selecionado);
        Assert.Equal("Adobe Acrobat/Reader", adobe.Programa);

        Assert.False(meu.Conhecido);
        Assert.False(meu.Selecionado);   // desconhecido não vem marcado
    }

    [Fact]
    public void Listar_ConhecidosVemPrimeiros()
    {
        var lista = ServicosSuspendiveis.Listar(new[]
        {
            Servico("AAAProgram", @"C:\Program Files\AAA\App.exe"),
            Servico("gupdate", @"C:\Program Files\Google\Update.exe"),
        });

        Assert.Equal("gupdate", lista[0].Nome);
    }

    [Fact]
    public void Listar_ReconheceServicoComVersaoNoNome()
    {
        // O Google Update real se chama "GoogleUpdaterService157.0.8089.0":
        // sem o prefixo, o app não reconheceria nem o atualizador mais comum.
        var lista = ServicosSuspendiveis.Listar(new[]
        {
            Servico("GoogleUpdaterService157.0.8089.0", @"C:\Program Files (x86)\Google\GoogleUpdater\updater.exe"),
            Servico("GoogleUpdaterInternalService157.0.8089.0", @"C:\Program Files (x86)\Google\GoogleUpdater\updater.exe"),
        });

        Assert.Equal(2, lista.Count);
        Assert.All(lista, s => Assert.True(s.Conhecido));
        Assert.All(lista, s => Assert.True(s.Selecionado));
        Assert.All(lista, s => Assert.Equal("Google Update", s.Programa));
    }

    [Fact]
    public void Listar_NaoIncluiServicoQueOProprioAppUsa()
    {
        // O TRANAL SENTINEL controla o RGB pelo OpenRGB: suspender esse
        // serviço quebraria a integração que o app oferece.
        var lista = ServicosSuspendiveis.Listar(new[]
        {
            Servico("OpenRGB", @"E:\Program Files\OpenRGB\OpenRGB.exe"),
        });

        Assert.Empty(lista);
    }

    [Fact]
    public void Suspender_NuncaTocaServicoDeIntegracaoDoApp()
    {
        var settings = new AppSettings();
        var alvo = new ServicoFalso();
        alvo.Modos["OpenRGB"] = new ModoServico { Modo = ModoServico.Automatico };

        int total = ServicosSuspender.Suspender(settings, new[] { Susp("OpenRGB", @"E:\Program Files\OpenRGB\OpenRGB.exe") }, alvo);

        Assert.Equal(0, total);
        Assert.Equal(ModoServico.Automatico, alvo.Modos["OpenRGB"].Modo);
        Assert.Empty(alvo.Parados);
        Assert.Empty(settings.TweakBackup);
    }

    /// <summary>Serviços de mentira: registra o que o app mandou fazer.</summary>
    private sealed class ServicoFalso : IServicoWindows
    {
        public Dictionary<string, ModoServico> Modos { get; } = new();
        public List<string> Parados { get; } = new();
        public List<string> Iniciados { get; } = new();
        public bool PodeConfigurar { get; set; } = true;
        public bool PodeParar { get; set; } = true;

        public ModoServico? LerModo(string nome) => Modos.TryGetValue(nome, out var m) ? m : null;

        public bool Configurar(string nome, ModoServico modo)
        {
            if (!PodeConfigurar) return false;
            Modos[nome] = modo;
            return true;
        }

        public bool Parar(string nome)
        {
            if (!PodeParar) return false;
            Parados.Add(nome);
            return true;
        }

        public bool Iniciar(string nome)
        {
            Iniciados.Add(nome);
            return true;
        }
    }

    [Fact]
    public void Suspender_GuardaOModoOriginalEDeixaManual()
    {
        var settings = new AppSettings();
        var alvo = new ServicoFalso();
        alvo.Modos["AdobeARCSService"] = new ModoServico { Modo = ModoServico.Automatico };

        int total = ServicosSuspender.Suspender(settings,
            new[] { Susp("AdobeARCSService", @"C:\Program Files\Adobe\ARC.exe") }, alvo);

        Assert.Equal(1, total);
        Assert.Equal(ModoServico.Manual, alvo.Modos["AdobeARCSService"].Modo);   // manual
        Assert.Contains("AdobeARCSService", alvo.Parados);
        // O backup carrega modo, atraso e caminho: é o que permite desfazer
        // (e o que impede um backup forjado de desativar um serviço).
        Assert.Equal("2;0;C:\\Program Files\\Adobe\\ARC.exe", settings.TweakBackup["servico.AdobeARCSService"]);
    }

    [Fact]
    public void Suspender_AutomaticoComAtrasoGuardaOAtraso()
    {
        // Sem isso, o serviço voltaria a subir no boot normal ao restaurar.
        var settings = new AppSettings();
        var alvo = new ServicoFalso();
        alvo.Modos["AdobeARCSService"] = new ModoServico { Modo = ModoServico.Automatico, Atrasado = true };

        ServicosSuspender.Suspender(settings,
            new[] { Susp("AdobeARCSService", @"C:\Program Files\Adobe\ARC.exe") }, alvo);

        Assert.StartsWith("2;1;", settings.TweakBackup["servico.AdobeARCSService"]);
    }

    [Fact]
    public void Suspender_ServicoDesativadoNaoEMexido()
    {
        // Desativado (4) não é um modo que o app sabe desfazer: ficar de
        // fora é a única forma de não deixar o serviço preso em Manual.
        var settings = new AppSettings();
        var alvo = new ServicoFalso();
        alvo.Modos["AdobeARCSService"] = new ModoServico { Modo = 4 };

        int total = ServicosSuspender.Suspender(settings,
            new[] { Susp("AdobeARCSService", @"C:\Program Files\Adobe\ARC.exe") }, alvo);

        Assert.Equal(0, total);
        Assert.Equal(4, alvo.Modos["AdobeARCSService"].Modo);
        Assert.Empty(alvo.Parados);
        Assert.Empty(settings.TweakBackup);
    }

    [Fact]
    public void Suspender_ServicoQueNaoConseguePararNaoGuardaBackup()
    {
        // Se o serviço não parou, nada mudou: não há nada para desfazer.
        var settings = new AppSettings();
        var alvo = new ServicoFalso { PodeParar = false };
        alvo.Modos["AdobeARCSService"] = new ModoServico { Modo = ModoServico.Automatico };

        int total = ServicosSuspender.Suspender(settings,
            new[] { Susp("AdobeARCSService", @"C:\Program Files\Adobe\ARC.exe") }, alvo);

        Assert.Equal(0, total);
        Assert.Empty(settings.TweakBackup);
    }

    [Fact]
    public void Suspender_NuncaTocaServicoDeSeguranca()
    {
        var settings = new AppSettings();
        var alvo = new ServicoFalso();
        alvo.Modos["McAfeeMPE"] = new ModoServico { Modo = ModoServico.Automatico };

        int total = ServicosSuspender.Suspender(settings,
            new[] { Susp("McAfeeMPE", @"C:\Program Files\McAfee\X.exe") }, alvo);

        Assert.Equal(0, total);
        Assert.Equal(ModoServico.Automatico, alvo.Modos["McAfeeMPE"].Modo);   // intocado
        Assert.Empty(alvo.Parados);
        Assert.Empty(settings.TweakBackup);
    }

    [Fact]
    public void Suspender_ServicoQueNaoExisteEPulado()
    {
        var settings = new AppSettings();
        var alvo = new ServicoFalso();   // nenhum serviço cadastrado

        int total = ServicosSuspender.Suspender(settings,
            new[] { Susp("NaoExiste", @"C:\Program Files\Nao\X.exe") }, alvo);

        Assert.Equal(0, total);
        Assert.Empty(settings.TweakBackup);
    }

    [Fact]
    public void Restaurar_DevolveOModoOriginalEInicia()
    {
        var settings = new AppSettings();
        settings.TweakBackup["servico.AdobeARCSService"] = "2;0;C:\\Program Files\\Adobe\\ARC.exe";
        var alvo = new ServicoFalso();
        alvo.Modos["AdobeARCSService"] = new ModoServico { Modo = ModoServico.Manual };

        int total = ServicosSuspender.Restaurar(settings, alvo);

        Assert.Equal(1, total);
        Assert.Equal(ModoServico.Automatico, alvo.Modos["AdobeARCSService"].Modo);   // volta a automático
        Assert.Contains("AdobeARCSService", alvo.Iniciados);
        Assert.False(settings.TweakBackup.ContainsKey("servico.AdobeARCSService"));
    }

    [Fact]
    public void Restaurar_DevolveOAutomaticoComAtraso()
    {
        var settings = new AppSettings();
        settings.TweakBackup["servico.AdobeARCSService"] = "2;1;C:\\Program Files\\Adobe\\ARC.exe";
        var alvo = new ServicoFalso();
        alvo.Modos["AdobeARCSService"] = new ModoServico { Modo = ModoServico.Manual };

        int total = ServicosSuspender.Restaurar(settings, alvo);

        Assert.Equal(1, total);
        Assert.Equal(ModoServico.Automatico, alvo.Modos["AdobeARCSService"].Modo);
        Assert.True(alvo.Modos["AdobeARCSService"].Atrasado);
    }

    [Fact]
    public void Restaurar_ServicoQueEraManualNaoEIniciado()
    {
        var settings = new AppSettings();
        settings.TweakBackup["servico.Meu"] = "3;0;C:\\Program Files\\Meu\\X.exe";
        var alvo = new ServicoFalso();
        alvo.Modos["Meu"] = new ModoServico { Modo = ModoServico.Manual };

        ServicosSuspender.Restaurar(settings, alvo);

        Assert.Equal(ModoServico.Manual, alvo.Modos["Meu"].Modo);
        Assert.Empty(alvo.Iniciados);
    }

    [Fact]
    public void Restaurar_BackupForjadoComModoDesativadoEDescartado()
    {
        // O settings.json é do usuário e pode ser editado à mão. Um backup
        // forjado não pode desligar nada — e um modo fora do que o app
        // grava é descartado sem tocar no serviço.
        var settings = new AppSettings();
        settings.TweakBackup["servico.Defender"] = "4;0;C:\\Program Files\\Defender\\X.exe";
        var alvo = new ServicoFalso();
        alvo.Modos["Defender"] = new ModoServico { Modo = ModoServico.Manual };

        int total = ServicosSuspender.Restaurar(settings, alvo);

        Assert.Equal(0, total);
        Assert.Equal(ModoServico.Manual, alvo.Modos["Defender"].Modo);   // intocado
        Assert.False(settings.TweakBackup.ContainsKey("servico.Defender"));
    }

    [Fact]
    public void Restaurar_BackupForjadoDeServicoDeSegurancaEDescartado()
    {
        var settings = new AppSettings();
        settings.TweakBackup["servico.McAfeeMPE"] = "2;0;C:\\Program Files\\McAfee\\X.exe";
        var alvo = new ServicoFalso();
        alvo.Modos["McAfeeMPE"] = new ModoServico { Modo = ModoServico.Manual };

        int total = ServicosSuspender.Restaurar(settings, alvo);

        Assert.Equal(0, total);
        Assert.Empty(alvo.Iniciados);
        Assert.False(settings.TweakBackup.ContainsKey("servico.McAfeeMPE"));
    }

    [Fact]
    public void Restaurar_BackupForjadoDeServicoDoWindowsEDescartado()
    {
        var settings = new AppSettings();
        settings.TweakBackup["servico.Wsearch"] = "2;0;C:\\Windows\\System32\\SearchIndexer.exe";
        var alvo = new ServicoFalso();
        alvo.Modos["Wsearch"] = new ModoServico { Modo = ModoServico.Manual };

        int total = ServicosSuspender.Restaurar(settings, alvo);

        Assert.Equal(0, total);
        Assert.False(settings.TweakBackup.ContainsKey("servico.Wsearch"));
    }

    [Fact]
    public void Restaurar_QuandoServicoNaoEstaMaisManualNaoSobrescreve()
    {
        // Alguém mexeu no serviço depois (botou em Automático ou
        // Desativado): a escolha de quem mexeu depois manda.
        var settings = new AppSettings();
        settings.TweakBackup["servico.AdobeARCSService"] = "2;0;C:\\Program Files\\Adobe\\ARC.exe";
        var alvo = new ServicoFalso();
        alvo.Modos["AdobeARCSService"] = new ModoServico { Modo = 4 };

        int total = ServicosSuspender.Restaurar(settings, alvo);

        Assert.Equal(0, total);
        Assert.Equal(4, alvo.Modos["AdobeARCSService"].Modo);
        Assert.Empty(alvo.Iniciados);
        Assert.False(settings.TweakBackup.ContainsKey("servico.AdobeARCSService"));
    }

    [Fact]
    public void Restaurar_QuandoWindowsRecusaOBancoFicaParaTentarDeNovo()
    {
        var settings = new AppSettings();
        settings.TweakBackup["servico.AdobeARCSService"] = "2;0;C:\\Program Files\\Adobe\\ARC.exe";
        var alvo = new ServicoFalso { PodeConfigurar = false };
        alvo.Modos["AdobeARCSService"] = new ModoServico { Modo = ModoServico.Manual };

        int total = ServicosSuspender.Restaurar(settings, alvo);

        Assert.Equal(0, total);
        Assert.True(settings.TweakBackup.ContainsKey("servico.AdobeARCSService"));
    }

    [Fact]
    public void Suspensos_ListaOQueOAppMudou()
    {
        var settings = new AppSettings();
        settings.TweakBackup["servico.Um"] = "2;0;C:\\X.exe";
        settings.TweakBackup["servico.Outro"] = "2;0;C:\\Y.exe";
        settings.TweakBackup["aspm"] = "plano|1";

        var suspensos = ServicosSuspender.Suspensos(settings);

        Assert.Equal(2, suspensos.Count);
        Assert.DoesNotContain("aspm", suspensos);
    }

    // ------------------------------------------------------------------ executor

    private sealed class AlvoFalso : IAcaoAlvo
    {
        public bool Elevado { get; set; } = true;
        public string? Plano { get; set; } = "381b4222-f694-41f0-9685-ff5bb260df2e";
        public int? Aspm { get; set; } = 1;
        public bool AceitaAspm { get; set; } = true;
        public int? AspmAplicado { get; private set; }
        public string? PlanoAplicado { get; private set; }
        public List<ServicoSuspendivel> Suspensos { get; } = new();
        public int Restaurados { get; set; }

        public string? PlanoAtivo() => Plano;
        public int? LerAspm(string plano) => Aspm;
        public bool AplicarAspm(string plano, int valor)
        {
            if (!AceitaAspm) return false;
            PlanoAplicado = plano;
            AspmAplicado = valor;
            return true;
        }
        public int SuspenderServicos(AppSettings settings, IEnumerable<ServicoSuspendivel> servicos)
        {
            Suspensos.AddRange(servicos);
            return servicos.Count();
        }
        public int RestaurarServicos(AppSettings settings) => Restaurados;
    }

    [Fact]
    public void Executor_DesligarAspmGuardaOValorOriginalEOPlano()
    {
        var settings = new AppSettings();
        var alvo = new AlvoFalso { Aspm = 1 };
        var executor = new AcaoExecutor(alvo, settings);

        var resultado = executor.Executar(AcoesSaude.DesligarAspm());

        Assert.True(resultado.Sucesso);
        Assert.Equal(0, alvo.AspmAplicado);
        // O plano entra no backup: restaurar no plano errado não desfaz nada.
        Assert.Equal(alvo.Plano + "|1", settings.TweakBackup[AcaoExecutor.ChaveAspm]);
    }

    [Fact]
    public void Executor_DesligarAspmSemAdministradorNaoMexeEmNada()
    {
        var settings = new AppSettings();
        var alvo = new AlvoFalso { Elevado = false };
        var executor = new AcaoExecutor(alvo, settings);

        var resultado = executor.Executar(AcoesSaude.DesligarAspm());

        Assert.False(resultado.Sucesso);
        Assert.Null(alvo.AspmAplicado);
        Assert.Empty(settings.TweakBackup);
        Assert.Contains("administrador", resultado.Mensagem, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Executor_RestaurarAspmVoltaOValorGuardadoNoPlanoGuardado()
    {
        var settings = new AppSettings();
        settings.TweakBackup[AcaoExecutor.ChaveAspm] = "381b4222-f694-41f0-9685-ff5bb260df2e|2";
        var alvo = new AlvoFalso { Aspm = 0, Plano = "outro-plano-0000-0000-0000-000000000000" };
        var executor = new AcaoExecutor(alvo, settings);

        var resultado = executor.Executar(AcoesSaude.RestaurarAspm());

        Assert.True(resultado.Sucesso);
        Assert.Equal(2, alvo.AspmAplicado);
        Assert.Equal("381b4222-f694-41f0-9685-ff5bb260df2e", alvo.PlanoAplicado);
        Assert.False(settings.TweakBackup.ContainsKey(AcaoExecutor.ChaveAspm));
    }

    [Fact]
    public void Executor_RestaurarAspmQuandoOWindowsRecusaMantemOValorGuardado()
    {
        // O backup é a prova de que o app mexeu: sem ele, o usuário perde o
        // caminho de volta. Falhou? O backup fica.
        var settings = new AppSettings();
        settings.TweakBackup[AcaoExecutor.ChaveAspm] = "381b4222-f694-41f0-9685-ff5bb260df2e|2";
        var alvo = new AlvoFalso { Aspm = 0, AceitaAspm = false };
        var executor = new AcaoExecutor(alvo, settings);

        var resultado = executor.Executar(AcoesSaude.RestaurarAspm());

        Assert.False(resultado.Sucesso);
        Assert.True(settings.TweakBackup.ContainsKey(AcaoExecutor.ChaveAspm));
    }

    [Fact]
    public void Executor_DesligarAspmQuandoOWindowsRecusaNaoJuraQueDeuCerto()
    {
        var settings = new AppSettings();
        var alvo = new AlvoFalso { Aspm = 1, AceitaAspm = false };
        var executor = new AcaoExecutor(alvo, settings);

        var resultado = executor.Executar(AcoesSaude.DesligarAspm());

        Assert.False(resultado.Sucesso);
        // O valor original continua guardado: o app ainda pode tentar depois.
        Assert.True(settings.TweakBackup.ContainsKey(AcaoExecutor.ChaveAspm));
    }

    [Fact]
    public void Executor_RestaurarAspmSemValorGuardadoNaoFazNada()
    {
        var settings = new AppSettings();
        var alvo = new AlvoFalso();
        var executor = new AcaoExecutor(alvo, settings);

        var resultado = executor.Executar(AcoesSaude.RestaurarAspm());

        Assert.False(resultado.Sucesso);
        Assert.Null(alvo.AspmAplicado);
    }

    [Fact]
    public void Executor_AcoesDeNavegacaoChamamONavegador()
    {
        var settings = new AppSettings();
        var alvo = new AlvoFalso();
        string? destino = null;
        var executor = new AcaoExecutor(alvo, settings, aba => destino = aba);

        executor.Executar(AcoesSaude.AbrirInicializacao());
        Assert.Equal("PageStartup", destino);

        executor.Executar(AcoesSaude.AbrirDrivers());
        Assert.Equal("PageDriver", destino);
    }

    [Fact]
    public void Executor_AcaoDesconhecidaNaoEstoura()
    {
        var executor = new AcaoExecutor(new AlvoFalso(), new AppSettings());

        var resultado = executor.Executar(new AcaoAplicavel { Id = "nao-existe", Rotulo = "?" });

        Assert.False(resultado.Sucesso);
    }

    // ------------------------------------------------------------- programa travado

    [Fact]
    public void Travado_NaoAvisaAntesDoTempoMinimo()
    {
        var agora = new DateTime(2026, 11, 5, 10, 0, 0, DateTimeKind.Utc);
        var monitor = new MonitorTravados(() => new[] { new JanelaTravada(1234, "Jogo", "Jogo") },
                                          () => agora) { SegundosMinimos = 30 };
        var avisados = new List<AppTravado>();
        monitor.Travou += a => avisados.Add(a);

        monitor.Verificar();                       // começa a contar
        agora = agora.AddSeconds(20);
        monitor.Verificar();                       // ainda não deu o tempo

        Assert.Empty(avisados);
    }

    [Fact]
    public void Travado_AvisaUmaVezERepeteNao()
    {
        var agora = new DateTime(2026, 11, 5, 10, 0, 0, DateTimeKind.Utc);
        var monitor = new MonitorTravados(() => new[] { new JanelaTravada(1234, "Jogo", "Jogo") },
                                          () => agora) { SegundosMinimos = 30 };
        var avisados = new List<AppTravado>();
        monitor.Travou += a => avisados.Add(a);

        monitor.Verificar();
        agora = agora.AddSeconds(35);
        monitor.Verificar();
        Assert.Single(avisados);

        agora = agora.AddSeconds(30);
        monitor.Verificar();
        Assert.Single(avisados);   // não repete enquanto continua travado
    }

    [Fact]
    public void Travado_QuandoVoltaAResponderAvisaDeNovoNaProximaTravada()
    {
        var agora = new DateTime(2026, 11, 5, 10, 0, 0, DateTimeKind.Utc);
        bool travado = true;
        var monitor = new MonitorTravados(() => travado
                ? new[] { new JanelaTravada(1234, "Jogo", "Jogo") }
                : Array.Empty<JanelaTravada>(),
            () => agora) { SegundosMinimos = 30 };
        var avisados = new List<AppTravado>();
        monitor.Travou += a => avisados.Add(a);

        monitor.Verificar();
        agora = agora.AddSeconds(35);
        monitor.Verificar();
        Assert.Single(avisados);

        travado = false;
        monitor.Verificar();                       // voltou a responder
        Assert.Single(avisados);

        travado = true;
        monitor.Verificar();                       // travou de novo
        agora = agora.AddSeconds(35);
        monitor.Verificar();
        Assert.Equal(2, avisados.Count);
    }

    [Fact]
    public void Travado_ListaOsQueEstaoTravados()
    {
        var agora = new DateTime(2026, 11, 5, 10, 0, 0, DateTimeKind.Utc);
        var monitor = new MonitorTravados(() => new[] { new JanelaTravada(1234, "Jogo", "Jogo") },
                                          () => agora);

        monitor.Verificar();
        agora = agora.AddSeconds(90);

        var travados = monitor.TravadosAgora();
        Assert.Single(travados);
        Assert.Equal(90, travados[0].Segundos);
    }

    [Fact]
    public void Travado_ListaQueFalhaNaoQuebra()
    {
        var monitor = new MonitorTravados(() => throw new InvalidOperationException("falhou"));

        monitor.Verificar();   // não deve lançar
    }

    [Fact]
    public void Travado_ListaAvisadosSoTemQuemPassouDoTempoMinimo()
    {
        // Uma janela lenta de 3 s não é um travamento: a bandeja não pode
        // oferecer "encerrar" para ela.
        var agora = new DateTime(2026, 11, 5, 10, 0, 0, DateTimeKind.Utc);
        var monitor = new MonitorTravados(() => new[] { new JanelaTravada(1234, "Jogo", "Jogo") },
                                          () => agora) { SegundosMinimos = 30 };

        monitor.Verificar();
        agora = agora.AddSeconds(5);
        monitor.Verificar();
        Assert.Empty(monitor.TravadosAvisados());

        agora = agora.AddSeconds(30);
        monitor.Verificar();
        Assert.Single(monitor.TravadosAvisados());
    }

    [Fact]
    public void Travado_QuandoUmVoltaOsOutrosContinuamAvisados()
    {
        // Dois travados; um volta a responder: o outro continua na lista.
        var agora = new DateTime(2026, 11, 5, 10, 0, 0, DateTimeKind.Utc);
        bool primeiroVoltou = false;
        var monitor = new MonitorTravados(() => primeiroVoltou
                ? new[] { new JanelaTravada(2222, "Jogo2", "Jogo2") }
                : new[] { new JanelaTravada(1111, "Jogo1", "Jogo1"), new JanelaTravada(2222, "Jogo2", "Jogo2") },
            () => agora) { SegundosMinimos = 30 };

        monitor.Verificar();
        agora = agora.AddSeconds(35);
        monitor.Verificar();
        Assert.Equal(2, monitor.TravadosAvisados().Count);

        primeiroVoltou = true;
        monitor.Verificar();

        var avisados = monitor.TravadosAvisados();
        Assert.Single(avisados);
        Assert.Equal(2222, avisados[0].Pid);
    }

    [Fact]
    public void Travado_AlguemPodePedirAListaEnquantoOVisoERegistrado()
    {
        // O aviso vai para a thread da interface (Dispatcher.Invoke) e a
        // interface pode pedir a lista de travados — que usa a mesma trava.
        // Se o aviso saísse dentro da trava, o aviso ficaria esperando a
        // interface e a interface ficaria esperando a trava: travamento.
        var monitor = new MonitorTravados(() => new[] { new JanelaTravada(1234, "Jogo", "Jogo") },
                                          () => DateTime.UtcNow) { SegundosMinimos = 0 };

        var avisado = new ManualResetEventSlim(false);
        var libera = new ManualResetEventSlim(false);
        monitor.Travou += _ =>
        {
            avisado.Set();
            libera.Wait(3000);   // simula a interface esperando o aviso
        };

        var verificador = new Thread(() => monitor.Verificar());
        verificador.Start();
        Assert.True(avisado.Wait(3000), "o aviso não chegou");

        var pediu = new ManualResetEventSlim(false);
        var leitor = new Thread(() => { monitor.TravadosAgora(); pediu.Set(); });
        leitor.Start();

        bool respondeu = pediu.Wait(2000);
        libera.Set();
        verificador.Join(3000);
        leitor.Join(3000);

        Assert.True(respondeu, "pedir a lista de travados travou enquanto o aviso era registrado");
    }

    // ------------------------------------------------------------------- alertas

    [Fact]
    public void Alerta_CpuQuenteAvisaUmaVez()
    {
        var monitor = new MonitorAlertas();
        var avisos = new List<Alerta>();
        monitor.Alertar += a => avisos.Add(a);

        monitor.Verificar(new MonitorAlertas.Estado(90, null, null, true));
        monitor.Verificar(new MonitorAlertas.Estado(91, null, null, true));

        Assert.Single(avisos);
        Assert.Equal("temperatura-cpu", avisos[0].Id);
    }

    [Fact]
    public void Alerta_QuePassaSomeEVoltaAAvisar()
    {
        var monitor = new MonitorAlertas();
        var avisos = new List<Alerta>();
        monitor.Alertar += a => avisos.Add(a);

        monitor.Verificar(new MonitorAlertas.Estado(90, null, null, true));
        monitor.Verificar(new MonitorAlertas.Estado(60, null, null, true));
        Assert.Single(avisos);
        Assert.Empty(monitor.Ativos);

        monitor.Verificar(new MonitorAlertas.Estado(90, null, null, true));
        Assert.Equal(2, avisos.Count);
    }

    [Fact]
    public void Alerta_CpuNormalNaoAvisa()
    {
        var monitor = new MonitorAlertas();

        var novos = monitor.Verificar(new MonitorAlertas.Estado(70, null, null, true));

        Assert.Empty(novos);
    }

    [Fact]
    public void Alerta_BateriaBaixaSoAvisaForaDaTomada()
    {
        var monitor = new MonitorAlertas();
        var avisos = new List<Alerta>();
        monitor.Alertar += a => avisos.Add(a);

        monitor.Verificar(new MonitorAlertas.Estado(60, null, 10, true));    // carregando
        Assert.Empty(avisos);

        monitor.Verificar(new MonitorAlertas.Estado(60, null, 10, false));   // na bateria
        Assert.Single(avisos);
        Assert.Equal("bateria-baixa", avisos[0].Id);
    }

    [Fact]
    public void Alerta_DiscoQuenteAvisa()
    {
        var monitor = new MonitorAlertas();

        var novos = monitor.Verificar(new MonitorAlertas.Estado(60, 65, null, true));

        Assert.Single(novos);
        Assert.Equal("temperatura-disco", novos[0].Id);
    }

    [Fact]
    public void Alerta_DiscoNormalNaoAvisa()
    {
        var monitor = new MonitorAlertas();

        Assert.Empty(monitor.Verificar(new MonitorAlertas.Estado(60, 45, null, true)));
    }
}
