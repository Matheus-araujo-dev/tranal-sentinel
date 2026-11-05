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
    }

    /// <summary>Para a vigia (chamada no fechamento da janela).</summary>
    private void PararVigia()
    {
        try { _timerTravados?.Dispose(); } catch { }
        try { _timerAlertas?.Dispose(); } catch { }
        _timerTravados = null;
        _timerAlertas = null;
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
            float? cpu = _monitor.CpuTemperature;
            float? disco = MonitorAlertas.TemperaturaMaximaDisco(_monitor.Rows);

            // Bateria: valem todas as baterias do PC (um notebook pode ter
            // mais de uma). O alerta usa a menor carga.
            int? bateria = null;
            bool carregando = false;
            try
            {
                foreach (var cartao in BatteryInfo.Read(_monitor.Rows.ToList()))
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

            _alertas.Verificar(new MonitorAlertas.Estado(cpu, disco, bateria, carregando));
        }
        catch
        {
            // Alerta que falha não pode derrubar o app.
        }
    }

    private void AoAlertar(Alerta alerta)
    {
        Toast(alerta.Titulo, alerta.Texto, "⚠");
        try { RegistroDiario.Registrar($"Alerta: {alerta.Titulo} — {alerta.Texto}", "ALERTA"); }
        catch { }
    }
}
