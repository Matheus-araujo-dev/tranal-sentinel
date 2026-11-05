using System.Windows;
using System.Windows.Threading;
using TranalSetup.Dominio;
using TranalSetup.Infra;
using TranalSetup.Servicos;

namespace TranalSetup;

/// <summary>
/// Janela de instalação do TRANAL SENTINEL.
///
/// Ela só mostra o que vai acontecer e chama o serviço de instalação: nada de
/// regra de instalação mora aqui. Ao terminar, mostra o resultado (sucesso ou
/// erro) e fecha.
/// </summary>
public partial class JanelaInstalador : Window
{
    private readonly ContextoSistema _contexto = ContextoSistema.DoSistema();
    private readonly IRegistro _registro = new RegistroWindows();
    private readonly IAtalho _atalho = new AtalhoWindows();
    private readonly ITarefaAgendada _tarefa = new TarefaAgendadaWindows();
    private readonly string _pastaPayload;
    private readonly LogInstalador _log;

    /// <summary>Cria a janela e mostra o destino e o payload detectado.</summary>
    public JanelaInstalador()
    {
        InitializeComponent();

        _pastaPayload = _contexto.PastaInstalador;
        _log = new LogInstalador(CaminhosInstalacao.ArquivoLog(_contexto));

        TxtDestino.Text = CaminhosInstalacao.PastaDestino(_contexto);
        TxtPayload.Text = _pastaPayload;
        TxtLog.Text = $"Log: {CaminhosInstalacao.ArquivoLog(_contexto)}";

        AvisarPayloadIncompleto();
    }

    /// <summary>
    /// Se o payload não estiver completo, avisa na tela antes de o usuário
    /// clicar em INSTALAR (e desliga o botão, porque a instalação não passaria).
    /// </summary>
    private void AvisarPayloadIncompleto()
    {
        var plano = new ConstrutorPlano(_registro, _tarefa)
            .MontarPlanoInstalacao(_contexto, new OpcoesInstalacao(), _pastaPayload);

        if (plano.PayloadCompleto) return;

        TxtAviso.Text = "Payload incompleto: falta " + string.Join(", ", plano.ArquivosAusentes)
            + $" em {_pastaPayload}. O instalador precisa dos dois arquivos juntos do TRANAL SENTINEL.exe.";
        TxtAviso.Visibility = Visibility.Visible;
        BtnInstalar.IsEnabled = false;
    }

    private async void BtnInstalar_Click(object sender, RoutedEventArgs e)
    {
        BtnInstalar.IsEnabled = false;
        BtnCancelar.IsEnabled = false;
        TxtResultado.Text = "Instalando...";
        TxtResultado.Foreground = new System.Windows.Media.SolidColorBrush(
            System.Windows.Media.Color.FromRgb(0x39, 0x41, 0x4E));
        TxtResultado.Visibility = Visibility.Visible;

        var opcoes = new OpcoesInstalacao
        {
            IniciarComWindows = ChkIniciarWindows.IsChecked == true,
            AtalhoAreaTrabalho = ChkAreaTrabalho.IsChecked == true,
            ReinstalarSobreInstalacaoExistente = ChkJaInstalar.IsChecked == true,
        };

        var servico = new ServicoInstalacao(_contexto, _registro, _atalho, _tarefa, _log);
        ResultadoOperacao resultado = await Task.Run(() => servico.Instalar(opcoes, _pastaPayload));

        MostrarResultado(resultado);
    }

    private void BtnCancelar_Click(object sender, RoutedEventArgs e)
    {
        _log.Registrar("Instalação cancelada pelo usuário.");
        Application.Current.Shutdown(0);
    }

    /// <summary>
    /// Mostra o resultado da instalação. No sucesso a janela fecha sozinha em
    /// alguns segundos; na falha, fica tempo suficiente para o usuário ler a
    /// mensagem antes de ela fechar.
    /// </summary>
    private void MostrarResultado(ResultadoOperacao resultado)
    {
        TxtResultado.Text = resultado.Sucesso
            ? $"{resultado.Mensagem}\n\nO TRANAL SENTINEL já pode ser aberto pelo menu Iniciar."
            : $"{resultado.Mensagem}\n\nNada foi alterado no sistema.";
        TxtResultado.Foreground = new System.Windows.Media.SolidColorBrush(
            resultado.Sucesso
                ? System.Windows.Media.Color.FromRgb(0x1B, 0x6E, 0x3C)
                : System.Windows.Media.Color.FromRgb(0xA8, 0x2E, 0x2E));

        BtnInstalar.Content = "FECHAR";
        BtnInstalar.IsEnabled = true;
        BtnInstalar.Click -= BtnInstalar_Click;
        BtnInstalar.Click += (_, _) => Application.Current.Shutdown(resultado.Sucesso ? 0 : 2);
        BtnCancelar.IsEnabled = false;

        var timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(resultado.Sucesso ? 4 : 10),
        };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            Application.Current.Shutdown(resultado.Sucesso ? 0 : 2);
        };
        timer.Start();
    }
}
