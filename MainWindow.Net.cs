using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace TranalSentinel;

/// <summary>Páginas Energia (bateria e fonte) e Rede (diagnóstico de lag e tráfego).</summary>
public partial class MainWindow
{
    // ================= Energia =================

    private bool _energyBusy;

    private async void RefreshEnergy()
    {
        if (_energyBusy) return;
        _energyBusy = true;
        BtnEnergyRefresh.IsEnabled = false;
        EnergyStatus.Text = "Lendo bateria e energia…";
        try
        {
            var sensors = _monitor.Rows.ToList();
            var cards = await Task.Run(() => BatteryInfo.Read(sensors));
            EnergyCards.ItemsSource = cards;
            bool hasBattery = cards.Count > 1;
            EnergyStatus.Text = (hasBattery ? "Bateria detectada" : "Nenhuma bateria (PC de mesa)") + $" · lido às {DateTime.Now:HH:mm:ss}";
        }
        catch (Exception ex) { EnergyStatus.Text = "Falhou: " + ex.Message; }
        finally
        {
            _energyBusy = false;
            BtnEnergyRefresh.IsEnabled = true;
        }
    }

    private void BtnEnergyRefresh_Click(object sender, RoutedEventArgs e) => RefreshEnergy();

    // ================= Rede =================

    private CancellationTokenSource? _netCts;
    private bool _netRunning;
    private List<DiskHealthCard>? _lastNet;

    private async void BtnNetTest_Click(object sender, RoutedEventArgs e)
    {
        if (_netRunning) { _netCts?.Cancel(); return; }
        if (AnyBenchRunning || TestRunning) { NetStatus.Text = "Termine o outro teste antes."; return; }

        _netRunning = true;
        _netCts = new CancellationTokenSource();
        var ct = _netCts.Token;
        BtnNetTest.Content = "CANCELAR";
        NetCards.ItemsSource = null;
        NetProgress.ScaleX = 0;
        try
        {
            var cards = await NetworkTest.Run((text, frac) => Dispatcher.BeginInvoke(new Action(() =>
            {
                NetStatus.Text = text;
                NetProgress.ScaleX = Math.Clamp(frac, 0, 1);
            })), ct);
            _lastNet = cards;
            NetCards.ItemsSource = cards;
            NetStatus.Text = $"Concluído às {DateTime.Now:HH:mm:ss}. O resultado entra no relatório exportado.";
            NetProgress.ScaleX = 1;
        }
        catch (OperationCanceledException) { NetStatus.Text = "Teste cancelado."; NetProgress.ScaleX = 0; }
        catch (Exception ex) { NetStatus.Text = "Falhou: " + ex.Message; NetProgress.ScaleX = 0; }
        finally
        {
            _netRunning = false;
            _netCts?.Dispose();
            _netCts = null;
            BtnNetTest.Content = "TESTAR";
        }
    }

    // ================= Tráfego de internet =================

    /// <summary>
    /// O coletor: mede o tráfego por programa enquanto o app roda.
    ///
    /// <para>
    /// Ele vive desde o começo do app (e não desde a abertura da aba Rede):
    /// o tráfego de um dia só pode ser medido o dia inteiro — medir só
    /// quando a aba está aberta daria um número que muda conforme a pessoa
    /// navega, e seria uma mentira sobre o uso do dia.
    /// </para>
    /// </summary>
    private readonly ColetorTrafego _trafego = new();

    /// <summary>
    /// O relógio que grava o total do dia no banco e refresca o cartão.
    /// De dez em dez segundos: o banco é o espelho do total conhecido, e
    /// gravar a cada segundo seria I/O à toa.
    /// </summary>
    private DispatcherTimer? _timerTrafego;

    /// <summary>
    /// O dia que a página está mostrando.
    ///
    /// <para>
    /// É o dia que a pessoa escolheu no seletor — e não tem nada a ver com
    /// o dia que o app está gravando: mostrar um dia passado não pode
    /// mexer na virada do dia (o app continua gravando o dia de hoje, e a
    /// virada continua acontecendo à meia-noite).
    /// </para>
    /// </summary>
    private DateTime _trafegoDiaMostrado = DateTime.Today;

    /// <summary>
    /// O dia que o app está gravando no banco. É o dia de hoje, e muda
    /// sozinho quando o dia vira — nunca por causa do seletor.
    /// </summary>
    private DateTime _trafegoDiaGravado = DateTime.Today;

    /// <summary>
    /// Trava da troca de dia: o seletor dispara o evento quando a página
    /// monta a lista, e isso não é a pessoa escolhendo outro dia.
    /// </summary>
    private bool _trafegoTrocandoDia;

    /// <summary>
    /// O app pode gravar o total do dia?
    ///
    /// <para>
    /// Só depois de ler a base do banco (o que já estava salvo do dia).
    /// Sem a base, gravar o total medido agora escreveria por cima do que
    /// já estava salvo — e o dia perderia as horas em que o app não estava
    /// rodando. Enquanto não dá para ler, o app espera.
    /// </para>
    /// </summary>
    private bool _trafegoGravavel;

    /// <summary>
    /// Os pacotes que já tinham sido descartados antes de o app subir (a
    /// base do banco).
    /// </summary>
    private long _trafegoPerdidosBase;

    /// <summary>
    /// Não deu para ler quantos pacotes já tinham sido descartados. O
    /// cartão avisa em vez de mostrar zero.
    /// </summary>
    private bool _trafegoPerdidosDesconhecidos;

    /// <summary>
    /// Sobe a coleta do tráfego e o relógio que grava o total do dia.
    /// </summary>
    private async void IniciarTrafego()
    {
        try
        {
            DateTime hoje = DateTime.Today;

            // A base: o que já estava salvo no banco para hoje. Sem ela,
            // cada vez que o app abrisse ele começaria do zero — e
            // gravaria por cima do total do dia, apagando o que já tinha
            // sido medido antes.
            bool leuBase = await Task.Run(() => LerBaseDoDia(hoje));

            _trafegoGravavel = leuBase;

            // A ETW é do kernel: se ela não subir, o motivo fica guardado
            // e a tela mostra. O app continua funcionando.
            _trafego.Iniciar();

            _timerTrafego = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
            _timerTrafego.Tick += (_, _) => TiqueDoTrafego();
            _timerTrafego.Start();
        }
        catch (Exception ex)
        {
            // A leitura do tráfego é um extra: se ela não subiu, o app
            // continua — e o cartão da aba Rede dirá que não deu.
            _trafegoGravavel = false;
            _trafego.AnotarErro("Não foi possível ligar a leitura do tráfego: " + ex.Message);
        }
    }

    /// <summary>
    /// Lê do banco o que já estava salvo do dia (a base da conta).
    /// </summary>
    /// <returns>Verdadeiro quando a base foi lida (mesmo que o dia não tenha nada).</returns>
    private bool LerBaseDoDia(DateTime dia)
    {
        var leitura = Banco.LerTrafego(dia);
        if (!leitura.Completa) return false;

        _trafego.Semear(dia, leitura.Linhas);

        long perdidos = Banco.LerTrafegoPerdidos(dia);
        _trafegoPerdidosDesconhecidos = perdidos < 0;
        _trafegoPerdidosBase = perdidos < 0 ? 0 : perdidos;

        return true;
    }

    /// <summary>
    /// Prepara a aba "Rede" (chamada quando o menu "Rede" é aberto).
    /// </summary>
    private void InitRede()
    {
        if (TrafegoDia == null) return;

        CarregarDiasTrafego();
        _ = AtualizarTrafegoAsync();
    }

    /// <summary>
    /// Monta a lista de dias do seletor. Hoje fica sempre na lista — é o dia
    /// que a pessoa quer ver primeiro.
    /// </summary>
    private void CarregarDiasTrafego()
    {
        DateTime hoje = DateTime.Today;

        List<DateTime> dias;
        try
        {
            dias = Banco.DiasComTrafego();
        }
        catch (Exception)
        {
            // A lista de dias é um extra: se o banco não responder, o dia de
            // hoje continua disponível (e a tela diz que não deu para ler).
            dias = new List<DateTime>();
        }

        var opcoes = dias
            .Select(d => new OpcaoDia(d.Date, DiaTexto.RotuloMaiusculo(d.Date, hoje)))
            .ToList();

        if (!opcoes.Any(o => o.Data == hoje))
            opcoes.Insert(0, new OpcaoDia(hoje, DiaTexto.RotuloMaiusculo(hoje, hoje)));

        // O dia mostrado: o que a pessoa escolheu (se ainda existir), senão
        // o de hoje.
        DateTime escolhido = opcoes.Any(o => o.Data == _trafegoDiaMostrado) ? _trafegoDiaMostrado : hoje;

        _trafegoTrocandoDia = true;
        try
        {
            TrafegoDia.ItemsSource = opcoes;
            TrafegoDia.SelectedItem = opcoes.FirstOrDefault(o => o.Data == escolhido);
        }
        finally
        {
            _trafegoTrocandoDia = false;
        }

        _trafegoDiaMostrado = escolhido;
    }

    /// <summary>
    /// Troca o dia mostrado (o seletor de dia).
    /// </summary>
    private void TrafegoDia_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_trafegoTrocandoDia) return;
        if (TrafegoDia.SelectedItem is not OpcaoDia opcao) return;

        _trafegoDiaMostrado = opcao.Data;
        _ = AtualizarTrafegoAsync();
    }

    /// <summary>Botão "ATUALIZAR": recarrega o dia mostrado.</summary>
    private async void BtnTrafegoAtualizar_Click(object sender, RoutedEventArgs e) =>
        await AtualizarTrafegoAsync();

    /// <summary>
    /// Recarrega o cartão do dia. A leitura é I/O de banco — vai para fora
    /// da thread da tela.
    /// </summary>
    private async Task AtualizarTrafegoAsync()
    {
        DateTime dia = _trafegoDiaMostrado;

        if (BtnTrafegoAtualizar != null) BtnTrafegoAtualizar.IsEnabled = false;

        TrafegoRelatorio relatorio;
        try
        {
            relatorio = await Task.Run(() => MontarRelatorioTrafego(dia));
        }
        catch (Exception)
        {
            // Falha ao ler o tráfego não pode derrubar a aba: ela fica
            // dizendo que não deu para ler — que é a verdade.
            relatorio = TrafegoRegras.Relatorio(
                new List<TrafegoPrograma>(), dia, DateTime.Today, DateTime.Now, false, null, -1);
        }
        finally
        {
            if (BtnTrafegoAtualizar != null) BtnTrafegoAtualizar.IsEnabled = true;
        }

        // Uma leitura antiga (a pessoa trocou o dia no meio da leitura) não
        // pode pintar a tela com o dia errado.
        if (dia != _trafegoDiaMostrado) return;

        MostrarTrafego(relatorio);
    }

    /// <summary>
    /// Monta o relatório do dia: de onde vem o número, e o que ele diz.
    /// </summary>
    private TrafegoRelatorio MontarRelatorioTrafego(DateTime dia)
    {
        DateTime hoje = DateTime.Today;
        DateTime agora = DateTime.Now;

        if (dia.Date == hoje.Date)
        {
            // Hoje: o número vem do coletor — e o coletor já soma a base
            // (o que estava salvo no banco quando o app subiu).
            var numeros = _trafego.Acumulado(dia);

            long perdidos = _trafegoPerdidosDesconhecidos
                ? -1
                : _trafegoPerdidosBase + _trafego.EventosPerdidosDoDia(dia);

            string? aviso = null;
            if (!_trafegoGravavel)
            {
                aviso = "O histórico do dia não pôde ser lido quando o app abriu: " +
                    "o total acima é só o que foi medido depois que ele subiu.";
            }

            return TrafegoRegras.Relatorio(
                numeros, dia, hoje, agora, true, _trafego.Erro, ParaInt(perdidos), aviso);
        }

        // Um dia passado: o número vem do banco, e só do banco.
        var leitura = Banco.LerTrafego(dia);
        long perdidosDia = Banco.LerTrafegoPerdidos(dia);

        return TrafegoRegras.Relatorio(
            leitura.Linhas, dia, hoje, agora, leitura.Completa, null, ParaInt(perdidosDia));
    }

    /// <summary>
    /// O tique do relógio do tráfego.
    ///
    /// <para>
    /// A gravação do total é um extra: uma falha nela (o banco fechado, o
    /// disco cheio, um programa que morreu no meio da conta) não pode
    /// derrubar o app. Sem esta volta, a exceção subia até o despachante do
    /// Windows e matava o TRANAL SENTINEL — o que aconteceu na prova de
    /// 09/11/2026 (o nome de um processo que já tinha morrido chegou nulo
    /// no ranking, e o dicionário não aceita chave nula).
    /// </para>
    /// </summary>
    private void TiqueDoTrafego()
    {
        try
        {
            GravarTrafego();
        }
        catch (Exception)
        {
            // O app continua: o total do dia é gravado de novo no tique
            // seguinte, e o cartão mostra o que deu para ler.
        }
    }

    /// <summary>
    /// Grava o total do dia no banco e atualiza o cartão.
    /// </summary>
    private void GravarTrafego()
    {
        DateTime hoje = DateTime.Today;

        // A virada do dia: o dia que acabou recebe o total final, e o dia
        // novo começa do zero. Sem isto, o tráfego da madrugada entraria na
        // conta do dia anterior — e o dia novo nasceria com o total do
        // velho.
        //
        // O dia gravado é o dia de hoje, e não o dia que a pessoa está
        // olhando: escolher "ontem" no seletor não pode mexer na virada.
        if (hoje != _trafegoDiaGravado)
        {
            GravarDiaDoTrafego(_trafegoDiaGravado);
            _trafegoDiaGravado = hoje;
            _trafegoPerdidosBase = 0;
            _trafegoPerdidosDesconhecidos = false;
            CarregarDiasTrafego();
        }

        GravarDiaDoTrafego(hoje);

        if (PageNet != null && PageNet.IsVisible) _ = AtualizarTrafegoAsync();
    }

    /// <summary>
    /// Grava o total de um dia no banco.
    /// </summary>
    private void GravarDiaDoTrafego(DateTime dia)
    {
        if (!_trafegoGravavel)
        {
            // Sem a base do banco, gravar o total medido agora apagaria o
            // que já estava salvo do dia. O app espera a base aparecer
            // (ela some quando o banco não pôde ser lido).
            TentarBaseDeNovo();
            return;
        }

        var numeros = _trafego.Acumulado(dia);
        if (numeros.Count > 0)
            Banco.GravarTrafego(dia, numeros);

        if (!_trafegoPerdidosDesconhecidos)
            Banco.GravarTrafegoPerdidos(dia, _trafegoPerdidosBase + _trafego.EventosPerdidosDoDia(dia));
    }

    /// <summary>
    /// Grava o total do dia quando o app está fechando.
    ///
    /// <para>
    /// O relógio grava a cada dez segundos, e o app pode fechar entre um
    /// tique e outro: o tráfego desses segundos seria perdido sem aviso.
    /// Esta gravação é a última — e ela não pode impedir o fechamento.
    /// </para>
    /// </summary>
    private void GravarTrafegoFinal()
    {
        try
        {
            if (!_trafegoGravavel) return;

            DateTime hoje = DateTime.Today;

            if (hoje != _trafegoDiaGravado)
            {
                GravarDiaDoTrafego(_trafegoDiaGravado);
                _trafegoDiaGravado = hoje;
            }

            GravarDiaDoTrafego(hoje);
        }
        catch (Exception)
        {
            // O app está fechando: a falha na última gravação não pode
            // impedir o fechamento (e o cartão já dizia o que deu errado).
        }
    }

    /// <summary>
    /// Tenta de novo ler a base do dia (a que faltou quando o app subiu).
    /// </summary>
    private void TentarBaseDeNovo()
    {
        if (LerBaseDoDia(_trafegoDiaGravado)) _trafegoGravavel = true;
    }

    /// <summary>
    /// Converte a contagem de pacotes para o número que o cartão mostra
    /// (-1 continua sendo "não deu para contar").
    /// </summary>
    private static int ParaInt(long valor) =>
        valor < 0 ? -1 : (int)Math.Min(valor, int.MaxValue);

    /// <summary>
    /// Coloca o relatório na tela.
    /// </summary>
    private void MostrarTrafego(TrafegoRelatorio relatorio)
    {
        try
        {
            if (TrafegoCards == null) return;

            TrafegoCards.ItemsSource = new List<TrafegoCartao> { relatorio.Cartao };

            var linhas = TrafegoRegras.Linhas(relatorio.Ranking);
            TrafegoRanking.ItemsSource = linhas;
            TrafegoRanking.Visibility = linhas.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
            TrafegoVazio.Visibility = linhas.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (Exception)
        {
            // A aba do tráfego é uma leitura: se a montagem falhar, a página
            // fica como está. Derrubar o app por causa de um texto seria
            // absurdo.
        }
    }

    // ================= Tema (escuro / claro) =================

    private void Theme_Click(object sender, RoutedEventArgs e)
    {
        ThemeManager.Apply(!ThemeManager.IsLight);
        _settings.LightTheme = ThemeManager.IsLight;
        _settings.Save();
        UpdateThemeButton();
    }

    /// <summary>Mostra o ícone do modo para onde o clique leva (sol = ir para o claro, lua = ir para o escuro) e troca o logo.</summary>
    private void UpdateThemeButton()
    {
        bool light = ThemeManager.IsLight;
        BtnTheme.Content = light ? "\uE708" : "\uE706";
        BtnTheme.ToolTip = light ? "Modo escuro" : "Modo claro";
        TitleLogo.Source = new System.Windows.Media.Imaging.BitmapImage(
            new Uri(light ? "pack://application:,,,/Assets/logo-dark.png" : "pack://application:,,,/Assets/logo.png"));
    }
}
