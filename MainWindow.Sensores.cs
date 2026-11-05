using System.Diagnostics;
using System.Windows;

namespace ResetExplode;

/// <summary>
/// Guarda dos sensores: decide quanto do LibreHardwareMonitor pode ser usado
/// depois de um crash nativo e avisa o usuário na lateral.
///
/// O crash que motiva isto acontece dentro do driver da AMD (access violation
/// em <c>Marshal.PtrToStructure</c>, lendo ponteiro devolvido pela ADL) e não
/// dá para pegar em C# — o app morre. A defesa é não chamar o caminho quebrado:
/// o <see cref="SensorGuard"/> olha o log de eventos (onde o Windows registra a
/// morte do app) e desliga a GPU se ela foi a culpada.
/// </summary>
public partial class MainWindow
{
    private NivelSensor _nivelSensor = NivelSensor.Completo;
    private int _crashesSensor;
    private string _mensagemSensor = "";

    /// <summary>
    /// Lê os crashes do app no log de eventos e decide o nível de segurança.
    /// Roda fora da thread da janela (a leitura do log leva alguns cientos de ms).
    /// </summary>
    private (NivelSensor Nivel, int Crashes, string Mensagem) DecidirNivelSensor()
    {
        string nome = Process.GetCurrentProcess().ProcessName;
        var crashes = SensorCrashes.Ler(nome);
        DateTime agora = DateTime.Now;
        var nivel = SensorGuard.Nivel(crashes, agora, _settings.SensoresGpuReabilitadoEm, !_settings.SensoresGpuAdl);
        int n = SensorGuard.ContarCrashes(crashes, agora, _settings.SensoresGpuReabilitadoEm);
        return (nivel, n, SensorGuard.Mensagem(nivel, n));
    }

    /// <summary>Aplica a decisão na coleta e mostra o aviso na lateral.</summary>
    private void AplicarNivelSensor(NivelSensor nivel, int crashes, string mensagem)
    {
        _nivelSensor = nivel;
        _crashesSensor = crashes;
        _mensagemSensor = mensagem;

        // A GPU fica de fora da coleta quando ela foi a culpada: é o caminho
        // nativo do driver da AMD que derruba o programa.
        _monitor.PermitirGpuAdl = SensorGuard.UsarGpuAdl(nivel);

        UpdateSensorWarning();
    }

    /// <summary>Mostra ou esconde o aviso de sensores na lateral.</summary>
    private void UpdateSensorWarning()
    {
        if (SensorWarn is null || SensorWarnText is null) return;

        bool mostrar = _nivelSensor != NivelSensor.Completo && _mensagemSensor.Length > 0;
        SensorWarn.Visibility = mostrar ? Visibility.Visible : Visibility.Collapsed;
        if (mostrar) SensorWarnText.Text = _mensagemSensor;

        // O botão devolve os sensores ao normal (depois de trocar o driver, por exemplo).
        if (BtnReabilitarSensores is not null)
            BtnReabilitarSensores.Visibility = mostrar ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// Reabilita os sensores à mão (depois de trocar o driver, por exemplo).
    /// Zera a contagem de crashes para o app não desligar tudo de novo.
    /// </summary>
    private async void BtnReabilitarSensores_Click(object sender, RoutedEventArgs e)
    {
        _settings.SensoresGpuReabilitadoEm = DateTime.Now;
        _settings.Save();

        try
        {
            await _monitor.ReiniciarAsync(permitirGpuAdl: true);
            AplicarNivelSensor(NivelSensor.Completo, 0, "");
        }
        catch (Exception ex)
        {
            MessageBox.Show("Não foi possível religar os sensores: " + ex.Message, "RESET EXPLODE",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
