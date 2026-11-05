using System.Windows;

namespace TranalSentinel;

/// <summary>
/// Aba TRANAL SAÚDE: diagnóstico de travamentos e lentidão.
///
/// Lê o histórico do Windows (log de eventos, WMI, registro, serviços) e mostra
/// o que está causando problema, com a ação sugerida para cada achado.
/// É somente leitura: a aba não altera nada no sistema.
/// </summary>
public partial class MainWindow
{
    private bool _saudeInit, _saudeBusy;

    private void InitSaude()
    {
        if (_saudeInit) return;
        _saudeInit = true;

        SaudeAchados.ItemsSource = null;
        SaudeTravamentos.ItemsSource = null;
        SaudeResumo.ItemsSource = null;
        SaudeProgress.ScaleX = 0;
        SaudeStatus.Text = "Clique em ANALISAR para varrer o histórico do sistema. Nada é alterado.";
    }

    private async void BtnSaudeScan_Click(object sender, RoutedEventArgs e)
    {
        if (_saudeBusy) return;

        _saudeBusy = true;
        BtnSaudeScan.IsEnabled = false;
        SaudeProgress.ScaleX = 0.15;
        SaudeStatus.Text = "Lendo log de eventos, WMI e registro…";

        try
        {
            // A coleta é lenta (log de eventos, WMI, serviços): fora da thread da interface.
            var relatorio = await Task.Run(SaudeDiagnostico.Executar);

            SaudeProgress.ScaleX = 0.9;
            MostrarResumo(relatorio);
            MostrarAchados(relatorio);
            MostrarTravamentos(relatorio);

            SaudeProgress.ScaleX = 1;
            SaudeStatus.Text = relatorio.Total == 0
                ? $"Análise concluída às {relatorio.GeradoEm:HH:mm:ss}. Nenhum problema encontrado."
                : $"Análise concluída às {relatorio.GeradoEm:HH:mm:ss}. {relatorio.Resumo}";
        }
        catch (Exception ex)
        {
            SaudeProgress.ScaleX = 0;
            SaudeStatus.Text = "Falhou na análise: " + ex.Message;
        }
        finally
        {
            _saudeBusy = false;
            BtnSaudeScan.IsEnabled = true;
        }
    }

    private void MostrarResumo(RelatorioSaude relatorio)
    {
        var dados = relatorio.Dados;
        var resumo = new List<InfoNode>();

        resumo.Add(new InfoNode { Header = "Travamentos (90 dias)", Value = relatorio.Travamentos.Count.ToString() });
        resumo.Add(new InfoNode { Header = "Erros de hardware (WHEA)", Value = dados.EventosWhea.ToString() });
        resumo.Add(new InfoNode { Header = "Telas azuis (minidump)", Value = dados.Minidumps.ToString() });
        resumo.Add(new InfoNode { Header = "Resets de driver de vídeo", Value = dados.ResetsDriverVideo.ToString() });

        resumo.Add(new InfoNode
        {
            Header = "Gerenciamento PCIe (ASPM)",
            Value = dados.AspmAc == null ? "desconhecido" : SaudeRegras.NomeAspm(dados.AspmAc.Value)
        });

        var antivirus = !string.IsNullOrEmpty(dados.AntivirusRegistrado)
            ? dados.AntivirusRegistrado!
            : (dados.DriverAntivirus ?? "nenhum");

        resumo.Add(new InfoNode
        {
            Header = "Antivírus",
            Value = $"{antivirus} · Defender: {dados.ModoDefender ?? "desconhecido"} · filtro de kernel: {dados.EstadoDriverAntivirus ?? "ausente"}"
        });

        if (dados.DefinicoesAntivirusDias != null)
            resumo.Add(new InfoNode
            {
                Header = "Definições de antivírus",
                Value = $"{dados.DefinicoesAntivirusDias} dias ({dados.DefinicoesAntivirusData})"
            });

        if (dados.AntivirusMemoriaMb > 0)
            resumo.Add(new InfoNode { Header = "Memória do antivírus", Value = $"{dados.AntivirusMemoriaMb} MB" });

        resumo.Add(new InfoNode
        {
            Header = "Compressão de memória",
            Value = $"{dados.MemoriaCompressaoMb} MB"
        });

        foreach (var modulo in dados.ModulosMemoria)
        {
            resumo.Add(new InfoNode
            {
                Header = $"RAM {modulo.Slot}",
                Value = $"{modulo.Gigabytes:0.#} GB @ {modulo.VelocidadeConfigMHz} MHz"
            });
        }

        resumo.Add(new InfoNode
        {
            Header = "Inicialização automática",
            Value = $"{dados.AutostartAtivos} de {dados.AutostartTotal} ativos"
        });

        resumo.Add(new InfoNode
        {
            Header = "Hyper-V / VBS",
            Value = dados.HypervisorAtivo ? "ativo" : "inativo"
        });

        if (relatorio.AppsNoBoot.Count > 0)
        {
            var top = relatorio.AppsNoBoot.OrderByDescending(a => a.Vezes).Take(3);
            resumo.Add(new InfoNode
            {
                Header = "Apps que falham junto dos travamentos",
                Value = string.Join(", ", top.Select(a => $"{a.Nome} ({a.Vezes}x)"))
            });
        }

        SaudeResumo.ItemsSource = resumo;
    }

    private void MostrarAchados(RelatorioSaude relatorio)
    {
        SaudeAchados.ItemsSource = relatorio.Achados;
        SaudeAchadosVazio.Visibility = relatorio.Achados.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void MostrarTravamentos(RelatorioSaude relatorio)
    {
        SaudeTravamentos.ItemsSource = relatorio.Travamentos;
        SaudeTravamentosVazio.Visibility = relatorio.Travamentos.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }
}
