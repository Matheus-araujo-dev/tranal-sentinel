using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace TranalSentinel;

/// <summary>
/// O TRANAL SENTINEL como assistente: fica na bandeja, tira retrato do PC de
/// tempos em tempos e conta o que mudou.
/// </summary>
/// <remarks>
/// <para>
/// Esta é a parte que faz o app "acompanhar o PC" sem ficar na frente do
/// usuário: o ícone de bandeja é a casa dele, o <c>--iniciar-como-sistema</c>
/// é o que o traz junto com o Windows, e a linha do tempo é o que transforma
/// horas de coleta em uma frase útil ("o driver da GPU mudou na terça").
/// </para>
/// <para>
/// Tudo aqui é opcional por natureza: se a bandeja não puder ser criada, se o
/// snapshot falhar, o app continua funcionando como antes.
/// </para>
/// </remarks>
public partial class MainWindow
{
    private Bandeja? _bandeja;
    private DispatcherTimer? _timerInstantaneo;
    private DateTime _ultimoInstantaneo = DateTime.MinValue;

    /// <summary>Mudanças detectadas no PC, da mais recente para a mais antiga.</summary>
    public ObservableCollection<Mudanca> ListaMudancas { get; } = new();

    /// <summary>Texto curto do estado atual (usado na lateral e na bandeja).</summary>
    public string EstadoAssistente { get; private set; } = "TRANAL SENTINEL";

    /// <summary>
    /// Liga o modo assistente: bandeja, perfil de recursos e retrato periódico.
    /// </summary>
    public void IniciarAssistente()
    {
        AplicarPerfilRecursos();

        // Fechar a janela não fecha o app: ele continua na bandeja. Só o "Sair"
        // do menu do ícone encerra de verdade.
        Closing += (_, e) =>
        {
            if (_sair) return;
            e.Cancel = true;
            FecharParaBandeja();
        };

        try
        {
            _bandeja = new Bandeja(this, "TRANAL SENTINEL — acompanhando o PC");
            _bandeja.AoAbrir += () => Dispatcher.Invoke(MostrarJanela);
            _bandeja.AoSair += () => Dispatcher.Invoke(SairDeVerdade);
        }
        catch
        {
            // Sem bandeja o app segue funcionando pela janela.
            _bandeja = null;
        }

        // Aberto junto com o Windows: entra escondido e só aparece na bandeja.
        if (App.IniciarComoSistema)
        {
            Hide();
            ShowInTaskbar = false;
        }

        RegistroDiario.Registrar(
            $"App aberto (perfil {_settings.Perfil}, versão {VersaoAtual}).", "APP");

        // A versão na lateral vem do assembly: não fica desatualizada sozinha.
        if (SideVersion != null) SideVersion.Text = "v" + VersaoAtual;

        // Uma checagem por sessão: se houver versão nova, o banner aparece.
        IniciarVerificacaoDeAtualizacao();

        _timerInstantaneo = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMinutes(5),
        };
        _timerInstantaneo.Tick += (_, _) => AgendarInstantaneo();
        _timerInstantaneo.Start();

        // O primeiro retrato espera um pouco: logo depois de abrir o app, o
        // Windows ainda está terminando de instalar as coisas do boot.
        _ = Task.Run(async () =>
        {
            await Task.Delay(20_000).ConfigureAwait(false);
            await TirarInstantaneoAsync().ConfigureAwait(false);
        });
    }

    /// <summary>Aplica o perfil de recursos: define o relógio da coleta e o que roda sozinho.</summary>
    public void AplicarPerfilRecursos()
    {
        var perfil = _settings.Perfil;

        // A leitura do hardware é o que pesa: é ela que o perfil afasta.
        _monitor.IntervaloColetaMs = PoliticaRecursos.IntervaloSensoresMs(perfil);

        // O relógio da interface acompanha: em economia não faz sentido
        // repintar a tela 60 vezes por segundo sem dado novo.
        _timer.Interval = TimeSpan.FromMilliseconds(Math.Max(1000, PoliticaRecursos.IntervaloSensoresMs(perfil)));

        if (_timerInstantaneo != null)
            _timerInstantaneo.Interval = TimeSpan.FromMinutes(5);
    }

    /// <summary>Mostra a janela e traz para o primeiro plano.</summary>
    public void MostrarJanela()
    {
        ShowInTaskbar = true;
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
        Topmost = true;
        Topmost = false;
    }

    /// <summary>Fecha a janela mas continua na bandeja.</summary>
    public void FecharParaBandeja()
    {
        Hide();
        ShowInTaskbar = false;
        var aviso = new ToastWindow();
        aviso.Notify("TRANAL SENTINEL", "Continua acompanhando o PC na bandeja. Use o ícone ao lado do relógio para sair.", "\uE7BA");
    }

    /// <summary>Sai de verdade (fecha a janela e o processo).</summary>
    public void SairDeVerdade()
    {
        _sair = true;
        RegistroDiario.Registrar("App encerrado pelo usuário.", "APP");
        Close();
        Application.Current.Shutdown();
    }

    private bool _sair;

    /// <summary>
    /// Tira o retrato do PC, compara com o anterior e guarda a diferença.
    /// </summary>
    public async Task TirarInstantaneoAsync()
    {
        try
        {
            int intervaloMin = PoliticaRecursos.IntervaloInstantaneoMinutos(_settings.Perfil);
            if (DateTime.Now - _ultimoInstantaneo < TimeSpan.FromMinutes(intervaloMin)) return;
            _ultimoInstantaneo = DateTime.Now;

            var instantaneo = await Task.Run(ColetaInstantaneo.Criar).ConfigureAwait(false);

            var historico = HistoricoMudancas.Ler();
            var antes = historico.Count > 0 ? historico[^1] : null;
            var mudancas = Mudancas.Comparar(antes, instantaneo);

            historico.Add(instantaneo);
            HistoricoMudancas.Gravar(historico, PoliticaRecursos.HistoricoInstantaneos(_settings.Perfil));

            if (mudancas.Count > 0)
            {
                foreach (var m in mudancas)
                    RegistroDiario.Registrar(m.Resumo, "MUDANCA");
                await Dispatcher.InvokeAsync(() => MostrarMudancas(mudancas));
                Notificar(mudancas);
            }
        }
        catch
        {
            // Falha ao fotografar o PC não pode atrapalhar o app.
        }
    }

    /// <summary>Coloca as mudanças na lista da interface (mais recentes primeiro).</summary>
    public void MostrarMudancas(System.Collections.Generic.IEnumerable<Mudanca> mudancas)
    {
        ListaMudancas.Clear();
        foreach (var m in mudancas.OrderByDescending(m => m.Quando))
            ListaMudancas.Add(m);
    }

    /// <summary>Avisa o usuário sobre as mudanças relevantes.</summary>
    private void Notificar(System.Collections.Generic.IEnumerable<Mudanca> mudancas)
    {
        var lista = mudancas.ToList();
        var importante = lista.FirstOrDefault(m => m.Categoria is CategoriaMudanca.Driver or CategoriaMudanca.Bios
                                                   or CategoriaMudanca.Seguranca);
        if (importante == null) return;

        try
        {
            var aviso = new ToastWindow();
            aviso.Notify("O PC mudou", importante.Resumo, "\uE946");
        }
        catch { }
    }

    /// <summary>Agenda o próximo retrato: roda na hora que o perfil manda.</summary>
    private void AgendarInstantaneo()
    {
        int intervaloMin = PoliticaRecursos.IntervaloInstantaneoMinutos(_settings.Perfil);
        if (DateTime.Now - _ultimoInstantaneo < TimeSpan.FromMinutes(intervaloMin)) return;
        _ = TirarInstantaneoAsync();
    }

    /// <summary>Versão do app, para o log.</summary>
    private string VersaoAtual =>
        System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";

    // ---------------------------------------------------------------- aba do assistente

    /// <summary>Prepara a aba do assistente quando ela é aberta.</summary>
    public void InitTimeline()
    {
        if (TimelineItens != null && TimelineItens.ItemsSource == null)
        {
            TimelineItens.ItemsSource = ListaMudancas;
            ListaMudancas.CollectionChanged += (_, _) => AtualizarEstadoTimeline();
        }

        AtualizarEstadoTimeline();
        AtualizarBotoesPerfil();
        CarregarDiario();
    }

    private void AtualizarEstadoTimeline()
    {
        if (TimelineVazio == null) return;
        TimelineVazio.Visibility = ListaMudancas.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Tira um retrato do PC na hora, sem esperar o horário do perfil.</summary>
    private async void BtnRetrato_Click(object sender, RoutedEventArgs e)
    {
        if (BtnRetrato == null) return;
        BtnRetrato.IsEnabled = false;
        BtnRetrato.Content = "RETIRANDO…";
        try
        {
            _ultimoInstantaneo = DateTime.MinValue;   // força o retrato mesmo com o intervalo em cima
            await TirarInstantaneoAsync();
            RegistroDiario.Registrar("Retrato do PC tirado a pedido do usuário.", "ASSISTENTE");
        }
        finally
        {
            if (BtnRetrato != null)
            {
                BtnRetrato.IsEnabled = true;
                BtnRetrato.Content = "TIRAR RETRATO AGORA";
            }
            CarregarDiario();
        }
    }

    /// <summary>Troca o perfil de recursos.</summary>
    private void BtnPerfil_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement elemento) return;
        if (!int.TryParse(elemento.Tag?.ToString(), out int valor)) return;

        _settings.Perfil = (PerfilRecursos)valor;
        _settings.Save();
        AplicarPerfilRecursos();
        AtualizarBotoesPerfil();
        CarregarDiario();
        RegistroDiario.Registrar($"Perfil de recursos alterado para {PoliticaRecursos.Descricao(_settings.Perfil).Split('.')[0]}.", "ASSISTENTE");
    }

    private void AtualizarBotoesPerfil()
    {
        if (PerfilDescricao != null) PerfilDescricao.Text = PoliticaRecursos.Descricao(_settings.Perfil);

        MarcarPerfil(BtnPerfilCompleto, _settings.Perfil == PerfilRecursos.Completo);
        MarcarPerfil(BtnPerfilEquilibrado, _settings.Perfil == PerfilRecursos.Equilibrado);
        MarcarPerfil(BtnPerfilEconomia, _settings.Perfil == PerfilRecursos.Economia);
    }

    private static void MarcarPerfil(Button? botao, bool ativo)
    {
        if (botao == null) return;
        botao.Opacity = ativo ? 1.0 : 0.55;
        botao.FontWeight = ativo ? FontWeights.SemiBold : FontWeights.Normal;
    }

    /// <summary>Mostra o registro do dia (o que o app fez).</summary>
    private void CarregarDiario()
    {
        if (DiarioTexto == null) return;
        string texto = RegistroDiario.Ler(DateTime.Now).Trim();
        DiarioTexto.Text = texto.Length > 0 ? texto : "Nada registrado ainda hoje.";
    }
}
