using System;
using System.Collections.Generic;
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
/// O visor do Kalkan mostra duas coisas: a temperatura ("DEV TEMP") e a rotação
/// da ventoinha ("FAN SPEED"). No app oficial você escolhe de qual sensor vem
/// cada uma; aqui dá para escolher a mesma coisa, com a lista completa de
/// sensores que o RESET EXPLODE já coleta (CPU, núcleos, GPU, placa-mãe,
/// armazenamento, memória).
///
/// Fica na aba RESET INTEGRAÇÃO, junto com iCUE, OpenRGB e Chroma: são as
/// integrações do app com periféricos — e, ao contrário do diagnóstico, aqui o
/// app escreve no dispositivo (no visor), não só lê o sistema.
/// </summary>
public partial class MainWindow
{
    private KalkanDisplay? _kalkan;
    private DispatcherTimer? _kalkanTimer;
    private bool _kalkanOcupado;
    private string _kalkanListaTemp = "";
    private string _kalkanListaVent = "";

    private void InitKalkan()
    {
        if (_kalkanTimer != null) return;
        _kalkanTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _kalkanTimer.Tick += async (_, _) => await EnviarKalkanAsync(false);
        KalkanStatus.Text = "Visor Kalkan não conectado.";
        KalkanDetalhe.Text = "Procure o visor e ligue a atualização automática para ele mostrar a temperatura do CPU.";
        AtualizarFontesKalkan();

        // Se o usuário deixou o visor ligado da última vez, reconecta sozinho
        // (e manda a temperatura atualizada) ao abrir esta aba.
        if (_settings.KalkanAuto && ChkKalkanAuto.IsChecked != true)
            ChkKalkanAuto.IsChecked = true;
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
        bool ligado = ChkKalkanAuto.IsChecked == true;
        _settings.KalkanAuto = ligado;
        try { _settings.Save(); } catch { }

        if (ligado)
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

    private void KalkanFonteTemp_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (KalkanTemp.SelectedItem is SensorItem item)
        {
            _settings.KalkanTempSensor = item.Id;
            try { _settings.Save(); } catch { }
        }
    }

    private void KalkanFonteVent_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (KalkanVent.SelectedItem is SensorItem item)
        {
            _settings.KalkanFanSensor = item.Id;
            try { _settings.Save(); } catch { }
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
        AtualizarFontesKalkan();

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
            + $" · {_kalkan.QuadrosEnviados} quadros enviados · {_kalkan.LeiturasRecebidas} respostas do visor"
            + (_kalkan.UltimaLeituraHex.Length > 0 ? " · última: " + RecortarHex(_kalkan.UltimaLeituraHex) : "");
    }

    /// <summary>
    /// Preenche as listas de sensores (temperatura e ventoinha).
    /// A lista do LibreHardwareMonitor cresce com o tempo, então isso roda a cada
    /// ciclo — mas só mexe na interface quando a lista realmente mudou, para não
    /// apagar a escolha do usuário.
    /// </summary>
    private void AtualizarFontesKalkan()
    {
        var temperaturas = _monitor.Rows
            .Where(r => r.Type == SensorType.Temperature)
            .Select(r => new SensorItem { Id = r.Id, Display = $"{r.Hardware} · {r.Name}" })
            .OrderBy(i => i.Display)
            .ToList();

        var ventoinhas = _monitor.Rows
            .Where(r => r.Type == SensorType.Fan)
            .Select(r => new SensorItem { Id = r.Id, Display = $"{r.Hardware} · {r.Name}" })
            .OrderBy(i => i.Display)
            .ToList();

        string marcaTemp = string.Join('|', temperaturas.Select(i => i.Id));
        if (marcaTemp != _kalkanListaTemp)
        {
            _kalkanListaTemp = marcaTemp;
            string? atual = (KalkanTemp.SelectedItem as SensorItem)?.Id ?? _settings.KalkanTempSensor;
            KalkanTemp.ItemsSource = temperaturas;
            KalkanTemp.DisplayMemberPath = "Display";
            Selecionar(KalkanTemp, atual, TemperaturaPadrao(temperaturas));
        }

        string marcaVent = string.Join('|', ventoinhas.Select(i => i.Id));
        if (marcaVent != _kalkanListaVent)
        {
            _kalkanListaVent = marcaVent;
            string? atual = (KalkanVent.SelectedItem as SensorItem)?.Id ?? _settings.KalkanFanSensor;
            KalkanVent.ItemsSource = ventoinhas;
            KalkanVent.DisplayMemberPath = "Display";
            Selecionar(KalkanVent, atual, VentoinhaPadrao(ventoinhas));
        }
    }

    private static void Selecionar(ComboBox caixa, string? id, string? padrao)
    {
        var itens = caixa.ItemsSource as List<SensorItem>;
        if (itens == null || itens.Count == 0) return;
        var alvo = itens.FirstOrDefault(i => i.Id == id) ?? itens.FirstOrDefault(i => i.Id == padrao);
        caixa.SelectedItem = alvo;
    }

    /// <summary>Sensor de temperatura padrão: o pacote da CPU (ou Tctl/Tdie), como no app.</summary>
    private string? TemperaturaPadrao(List<SensorItem> itens)
    {
        var cpu = _monitor.Rows
            .Where(r => r.Type == SensorType.Temperature && r.HardwareType == HardwareType.Cpu)
            .ToList();
        SensorRow? principal = cpu.FirstOrDefault(r => r.Name.Contains("Package") || r.Name.Contains("Tctl") || r.Name.Contains("Tdie"))
                              ?? cpu.FirstOrDefault();
        return principal?.Id ?? itens.FirstOrDefault()?.Id;
    }

    /// <summary>Ventoinha padrão: a da CPU, senão a primeira da lista.</summary>
    private string? VentoinhaPadrao(List<SensorItem> itens)
    {
        SensorRow? cpu = _monitor.Rows
            .FirstOrDefault(r => r.Type == SensorType.Fan && r.HardwareType == HardwareType.Cpu);
        return cpu?.Id ?? itens.FirstOrDefault()?.Id;
    }

    /// <summary>
    /// Valores que vão para o visor: temperatura do sensor escolhido, rotação da
    /// ventoinha escolhida e carga da CPU. Sem sensor disponível, vai zero.
    /// </summary>
    private (byte Temp, ushort Fan, byte Carga) LerSensoresParaVisor()
    {
        float? t = ValorSensor(_settings.KalkanTempSensor, (KalkanTemp.SelectedItem as SensorItem)?.Id);
        float? c = _monitor.CpuLoad;
        byte temp = t.HasValue ? (byte)Math.Clamp(Math.Round(t.Value), 0, 255) : (byte)0;
        byte carga = c.HasValue ? (byte)Math.Clamp(Math.Round(c.Value), 0, 100) : (byte)0;
        ushort fan = LerVentoinha();
        return (temp, fan, carga);
    }

    /// <summary>Leitura do sensor escolhido (ou o padrão, se a escolha sumiu do hardware).</summary>
    private float? ValorSensor(string? idGuardado, string? idNaTela)
    {
        string? id = idNaTela ?? idGuardado;
        if (string.IsNullOrEmpty(id)) return null;
        var linha = _monitor.Rows.FirstOrDefault(r => r.Id == id);
        if (linha?.RawValue is > 0) return linha.RawValue;
        return null;
    }

    /// <summary>Rotação da ventoinha escolhida em RPM.</summary>
    private ushort LerVentoinha()
    {
        float? rpm = ValorSensor(_settings.KalkanFanSensor, (KalkanVent.SelectedItem as SensorItem)?.Id);
        if (rpm == null)
        {
            var ventoinhas = _monitor.Rows
                .Where(r => r.Type == SensorType.Fan && r.RawValue is > 0)
                .ToList();
            if (ventoinhas.Count == 0) return 0;
            var cpu = ventoinhas.FirstOrDefault(r => r.HardwareType == HardwareType.Cpu);
            rpm = cpu?.RawValue ?? ventoinhas.Max(r => r.RawValue ?? 0);
        }
        return (ushort)Math.Clamp(Math.Round(rpm.Value), 0, ushort.MaxValue);
    }

    /// <summary>Corta o hexadecimal da resposta do visor para caber no cartão.</summary>
    private static string RecortarHex(string hex)
    {
        if (hex.Length <= 20) return hex;
        return hex[..20] + "…";
    }
}

/// <summary>Item das listas de sensores do cartão do visor (guarda o id real do sensor).</summary>
public sealed class SensorItem
{
    public string Id { get; init; } = "";
    public string Display { get; init; } = "";
    public override string ToString() => Display;
}
