using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using LibreHardwareMonitor.Hardware;

namespace ResetExplode;

/// <summary>
/// Integração com o visor do cooler Kalkan SmartMonitor (linha Alpha Rev.1).
///
/// O RESET EXPLODE fala direto com o visor por USB HID — sem o software da
/// Kalkan e sem a DLL dele. O app faz o handshake, embaralha os quadros do
/// firmware 1.2 e manda a leitura dos sensores do PC a cada segundo, como o
/// programa original faz.
///
/// Quem manda é a aba RESET SAÚDE: ela já é o lugar onde o PC mostra como
/// está a temperatura, então o visor passa a mostrar o mesmo número.
/// </summary>
public partial class MainWindow
{
    private KalkanDisplay? _kalkan;
    private DispatcherTimer? _kalkanTimer;
    private bool _kalkanOcupado;

    private void InitKalkan()
    {
        if (_kalkanTimer != null) return;
        _kalkanTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _kalkanTimer.Tick += async (_, _) => await EnviarKalkanAsync(false);
        KalkanStatus.Text = "Visor Kalkan não conectado.";
        KalkanDetalhe.Text = "Procure o visor e ligue a atualização automática para ele mostrar a temperatura do CPU.";
    }

    /// <summary>Fecha a sessão com o visor quando a janela fecha.</summary>
    private void EncerrarKalkan()
    {
        _kalkanTimer?.Stop();
        _kalkan?.Dispose();
        _kalkan = null;
    }

    private async void BtnKalkanConectar_Click(object sender, RoutedEventArgs e)
    {
        if (_kalkanOcupado) return;
        _kalkanOcupado = true;
        BtnKalkanConectar.IsEnabled = false;
        KalkanStatus.Text = "Procurando o visor Kalkan…";

        try
        {
            bool ok = await Task.Run(() => ConectarKalkan());
            if (ok)
            {
                KalkanStatus.Text = "Visor conectado · " + _kalkan!.DetalheConexao + ".";
                KalkanDetalhe.Text = "Clique em ATUALIZAR O VISOR para ele mostrar a temperatura do CPU em tempo real.";
                if (ChkKalkanAuto.IsChecked != true) ChkKalkanAuto.IsChecked = true;
            }
            else
            {
                KalkanStatus.Text = "Não conectou: " + (_kalkan?.Erro ?? "visor não encontrado");
            }
        }
        catch (Exception ex)
        {
            KalkanStatus.Text = "Falha ao conectar: " + ex.Message;
        }
        finally
        {
            _kalkanOcupado = false;
            BtnKalkanConectar.IsEnabled = true;
        }
    }

    private async void BtnKalkanEnviar_Click(object sender, RoutedEventArgs e)
    {
        if (_kalkanOcupado) return;
        _kalkanOcupado = true;
        BtnKalkanEnviar.IsEnabled = false;
        try
        {
            await EnviarKalkanAsync(true);
        }
        finally
        {
            _kalkanOcupado = false;
            BtnKalkanEnviar.IsEnabled = true;
        }
    }

    private void ChkKalkanAuto_Click(object sender, RoutedEventArgs e)
    {
        if (ChkKalkanAuto.IsChecked == true)
        {
            _kalkanTimer?.Start();
            if (_kalkan == null || !_kalkan.Conectado)
                _ = Task.Run(() => ConectarKalkan());
        }
        else
        {
            _kalkanTimer?.Stop();
            KalkanStatus.Text = "Atualização automática desligada.";
        }
    }

    /// <summary>Abre o device e faz o handshake (pode demorar 2 s: fora da thread da interface).</summary>
    private bool ConectarKalkan()
    {
        _kalkan ??= new KalkanDisplay();
        return _kalkan.Conectar();
    }

    /// <summary>
    /// Manda um quadro ao visor com a leitura atual do PC.
    /// <paramref name="mostrar"/> força a atualização do texto de status (usado no botão).
    /// </summary>
    private async Task EnviarKalkanAsync(bool mostrar)
    {
        if (_kalkan == null || !_kalkan.Conectado)
        {
            bool ok = await Task.Run(() => ConectarKalkan());
            if (!ok)
            {
                KalkanStatus.Text = "Não conectou: " + (_kalkan?.Erro ?? "visor não encontrado");
                if (_kalkanTimer != null && _kalkanTimer.IsEnabled && mostrar == false)
                {
                    // Não insista: o visor não está plugado.
                    _kalkanTimer.Stop();
                    ChkKalkanAuto.IsChecked = false;
                }
                return;
            }
        }

        var (temp, fan, carga) = LerSensoresParaVisor();
        bool enviado = await Task.Run(() => _kalkan!.Enviar(temp, fan, carga));

        if (!enviado)
        {
            KalkanStatus.Text = "Falha ao enviar ao visor: " + _kalkan!.Erro;
            return;
        }

        KalkanStatus.Text = $"Visor atualizado · {temp} °C · {fan} RPM · {carga} % de carga.";
        KalkanDetalhe.Text = $"{_kalkan!.Colecao} · semente 0x{_kalkan.Semente:X2}"
            + $" · {_kalkan.QuadrosEnviados} quadros enviados · {_kalkan.LeiturasRecebidas} respostas do visor";
    }

    /// <summary>
    /// Valores que vão para o visor: temperatura do CPU, rotação da ventoinha e
    /// carga da CPU. Sem sensor disponível, vai zero.
    /// </summary>
    private (byte Temp, ushort Fan, byte Carga) LerSensoresParaVisor()
    {
        float? t = _monitor.CpuTemperature;
        float? c = _monitor.CpuLoad;
        byte temp = t.HasValue ? (byte)Math.Clamp(Math.Round(t.Value), 0, 255) : (byte)0;
        byte carga = c.HasValue ? (byte)Math.Clamp(Math.Round(c.Value), 0, 100) : (byte)0;
        ushort fan = LerVentoinha();
        return (temp, fan, carga);
    }

    /// <summary>Rotação da ventoinha em RPM (a da CPU, se houver; senão a maior do PC).</summary>
    private ushort LerVentoinha()
    {
        var ventoinhas = _monitor.Rows
            .Where(r => r.Type == SensorType.Fan && r.RawValue is > 0)
            .ToList();
        if (ventoinhas.Count == 0) return 0;

        var cpu = ventoinhas.FirstOrDefault(r => r.HardwareType == HardwareType.Cpu);
        float rpm = cpu?.RawValue ?? ventoinhas.Max(r => r.RawValue ?? 0);
        return (ushort)Math.Clamp(Math.Round(rpm), 0, ushort.MaxValue);
    }
}
