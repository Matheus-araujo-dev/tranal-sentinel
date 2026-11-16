using System.Diagnostics;
using System.Windows;
using System.Threading;
using LibreHardwareMonitor.Hardware;

namespace TranalSentinel;

/// <summary>
/// A vigia do PC em segundo plano: programa travado e alertas de
/// temperatura, bateria e disco.
///
/// <para>
/// Tudo aqui é opcional por natureza: se a vigia falhar (ou o Windows não
/// deixar), o app continua funcionando como antes. A única regra: nenhum
/// aviso tira o foco do usuário — quem decide o que fazer é ele.
/// </para>
/// </summary>
public partial class MainWindow
{
    private MonitorTravados? _monitorTravados;
    private System.Threading.Timer? _timerTravados;
    private int _vigiandoTravados;

    private readonly MonitorAlertas _alertas = new();
    private System.Threading.Timer? _timerAlertas;

    // Vigia da tecla presa: o Windows as vezes continua achando que
    // Shift, Ctrl, Alt ou Win está pressionado depois que o dono solta.
    private MonitorTeclaPresa? _monitorTeclaPresa;
    private VigiaTeclado? _vigiaTeclado;
    private System.Threading.Timer? _timerTeclaPresa;
    private int _vigiandoTecla;

    /// <summary>Quando o gancho de teclado foi reparado pela última vez.</summary>
    private long _ultimaReparacao;

    /// <summary>Quantas sondas seguidas o gancho não respondeu.</summary>
    private int _sondasSemResposta;

    /// <summary>Liga a vigia (chamada na abertura da janela).</summary>
    private void IniciarVigia()
    {
        try
        {
            _monitorTravados = new MonitorTravados(JanelasWindows.ListarTravadas) { SegundosMinimos = 30 };
            _monitorTravados.Travou += app => Dispatcher.Invoke(() => AoProgramaTravou(app));
            // Quando um volta a responder, a opção de encerrar continua lá se
            // houver outro travado: quem some é só o programa que voltou.
            _monitorTravados.Respondeu += app => Dispatcher.Invoke(AtualizarBandejaTravados);
            _timerTravados = new System.Threading.Timer(_ => VigiarTravados(), null, 8000, 8000);
        }
        catch (Exception ex)
        {
            _monitorTravados = null;
            try { RegistroDiario.Registrar("Vigia de programa travado indisponível: " + ex.Message, "VIGIA"); }
            catch { }
        }

        try
        {
            _alertas.Alertar += alerta => Dispatcher.Invoke(() => AoAlertar(alerta));
            _timerAlertas = new System.Threading.Timer(_ => VigiarAlertas(), null, 25000, 30000);
        }
        catch
        {
            // Sem alertas o app segue: é um extra, não uma função essencial.
        }

        try
        {
            AplicarPreferenciaDaVigia();
        }
        catch (Exception ex)
        {
            _monitorTeclaPresa = null;
            try { RegistroDiario.Registrar("Vigia de tecla presa indisponível: " + ex.Message, "VIGIA"); }
            catch
            {
                // Diário é acessório: falhar nele não pode derrubar o app.
            }
        }
    }

    /// <summary>
    /// Instala (ou desinstala) a vigia da tecla presa conforme a preferência
    /// do dono.
    ///
    /// <para>
    /// Desligada, a vigia não fica nem com o gancho de teclado instalado: o
    /// app não olha o teclado. Religar instala tudo de novo — e o que ficou
    /// para trás é esquecido, para o silêncio antigo não virar "prova" de
    /// tecla presa.
    /// </para>
    /// </summary>
    private void AplicarPreferenciaDaVigia()
    {
        bool ligada = _settings.CorrigirTeclaPresa == true;

        if (!ligada)
        {
            try { _timerTeclaPresa?.Dispose(); }
            catch
            {
                // Descartar o timer não pode derrubar a vigia.
            }
            _timerTeclaPresa = null;

            try { _vigiaTeclado?.Remover(); }
            catch
            {
                // O gancho sai com o app de qualquer jeito.
            }
            _vigiaTeclado = null;
            _monitorTeclaPresa?.Esquecer();
            AtualizarEstadoDaVigia();
            return;
        }

        if (_monitorTeclaPresa is not null && _vigiaTeclado is { Instalado: true })
        {
            AtualizarEstadoDaVigia();
            return;
        }

        // O gancho de teclado mora numa thread só dela, com a sua bomba de
        // mensagens: a thread da interface pode travar (e um gancho de baixo
        // nível instalado nela morreria em silêncio).
        var vigia = new VigiaTeclado();
        if (!vigia.Instalar())
        {
            // Sem o gancho o app não sabe se o teclado está em silêncio —
            // e silêncio sem prova não corrige tecla nenhuma.
            vigia.Remover();
            _monitorTeclaPresa = null;
            _vigiaTeclado = null;
            AtualizarEstadoDaVigia();
            try { RegistroDiario.Registrar("Vigia de tecla presa indisponível: o Windows recusou o gancho de teclado.", "VIGIA"); }
            catch
            {
                // Diário é acessório: falhar nele não pode derrubar o app.
            }
            return;
        }

        _vigiaTeclado = vigia;
        _monitorTeclaPresa = new MonitorTeclaPresa(
            TeclasWindows.ModificadorasPressionadas,
            // Relógio monotônico: o de parede salta quando a hora é
            // corrigida, e o salto faria o app soltar a tecla que o dono
            // está segurando.
            () => Environment.TickCount64,
            () => SilencioDoTeclado(vigia),
            () => TeclasWindows.RepeticaoDoTecladoLigada(),
            TeclasWindows.SoltarTodas,
            relogio: null,
            // O gancho pode ter morrido (o Windows o remove em silêncio
            // quando o PC trava): sem prova de vida, o app não solta nada.
            ganchoEstaVivo: () => GanchoEstaVivo(),
            // O dono pode desligar a correção no meio da ronda: o app
            // confere isso na hora de soltar.
            podeAgir: () => _settings.CorrigirTeclaPresa == true);
        // InvokeAsync (e não Invoke): o aviso vai para a fila da
        // interface sem prender a thread da vigia — e sem que a
        // interface travada trave a vigia.
        _monitorTeclaPresa.Corrigida += tecla => Dispatcher.InvokeAsync(() => AoTeclaPresaCorrigida(tecla));
        _monitorTeclaPresa.NaoConseguiu += tecla => Dispatcher.InvokeAsync(() => AoTeclaPresaNaoSoltou(tecla));

        try { _timerTeclaPresa?.Dispose(); }
        catch
        {
            // Descartar o timer antigo não pode derrubar a vigia.
        }
        _timerTeclaPresa = new System.Threading.Timer(_ => VigiarTeclaPresa(), null, 15000, 5000);
        AtualizarEstadoDaVigia();

        // A correção é a única ação que o app faz por conta própria: ela
        // fica no diário desde o começo, para o dono ver que está ligada.
        try { RegistroDiario.Registrar("Vigia de tecla presa ativa: o app vai soltar a tecla que ficar presa.", "VIGIA"); }
        catch
        {
            // Diário é acessório: falhar nele não pode derrubar o app.
        }
    }

    /// <summary>
    /// O texto do cartão que diz o que a vigia está fazendo agora — a tela
    /// não pode dizer que a vigia está ativa depois de o dono desligar a
    /// correção.
    /// </summary>
    private void AtualizarEstadoDaVigia()
    {
        if (ChkTeclaPresaEstado is null) return;

        if (_settings.CorrigirTeclaPresa != true)
        {
            ChkTeclaPresaEstado.Text = "Correção automática desligada: o app não olha o teclado.";
            return;
        }

        ChkTeclaPresaEstado.Text = _vigiaTeclado is { Instalado: true }
            ? "Vigia ativa: o app acompanha o teclado e solta a tecla que ficar presa."
            : "Vigia parada: o gancho do teclado não está instalado (o Windows recusou ou "
              + "derrubou), e sem ele o app não consegue ver o silêncio do teclado. "
              + "A correção automática está desligada.";
    }

    /// <summary>
    /// Há quanto tempo o teclado não manda evento? E, se o gancho morreu, o
    /// app tenta consertá-lo (sem soltar nada).
    /// </summary>
    /// <remarks>
    /// O Windows remove um gancho de baixo nível em silêncio quando o PC
    /// trava. Sem gancho não há silêncio, e sem silêncio a vigia se cala —
    /// mas o gancho pode ser reinstalado, e aí a vigia volta a funcionar.
    /// </remarks>
    private TimeSpan? SilencioDoTeclado(VigiaTeclado vigia)
    {
        if (vigia.Instalado) return vigia.Silencio;

        ReparaOGancho(vigia);
        return null;
    }

    /// <summary>
    /// O gancho de teclado ainda está vivo?
    /// </summary>
    /// <remarks>
    /// O Windows remove um gancho de baixo nível em silêncio quando o PC
    /// trava, e aí o "silêncio" do teclado vira falso — o app soltaria a
    /// tecla que o dono está segurando, que é o pior dano possível. A sonda
    /// injeta um key-up de F24 (tecla que ninguém usa) e confere se o gancho
    /// viu. Se não viu, o app repara o gancho e <b>não</b> solta nada.
    /// </remarks>
    private bool GanchoEstaVivo()
    {
        var vigia = _vigiaTeclado;
        if (vigia is null) return false;

        // O dono desligou a correção no meio da ronda: o app não repara
        // nada e não solta nada.
        if (_settings.CorrigirTeclaPresa != true) return false;

        bool vivo = false;
        try { vivo = vigia.EstaVivo(); }
        catch
        {
            // Gancho que não responde é o mesmo que gancho morto.
        }

        if (vivo)
        {
            Interlocked.Exchange(ref _sondasSemResposta, 0);
            return true;
        }

        // Uma sonda que falha pode ser só a máquina carregada (medido em
        // 16/11/2026: com três compilações ao mesmo tempo, a injeção
        // demora a chegar no gancho). Consertar o gancho nesse caso zera o
        // silêncio e atrasa a correção em 30 s — por isso o conserto só
        // acontece depois de duas falhas seguidas.
        if (Interlocked.Increment(ref _sondasSemResposta) >= 2) ReparaOGancho(vigia);
        return false;
    }

    /// <summary>
    /// Reinstala o gancho de teclado (no máximo uma vez por minuto).
    /// </summary>
    /// <remarks>
    /// O reparo acontece na thread da vigia, então o texto do cartão vai
    /// pela fila da interface — escrever nele direto daqui daria erro de
    /// thread.
    /// </remarks>
    private void ReparaOGancho(VigiaTeclado vigia)
    {
        long agora = Environment.TickCount64;
        long ultima = Interlocked.Read(ref _ultimaReparacao);
        if (ultima != 0 && agora - ultima < 60_000) return;
        Interlocked.Exchange(ref _ultimaReparacao, agora);

        bool voltou = false;
        try
        {
            vigia.Remover();
            voltou = vigia.Instalar();
        }
        catch
        {
            // Gancho que não volta é o mesmo que gancho morto: o app se cala.
        }

        // A tela não pode dizer "vigia ativa" com o gancho fora.
        Dispatcher.InvokeAsync(() => AtualizarEstadoDaVigia());

        if (!voltou)
        {
            try { RegistroDiario.Registrar("Vigia de tecla presa: o gancho do teclado não voltou.", "VIGIA"); }
            catch
            {
                // Diário é acessório: falhar nele não pode derrubar o app.
            }
        }
    }

    /// <summary>Para a vigia (chamada no fechamento da janela).</summary>
    private void PararVigia()
    {
        try { _timerTravados?.Dispose(); } catch { }
        try { _timerAlertas?.Dispose(); } catch { }
        try { _timerTeclaPresa?.Dispose(); } catch { }
        // O gancho de teclado também some: ele não pode ficar instalado
        // depois do app fechar.
        try { _vigiaTeclado?.Dispose(); } catch { }
        _timerTravados = null;
        _timerAlertas = null;
        _timerTeclaPresa = null;
        _vigiaTeclado = null;
    }

    // ------------------------------------------------------- programa travado

    private void VigiarTravados()
    {
        // A varredura de janelas é lenta (300 ms por janela travada): se a
        // volta anterior ainda está rodando, esta é pulada.
        if (Interlocked.Exchange(ref _vigiandoTravados, 1) == 1) return;
        try
        {
            _monitorTravados?.Verificar();
        }
        catch
        {
            // Vigia com problema não pode derrubar o app.
        }
        finally
        {
            Interlocked.Exchange(ref _vigiandoTravados, 0);
        }
    }

    private void AoProgramaTravou(AppTravado app)
    {
        _bandeja?.DefinirTravado(true, $"Encerrar {app.Processo}");
        Toast("Programa travado", $"{app.Resumo}. Para encerrar, use o menu do ícone do TRANAL SENTINEL.", "⚠");
        try { RegistroDiario.Registrar($"Travado: {app.Processo} (pid {app.Pid}) há {app.Segundos}s", "VIGIA"); }
        catch { }
    }

    /// <summary>
    /// Atualiza a opção de encerrar da bandeja. Só entra na lista o programa
    /// que o app já avisou (passou do tempo mínimo) — uma janela lenta de 3 s
    /// não é um travamento.
    /// </summary>
    private void AtualizarBandejaTravados()
    {
        var avisados = _monitorTravados?.TravadosAvisados();
        bool tem = avisados is not null && avisados.Count > 0;
        _bandeja?.DefinirTravado(tem, tem ? $"Encerrar {avisados![0].Processo}" : "Encerrar programa travado");
    }

    /// <summary>Encerra o programa travado (o usuário pediu pelo menu do ícone).</summary>
    private void EncerrarTravado()
    {
        var travados = _monitorTravados?.TravadosAvisados();
        if (travados is null || travados.Count == 0)
        {
            _bandeja?.DefinirTravado(false);
            return;
        }

        var app = travados[0];
        var resposta = MessageBox.Show(
            this,
            $"{app.Resumo}.\n\nEncerrar o programa? O trabalho que ele não salvou vai se perder.",
            "TRANAL SENTINEL",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning);

        if (resposta != MessageBoxResult.OK) return;

        try
        {
            using var p = Process.GetProcessById(app.Pid);

            // O Windows reaproveita o número do processo: o PID que o app
            // guardou pode ser outro programa agora. O nome é a conferência
            // antes de encerrar qualquer coisa.
            if (!string.Equals(p.ProcessName, app.Processo, StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show(
                    this,
                    $"O programa {app.Processo} já não está mais travado — o Windows reaproveitou o número dele. Nada foi encerrado.",
                    "TRANAL SENTINEL", MessageBoxButton.OK, MessageBoxImage.Information);
                AtualizarBandejaTravados();
                return;
            }

            p.Kill();
            Toast("Programa encerrado", $"{app.Processo} foi encerrado.", "✓");
            try { RegistroDiario.Registrar($"Encerrado (travado): {app.Processo} (pid {app.Pid})", "VIGIA"); }
            catch { }
            AtualizarBandejaTravados();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Não deu para encerrar: " + ex.Message, "TRANAL SENTINEL", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    // -------------------------------------------------------------- alertas

    private void VigiarAlertas()
    {
        try
        {
            // Cada sensor é lido por conta: a lista de sensores muda na thread
            // da tela e uma leitura pode falhar no meio. Um sensor que falha
            // não pode derrubar o ciclo inteiro de avisos.
            float? cpu = LerSensor(() => _monitor.CpuTemperature);
            List<SensorRow> linhas = LinhasDosSensores();
            float? disco = MonitorAlertas.TemperaturaMaximaDisco(linhas);

            // Bateria: valem todas as baterias do PC (um notebook pode ter
            // mais de uma). O alerta usa a menor carga.
            int? bateria = null;
            bool carregando = false;
            try
            {
                foreach (var cartao in BatteryInfo.Read(linhas))
                {
                    if (!cartao.Title.Contains("Bateria", StringComparison.OrdinalIgnoreCase)) continue;

                    foreach (var metrica in cartao.Metrics)
                    {
                        if (metrica.Key == "Carga atual" &&
                            int.TryParse(metrica.Value.TrimEnd('%'), out int pct))
                            bateria = bateria is null ? pct : Math.Min(bateria.Value, pct);
                        if (metrica.Key == "Estado" &&
                            (metrica.Value.Contains("Carregando", StringComparison.OrdinalIgnoreCase) ||
                             metrica.Value.Contains("Tomada", StringComparison.OrdinalIgnoreCase)))
                            carregando = true;
                    }
                }
            }
            catch
            {
                // Sem bateria (PC de mesa) é o normal.
            }

            var contexto = new ContextoSensores
            {
                TemperaturaCpu = cpu,
                TemperaturaGpu = LerSensor(() => _monitor.GpuTemperature),
                TemperaturaDisco = disco,
                CargaCpu = LerSensor(() => _monitor.CpuLoad),
                CargaGpu = LerSensor(() => _monitor.GpuLoad),
                ClockCpu = LerSensor(() => _monitor.CpuClocks().Avg),
                ClockGpu = LerSensor(() => _monitor.GpuClock),
                UsoRam = LerSensor(() => _monitor.RamUso),
                Bateria = bateria,
                Carregando = carregando,
                Ventoinhas = VentoinhasDoContexto(),
            };

            // A cópia evita que a tela (que edita as regras ao mesmo tempo)
            // estrague a leitura da vigia.
            List<RegraAlerta> regras = CopiaRegras();

            _alertas.Verificar(DateTime.Now, regras, contexto);
        }
        catch
        {
            // Alerta que falha não pode derrubar o app.
        }
    }

    /// <summary>
    /// As ventoinhas que o app pode vigiar neste ciclo.
    ///
    /// <para>
    /// Entra <b>toda</b> ventoinha que o app conhece — inclusive a que
    /// parou de dar leitura (ela fica sem RPM). A regra só avalia a que
    /// está lendo, e a lista completa é o que permite ao app dizer que a
    /// regra da outra não está vigiando nada (e não calar sobre isso).
    /// </para>
    /// </summary>
    private List<ContextoVentoinha> VentoinhasDoContexto()
    {
        try
        {
            return _ventoinhas.Cartoes()
                .Select(c => new ContextoVentoinha(
                    c.Id, c.Rotulo, c.RpmAtual is double rpm ? (float)rpm : null))
                .ToList();
        }
        catch
        {
            // A vigia não pode derrubar por causa das ventoinhas.
            return new List<ContextoVentoinha>();
        }
    }

    /// <summary>
    /// Lê um sensor sem derrubar a vigia: a lista de sensores é atualizada na
    /// thread da tela e uma leitura pode falhar no meio da contagem.
    /// </summary>
    private static float? LerSensor(Func<float?> leitura)
    {
        try { return leitura(); }
        catch { return null; }
    }

    /// <summary>
    /// Uma cópia das linhas dos sensores (a vigia roda em outra thread e a
    /// lista muda na thread da tela).
    /// </summary>
    private List<SensorRow> LinhasDosSensores()
    {
        try { return _monitor.Rows.ToList(); }
        catch { return new List<SensorRow>(); }
    }

    /// <summary>
    /// As regras de alerta que valem agora.
    ///
    /// <para>
    /// Se o usuário nunca mexeu nas regras (<c>null</c>), as três regras
    /// padrão entram no lugar — e são gravadas para o usuário ver e poder
    /// editar. Uma lista vazia é o usuário apagando tudo: isso é respeitado.
    /// </para>
    ///
    /// <para>
    /// Devolve a lista viva das configurações: quem for mudar tem que estar
    /// dentro do <see cref="TravaAlertas"/>. Quem for só ler usa
    /// <see cref="CopiaRegras"/>.
    /// </para>
    /// </summary>
    private List<RegraAlerta> RegrasAtivas()
    {
        lock (TravaAlertas)
        {
            if (_settings.Alertas is null)
            {
                _settings.Alertas = RegrasAlertaPadrao.Criar();
                _settings.Save();
            }
            return _settings.Alertas;
        }
    }

    /// <summary>
    /// Uma cópia das regras (a vigia lê de outra thread e a tela pode estar
    /// mudando a lista no mesmo instante).
    /// </summary>
    private List<RegraAlerta> CopiaRegras()
    {
        lock (TravaAlertas) return new List<RegraAlerta>(RegrasAtivas());
    }

    private void AoAlertar(Alerta alerta)
    {
        Toast(alerta.Titulo, alerta.Texto, "⚠");
        try { RegistroDiario.Registrar($"Alerta: {alerta.Titulo} — {alerta.Texto}", "ALERTA"); }
        catch { }
    }

    // ------------------------------------------------------- tecla presa

    /// <summary>
    /// Um ciclo da vigia da tecla presa. A decisão é da regra pura; aqui só
    /// entra a medida e o "soltar".
    /// </summary>
    private void VigiarTeclaPresa()
    {
        try
        {
            // O dono pode desligar a correção pela página do TRANAL
            // SAÚDE: desligada, a vigia nem olha o teclado — e o que ela
            // estava acompanhando é esquecido, para o silêncio antigo não
            // virar "prova" quando o dono religar.
            if (_settings.CorrigirTeclaPresa != true)
            {
                _monitorTeclaPresa?.Esquecer();
                return;
            }

            if (Interlocked.Exchange(ref _vigiandoTecla, 1) == 1) return;
            try
            {
                _monitorTeclaPresa?.Verificar();
            }
            catch
            {
                // Vigia com problema não pode derrubar o app.
            }
            finally
            {
                Interlocked.Exchange(ref _vigiandoTecla, 0);
            }
        }
        catch
        {
            // Vigia com problema não pode derrubar o app.
        }
    }

    /// <summary>
    /// O app soltou uma tecla que estava presa: o dono é avisado e o
    /// diário registra o que aconteceu.
    /// </summary>
    private void AoTeclaPresaCorrigida(TeclaPresa tecla)
    {
        Toast("Tecla presa corrigida", TeclaPresaRegras.TextoDoAviso(tecla.Teclas, tecla.HaQuanto), "✓");
        try
        {
            // O mesmo número do aviso (arredondado), e não o do diário:
            // o mesmo evento não pode sair com dois números.
            long segundos = TeclaPresaRegras.SegundosDoAviso(tecla.HaQuanto);
            RegistroDiario.Registrar(
                $"{TeclaPresaRegras.TextoDaCorrecao(tecla.Teclas)} "
                + $"(pressionada há pelo menos {NumeroTexto.Inteiro(segundos)} s).",
                "TECLA");
        }
        catch
        {
            // Diário é acessório: falhar nele não pode derrubar o app.
        }
    }

    /// <summary>
    /// O app tentou soltar a tecla e ela continuou presa: o dono é avisado
    /// (o app não pode dizer que soltou o que não soltou).
    /// </summary>
    private void AoTeclaPresaNaoSoltou(TeclaPresa tecla)
    {
        Toast("Tecla presa", TeclaPresaRegras.TextoDoAvisoSemSucesso(tecla.Teclas), "⚠");
        try
        {
            RegistroDiario.Registrar(TeclaPresaRegras.TextoDaTentativa(tecla.Teclas), "TECLA");
        }
        catch
        {
            // Diário é acessório: falhar nele não pode derrubar o app.
        }
    }

    /// <summary>
    /// O dono ligou ou desligou a correção automática da tecla presa.
    /// </summary>
    private void ChkTeclaPresa_Changed(object sender, RoutedEventArgs e)
    {
        try
        {
            bool novo = ChkTeclaPresa.IsChecked == true;
            // Sem mudança não grava: a tela reescrevia o arquivo de
            // configuração a cada abertura sem necessidade.
            if (_settings.CorrigirTeclaPresa == novo) return;
            _settings.CorrigirTeclaPresa = novo;
            _settings.Save();

            // A preferência não é só um desenho na tela: ela instala (ou
            // desinstala) a vigia de verdade.
            AplicarPreferenciaDaVigia();
        }
        catch
        {
            // Gravar a preferência é conveniência: falhar aqui não
            // impede a vigia de funcionar nesta sessão.
        }
    }
}
