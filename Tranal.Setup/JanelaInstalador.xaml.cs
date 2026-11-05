using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using TranalSetup.Dominio;
using TranalSetup.Infra;
using TranalSetup.Servicos;

namespace TranalSetup;

/// <summary>
/// Janela de instalação do TRANAL SENTINEL.
///
/// Ela só mostra o que vai acontecer e chama o serviço de instalação: nada de
/// regra de instalação mora aqui. O destino pode ser alterado pelo usuário
/// (campo editável e botão para escolher outra pasta). Ao terminar, mostra o
/// resultado (sucesso ou erro) e fecha.
/// </summary>
public partial class JanelaInstalador : Window
{
    private readonly ContextoSistema _contexto = ContextoSistema.DoSistema();
    private readonly IRegistro _registro = new RegistroWindows();
    private readonly IAtalho _atalho = new AtalhoWindows();
    private readonly ITarefaAgendada _tarefa = new TarefaAgendadaWindows();
    private readonly string _pastaPayload;
    private readonly LogInstalador _log;

    private bool _payloadCompleto = true;
    private string? _erroDestino;

    /// <summary>Cria a janela e mostra o destino e o payload detectados.</summary>
    public JanelaInstalador()
    {
        InitializeComponent();

        _pastaPayload = _contexto.PastaInstalador;
        _log = new LogInstalador(CaminhosInstalacao.ArquivoLog(_contexto));

        TxtDestino.Text = DestinoInstalacao.Padrao(_contexto).Pasta;
        TxtPayload.Text = _pastaPayload;
        TxtLog.Text = $"Log: {CaminhosInstalacao.ArquivoLog(_contexto)}";

        AvisarPayloadIncompleto();
        ValidarDestino();
    }

    /// <summary>
    /// Se o payload não estiver completo, avisa na tela antes de o usuário
    /// clicar em INSTALAR (e desliga o botão, porque a instalação não passaria).
    /// </summary>
    private void AvisarPayloadIncompleto()
    {
        var plano = new ConstrutorPlano(_registro, _tarefa).MontarPlanoInstalacao(
            _contexto,
            new OpcoesInstalacao { Destino = DestinoInstalacao.Padrao(_contexto) },
            _pastaPayload);

        _payloadCompleto = plano.PayloadCompleto;

        if (_payloadCompleto)
        {
            AtualizarEstado();
            return;
        }

        TxtAviso.Text = "Payload incompleto: falta " + string.Join(", ", plano.ArquivosAusentes)
            + $" em {_pastaPayload}. O instalador precisa dos dois arquivos juntos do TRANAL SENTINEL.exe.";
        TxtAviso.Visibility = Visibility.Visible;
        AtualizarEstado();
    }

    /// <summary>
    /// Confere o caminho digitado e mostra o que houver: erro no caminho ou
    /// aviso de que já existe uma instalação naquela pasta.
    /// </summary>
    private void ValidarDestino()
    {
        _erroDestino = DestinoInstalacao.Erro(TxtDestino.Text);

        if (_erroDestino is null)
        {
            TxtDestinoErro.Visibility = Visibility.Collapsed;
        }
        else
        {
            TxtDestinoErro.Text = _erroDestino;
            TxtDestinoErro.Visibility = Visibility.Visible;
        }

        AvisarInstalacaoExistente();
        AtualizarEstado();
    }

    /// <summary>
    /// Avisa o que vai acontecer com o que já está instalado: na pasta
    /// escolhida (vai ser substituído) e em outra pasta (vai ser movido).
    /// </summary>
    private void AvisarInstalacaoExistente()
    {
        if (_erroDestino is not null)
        {
            TxtDestinoAviso.Visibility = Visibility.Collapsed;
            return;
        }

        string pasta = DestinoInstalacao.DaEscolha(TxtDestino.Text).Pasta;
        var avisos = new List<string>();

        if (File.Exists(CaminhosInstalacao.ExecutavelInstalado(pasta)))
        {
            avisos.Add("Já existe uma instalação nesta pasta: ela será substituída "
                + "(o executável atual é salvo como .bak antes de ser trocado).");
        }

        DestinoInstalacao? anterior = new ConstrutorPlano(_registro, _tarefa).DestinoRegistrado();
        if (anterior is not null
            && !string.Equals(anterior.Pasta, pasta, StringComparison.OrdinalIgnoreCase)
            && File.Exists(CaminhosInstalacao.ExecutavelInstalado(anterior.Pasta)))
        {
            avisos.Add($"Existe outra instalação em {anterior.Pasta}. "
                + "Instalar aqui é mudar de pasta: os arquivos de lá serão removidos "
                + "depois que a nova instalação estiver pronta.");
        }

        if (avisos.Count == 0)
        {
            TxtDestinoAviso.Visibility = Visibility.Collapsed;
            return;
        }

        TxtDestinoAviso.Text = string.Join("  ", avisos);
        TxtDestinoAviso.Visibility = Visibility.Visible;
    }

    /// <summary>O botão INSTALAR só fica habilitado quando não há erro nenhum.</summary>
    private void AtualizarEstado() => BtnInstalar.IsEnabled = _payloadCompleto && _erroDestino is null;

    /// <summary>Abri o seletor de pasta do Windows e coloca o resultado no campo.</summary>
    private void BtnEscolherPasta_Click(object sender, RoutedEventArgs e)
    {
        var dialogo = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Escolher a pasta de instalação",
            Multiselect = false,
            InitialDirectory = PastaInicialDoDialogo(),
        };

        if (dialogo.ShowDialog(this) != true) return;

        TxtDestino.Text = DestinoInstalacao.PastaDentroDe(dialogo.FolderName);
        TxtDestino.Focus();
    }

    /// <summary>
    /// Pasta em que o seletor abre: o destino atual (ou o pai dele, quando a
    /// pasta ainda não existe).
    /// </summary>
    private string PastaInicialDoDialogo()
    {
        string atual = TxtDestino.Text.Trim();

        if (!string.IsNullOrEmpty(atual))
        {
            if (Directory.Exists(atual)) return atual;

            string? pai = Path.GetDirectoryName(atual);
            if (!string.IsNullOrEmpty(pai) && Directory.Exists(pai)) return pai;
        }

        return _contexto.PastaProgramFiles;
    }

    private void TxtDestino_TextChanged(object sender, TextChangedEventArgs e) => ValidarDestino();

    private async void BtnInstalar_Click(object sender, RoutedEventArgs e)
    {
        string? erro = DestinoInstalacao.Erro(TxtDestino.Text);
        if (erro is not null)
        {
            _erroDestino = erro;
            TxtDestinoErro.Text = erro;
            TxtDestinoErro.Visibility = Visibility.Visible;
            AtualizarEstado();
            return;
        }

        BtnInstalar.IsEnabled = false;
        BtnCancelar.IsEnabled = false;
        TxtResultado.Text = "Instalando...";
        TxtResultado.Foreground = new System.Windows.Media.SolidColorBrush(
            System.Windows.Media.Color.FromRgb(0x39, 0x41, 0x4E));
        TxtResultado.Visibility = Visibility.Visible;

        var opcoes = new OpcoesInstalacao
        {
            Destino = DestinoInstalacao.DaEscolha(TxtDestino.Text),
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
