using System.Windows;

namespace TranalSentinel;

/// <summary>
/// Janela de suspensão de serviços de programas em segundo plano.
///
/// <para>
/// O app não suspende nada por conta própria: mostra a lista, explica o que
/// cada serviço é, e o usuário escolhe. O que foi mudado fica guardado para
/// desfazer.
/// </para>
/// </summary>
public partial class JanelaServicos : Window
{
    private readonly AppSettings _settings;
    private readonly List<ServicoSuspendivel> _servicos = new();

    public JanelaServicos(AppSettings settings, IEnumerable<ServicoInfo> servicosColetados)
    {
        InitializeComponent();
        _settings = settings;

        _servicos.AddRange(ServicosSuspendiveis.Listar(servicosColetados));
        ListaServicos.ItemsSource = _servicos;

        var suspensos = ServicosSuspender.Suspensos(_settings);
        BtnRestaurar.Visibility = suspensos.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        Status.Text = _servicos.Count == 0
            ? "Nenhum serviço de terceiro para suspender (só os do Windows, os de segurança e os de suporte foram encontrados)."
            : $"{_servicos.Count} serviço(s) de terceiros encontrado(s). {_servicos.Count(s => s.Ruido)} são atualizador ou telemetria (marcados).";
    }

    private void BtnSuspender_Click(object sender, RoutedEventArgs e)
    {
        if (!Elevado())
        {
            MessageBox.Show(this,
                "Suspender serviço precisa de administrador. Abra o TRANAL SENTINEL como administrador.",
                "TRANAL SENTINEL", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var escolhidos = _servicos.Where(s => s.Selecionado).ToList();
        if (escolhidos.Count == 0)
        {
            Status.Text = "Nada marcado — escolha pelo menos um serviço.";
            return;
        }

        var resposta = MessageBox.Show(
            this,
            $"Suspender {escolhidos.Count} serviço(s)?\n\n" +
            string.Join("\n", escolhidos.Take(12).Select(s => "• " + s.Programa + " (" + s.Nome + ")")) +
            (escolhidos.Count > 12 ? $"\n… e mais {escolhidos.Count - 12}" : "") +
            "\n\nEles param de rodar e voltam a iniciar quando você quiser (pelo botão RESTAURAR TUDO).",
            "TRANAL SENTINEL",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning);

        if (resposta != MessageBoxResult.OK) return;

        try
        {
            int total = ServicosSuspender.Suspender(_settings, escolhidos);
            _settings.Save();

            foreach (var s in _servicos) s.Selecionado = false;
            ListaServicos.ItemsSource = null;
            ListaServicos.ItemsSource = _servicos;

            BtnRestaurar.Visibility = Visibility.Visible;
            Status.Text = total > 0
                ? $"{total} serviço(s) suspenso(s). Dá para restaurar por aqui."
                : "Nenhum serviço pôde ser suspenso (protegido ou sem permissão).";
        }
        catch (Exception ex)
        {
            Status.Text = "Falhou ao suspender: " + ex.Message;
        }
    }

    private void BtnRestaurar_Click(object sender, RoutedEventArgs e)
    {
        if (!Elevado())
        {
            MessageBox.Show(this,
                "Restaurar serviço precisa de administrador. Abra o TRANAL SENTINEL como administrador.",
                "TRANAL SENTINEL", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var resposta = MessageBox.Show(
            this,
            "Restaurar todos os serviços que o TRANAL SENTINEL suspendeu?",
            "TRANAL SENTINEL",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Question);

        if (resposta != MessageBoxResult.OK) return;

        try
        {
            int total = ServicosSuspender.Restaurar(_settings);
            _settings.Save();
            BtnRestaurar.Visibility = Visibility.Collapsed;
            Status.Text = total > 0
                ? $"{total} serviço(s) restaurado(s)."
                : "Não havia serviço suspenso para restaurar.";
        }
        catch (Exception ex)
        {
            Status.Text = "Falhou ao restaurar: " + ex.Message;
        }
    }

    private void BtnFechar_Click(object sender, RoutedEventArgs e) => Close();

    /// <summary>O app está rodando como administrador?</summary>
    private static bool Elevado()
    {
        try
        {
            using var id = System.Security.Principal.WindowsIdentity.GetCurrent();
            return new System.Security.Principal.WindowsPrincipal(id)
                .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }
}
