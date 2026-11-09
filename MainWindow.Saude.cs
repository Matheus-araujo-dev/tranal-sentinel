using System.Globalization;
using System.Windows;
using System.Windows.Controls;

namespace TranalSentinel;

/// <summary>
/// Aba TRANAL SAÚDE: diagnóstico de travamentos e lentidão.
///
/// Lê o histórico do Windows (log de eventos, WMI, registro, serviços) e mostra
/// o que está causando problema, com a ação sugerida para cada achado.
///
/// <para>
/// A análise é somente leitura. Alterar o sistema é exceção: cada ação que
/// mexe em alguma coisa pede confirmação, fica registrada no diário e pode
/// ser desfeita.
/// </para>
/// </summary>
public partial class MainWindow
{
    private bool _saudeInit, _saudeBusy;
    private RelatorioSaude? _relatorioSaudeAtual;

    private void InitSaude()
    {
        if (_saudeInit) return;
        _saudeInit = true;

        SaudeAchados.ItemsSource = null;
        SaudeTravamentos.ItemsSource = null;
        SaudeResumo.ItemsSource = null;
        SaudeProgress.ScaleX = 0;
        SaudeStatus.Text = "Clique em ANALISAR para varrer o histórico do sistema. Nada é alterado sem você confirmar.";

        // O estado do banco é lido fora da thread da tela: a verificação de
        // integridade percorre o arquivo inteiro do histórico.
        _ = MostrarBancoAsync();
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

        // Os botões de restaurar só aparecem quando o app mexeu em algo
        // (ASPM ou serviços). O de restaurar serviço funciona mesmo sem a
        // varredura ter dado certo — o desfazer não pode depender dela.
        bool mexeuAspm = AcaoExecutor.TemAspmAlterado(_settings);
        bool mexeuServicos = ServicosSuspender.Suspensos(_settings).Count > 0;

        SaudeAcoesPendentes.Visibility = (mexeuAspm || mexeuServicos) ? Visibility.Visible : Visibility.Collapsed;
        BtnSaudeRestaurarAspm.Visibility = mexeuAspm ? Visibility.Visible : Visibility.Collapsed;
        BtnSaudeRestaurarServicos.Visibility = mexeuServicos ? Visibility.Visible : Visibility.Collapsed;
        SaudePendentesTexto.Text = mexeuAspm && mexeuServicos
            ? "O TRANAL SENTINEL alterou o ASPM e suspendeu serviços neste PC. Dá para desfazer."
            : mexeuAspm
                ? "O TRANAL SENTINEL alterou o gerenciamento de energia do PCIe (ASPM) neste PC."
                : "O TRANAL SENTINEL suspendeu serviços neste PC. Dá para restaurar.";
    }

    private void MostrarAchados(RelatorioSaude relatorio)
    {
        _relatorioSaudeAtual = relatorio;
        SaudeAchados.ItemsSource = relatorio.Achados;
        SaudeAchadosVazio.Visibility = relatorio.Achados.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        MostrarNota(relatorio);
    }

    /// <summary>
    /// Mostra a nota de saúde do PC (0–100) e a tendência em relação aos
    /// dias anteriores. A nota do dia fica guardada no histórico.
    /// </summary>
    private void MostrarNota(RelatorioSaude relatorio)
    {
        try
        {
            var nota = SaudeNota.Calcular(relatorio.Achados);
            SaudeNotaValor.Text = nota.Nota.ToString();
            SaudeNotaResumo.Text = $"{nota.Conceito} · {nota.Resumo}";

            SaudeNotaHistorico.GravarHoje(nota.Nota);
            SaudeNotaTendencia.Text = SaudeNotaHistorico.TendenciaDe(nota.Nota);
        }
        catch
        {
            // A nota é um extra: se o histórico não estiver gravável, o
            // diagnóstico continua funcionando.
            SaudeNotaResumo.Text = "Nota indisponível neste momento.";
        }
    }

    private void MostrarTravamentos(RelatorioSaude relatorio)
    {
        SaudeTravamentos.ItemsSource = relatorio.Travamentos;
        SaudeTravamentosVazio.Visibility = relatorio.Travamentos.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    // ---------------- ações de um clique ----------------

    /// <summary>
    /// Executa a ação de um achado. Só chega aqui depois da confirmação: o
    /// botão da interface avisa o que a ação faz antes de mexer em alguma
    /// coisa.
    /// </summary>
    private void BtnSaudeAcao_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button botao) return;
        if (botao.Tag is not AcaoAplicavel acao) return;

        // Suspender serviços tem lista própria (o usuário escolhe um a um).
        if (acao.Id == AcoesSaude.IdSuspenderServicos)
        {
            AbrirJanelaServicos();
            return;
        }

        if (acao.MudaSistema)
        {
            var resposta = MessageBox.Show(
                this,
                acao.Explicacao + "\n\nQuer aplicar agora?",
                "TRANAL SENTINEL",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Warning);

            if (resposta != MessageBoxResult.OK) return;
        }

        var executor = new AcaoExecutor(new AcaoWindows(), _settings, IrParaAba);
        var resultado = executor.Executar(acao);

        MessageBox.Show(this,
            resultado.Mensagem,
            "TRANAL SENTINEL",
            MessageBoxButton.OK,
            resultado.Sucesso ? MessageBoxImage.Information : MessageBoxImage.Warning);

        try
        {
            RegistroDiario.Registrar($"saude: acao {acao.Id} — {(resultado.Sucesso ? "ok" : "falhou")}: {resultado.Mensagem}");
        }
        catch
        {
            // O diário é um extra: se falhar, a ação já foi aplicada e isso
            // não pode virar mais um erro na tela.
        }
    }

    /// <summary>Botão de restaurar o ASPM (aparece quando o app mexeu nele).</summary>
    private void BtnSaudeRestaurarAspm_Click(object sender, RoutedEventArgs e)
    {
        var executor = new AcaoExecutor(new AcaoWindows(), _settings, IrParaAba);
        var resultado = executor.Executar(AcoesSaude.RestaurarAspm());

        MessageBox.Show(this, resultado.Mensagem, "TRANAL SENTINEL",
            MessageBoxButton.OK,
            resultado.Sucesso ? MessageBoxImage.Information : MessageBoxImage.Warning);

        if (resultado.Sucesso) MostrarResumo(_relatorioSaudeAtual ?? new RelatorioSaude());
    }

    /// <summary>Botão de restaurar os serviços suspensos (abre a janela).</summary>
    private void BtnSaudeRestaurarServicos_Click(object sender, RoutedEventArgs e)
    {
        AbrirJanelaServicos();
        MostrarResumo(_relatorioSaudeAtual ?? new RelatorioSaude());
    }

    /// <summary>Abre a janela de suspensão de serviços.</summary>
    private void AbrirJanelaServicos()
    {
        var servicos = _relatorioSaudeAtual?.Dados.ServicosAuto ?? new List<ServicoInfo>();
        var suspensos = ServicosSuspender.Suspensos(_settings);

        // Sem análise e sem serviço suspenso, não há o que mostrar: a lista
        // vem da varredura. Mas havendo serviço suspenso, a janela abre
        // mesmo assim — o botão de restaurar não pode depender de a varredura
        // ter dado certo.
        if (servicos.Count == 0 && suspensos.Count == 0)
        {
            MessageBox.Show(this,
                "Rode a análise (ANALISAR) primeiro: a lista de serviços vem da varredura do sistema.",
                "TRANAL SENTINEL", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var janela = new JanelaServicos(_settings, servicos) { Owner = this };
        janela.ShowDialog();
    }

    /// <summary>
    /// Mostra o estado do banco de dados do histórico: se está íntegro,
    /// quanto ocupa e quantas linhas tem.
    /// </summary>
    private async Task MostrarBancoAsync()
    {
        try
        {
            var dados = await Task.Run(() => Banco.Ver());

            SaudeBancoEstado.Text = dados.Estado == "ok"
                ? "O banco de dados do histórico está íntegro."
                : dados.Estado;

            // O aviso do banco aparece na tela: o cartão não pode dizer
            // "está íntegro" e esconder que a migração do histórico
            // antigo ficou pela metade. O estado e o aviso são coisas
            // diferentes — e os dois aparecem.
            if (string.IsNullOrWhiteSpace(dados.Aviso))
            {
                SaudeBancoAvisoCaixa.Visibility = Visibility.Collapsed;
                SaudeBancoAviso.Text = "";
            }
            else
            {
                SaudeBancoAvisoCaixa.Visibility = Visibility.Visible;
                SaudeBancoAviso.Text = "Atenção: " + dados.Aviso;
            }

            SaudeBancoResumo.ItemsSource = new List<InfoNode>
            {
                new InfoNode { Header = "Tamanho do histórico", Value = TamanhoLegivel(dados.TamanhoBytes) },
                new InfoNode { Header = "Amostras de sensor", Value = ContagemLegivel(dados.Amostras) },
                new InfoNode { Header = "Registros de uso", Value = ContagemLegivel(dados.Uso) },
                new InfoNode { Header = "Arquivo", Value = dados.Caminho }
            };

            BtnSaudeBancoRestaurar.Visibility = dados.CopiaAvariada != null
                ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            SaudeBancoEstado.Text = "Não foi possível ler o banco de dados: " + ex.Message;
        }
    }

    /// <summary>
    /// A contagem de linhas, ou "não deu para contar" quando a consulta
    /// falhou (o banco estragado). Número errado é pior que número
    /// ausente: "0 amostras" num banco com milhares seria mentira.
    /// </summary>
    private static string ContagemLegivel(long linhas) =>
        linhas < 0 ? "não deu para contar" : linhas.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// O tamanho em kilobytes ou megabytes (sem locale: a tela do Windows
    /// é em português, mas o número não pode depender do Windows).
    /// </summary>
    private static string TamanhoLegivel(long bytes)
    {
        if (bytes < 1024) return bytes.ToString(CultureInfo.InvariantCulture) + " B";
        if (bytes < 1024 * 1024) return (bytes / 1024.0).ToString("0.#", CultureInfo.InvariantCulture) + " KB";
        return (bytes / (1024.0 * 1024.0)).ToString("0.#", CultureInfo.InvariantCulture) + " MB";
    }

    /// <summary>Botão de verificar: confere a integridade do banco de novo.</summary>
    private async void BtnSaudeBancoVerificar_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await MostrarBancoAsync();
        }
        catch (Exception ex)
        {
            // Handler assíncrono: uma exceção aqui derrubaria o app inteiro.
            SaudeBancoEstado.Text = "Não foi possível verificar o banco de dados: " + ex.Message;
        }
    }

    /// <summary>
    /// Botão de reparar: reconstrói o banco com o que der para ler.
    ///
    /// <para>
    /// Muda o arquivo do histórico, então pede confirmação e fica
    /// registrado no diário. O banco velho não é apagado: ele fica guardado
    /// como cópia datada, e é o que o botão "restaurar cópia" devolve.
    /// </para>
    /// </summary>
    private async void BtnSaudeBancoReparar_Click(object sender, RoutedEventArgs e)
    {
        var resposta = MessageBox.Show(
            this,
            "O reparo reconstrói o banco de dados do histórico.\n\n" +
            "O banco atual é guardado como cópia datada na mesma pasta, e o que estiver legível é copiado para um banco novo. " +
            "O que não der para ler se perde — a cópia antiga continua no disco.\n\nQuer reparar agora?",
            "TRANAL SENTINEL",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning);

        if (resposta != MessageBoxResult.OK) return;

        BtnSaudeBancoReparar.IsEnabled = false;
        try
        {
            var resultado = await Task.Run(Banco.Reparar);

            MessageBox.Show(this,
                resultado.Relato,
                "TRANAL SENTINEL",
                MessageBoxButton.OK,
                resultado.Sucesso ? MessageBoxImage.Information : MessageBoxImage.Warning);

            try
            {
                RegistroDiario.Registrar($"banco: reparo — {(resultado.Sucesso ? "ok" : "falhou")}: {resultado.Relato}");
            }
            catch
            {
                // O diário é um extra: se falhar, o reparo já foi aplicado e
                // isso não pode virar mais um erro na tela.
            }
        }
        catch (Exception ex)
        {
            // Handler assíncrono: uma exceção aqui derrubaria o app inteiro.
            MessageBox.Show(this,
                "Não foi possível reparar o banco de dados: " + ex.Message,
                "TRANAL SENTINEL", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            BtnSaudeBancoReparar.IsEnabled = true;
            await MostrarBancoAsync();
        }
    }

    /// <summary>
    /// Botão de restaurar a cópia: desfaz o reparo, devolvendo o banco
    /// antigo (com o problema que ele tinha).
    /// </summary>
    private async void BtnSaudeBancoRestaurar_Click(object sender, RoutedEventArgs e)
    {
        var resposta = MessageBox.Show(
            this,
            "A cópia avariada mais recente volta para o lugar, e o banco atual fica guardado como cópia datada.\n\n" +
            "Atenção: o banco volta com o mesmo problema que tinha antes do reparo.\n\nQuer restaurar?",
            "TRANAL SENTINEL",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning);

        if (resposta != MessageBoxResult.OK) return;

        BtnSaudeBancoRestaurar.IsEnabled = false;
        try
        {
            var resultado = await Task.Run(Banco.RestaurarCopia);

            MessageBox.Show(this,
                resultado.Relato,
                "TRANAL SENTINEL",
                MessageBoxButton.OK,
                resultado.Sucesso ? MessageBoxImage.Information : MessageBoxImage.Warning);

            try
            {
                RegistroDiario.Registrar($"banco: restauracao da copia — {(resultado.Sucesso ? "ok" : "falhou")}: {resultado.Relato}");
            }
            catch
            {
                // O diário é um extra: se falhar, a restauração já foi
                // aplicada e isso não pode virar mais um erro na tela.
            }
        }
        catch (Exception ex)
        {
            // Handler assíncrono: uma exceção aqui derrubaria o app inteiro.
            MessageBox.Show(this,
                "Não foi possível restaurar a cópia do banco de dados: " + ex.Message,
                "TRANAL SENTINEL", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            BtnSaudeBancoRestaurar.IsEnabled = true;
            await MostrarBancoAsync();
        }
    }

    /// <summary>Leva para uma aba (usado pelas ações de navegação).</summary>
    private void IrParaAba(string page) => ShowPage(page);
}
