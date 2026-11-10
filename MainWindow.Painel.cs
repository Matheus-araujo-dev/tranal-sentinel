using System.Windows;
using System.Windows.Threading;

namespace TranalSentinel;

public partial class MainWindow
{
    /// <summary>O painel flutuante (null enquanto ele não foi ligado).</summary>
    private PainelWindow? _painel;

    /// <summary>O relógio que atualiza o painel.</summary>
    private DispatcherTimer? _painelTimer;

    /// <summary>
    /// Liga ou desliga o painel flutuante (o interruptor da página "Painel").
    /// </summary>
    private void ChkPainel_Changed(object sender, RoutedEventArgs e)
    {
        if (ChkPainel.IsChecked == true)
        {
            if (_painel is null)
            {
                // A janela nasce uma vez só, e os eventos se ligam uma vez
                // só: se o dono desligar e ligar o painel, o duplo clique não
                // pode acabar abrindo a janela principal duas vezes.
                _painel = new PainelWindow();
                _painel.AoFechar += () => ChkPainel.IsChecked = false;
                _painel.AoAbrirJanela += () => Dispatcher.Invoke(MostrarJanela);
            }

            _painel.Topmost = ChkPainelTopo.IsChecked == true;

            RestaurarPosicaoDoPainel();
            if (!_painel.IsVisible) _painel.Show();

            _painelTimer ??= new DispatcherTimer(
                TimeSpan.FromMilliseconds(500), DispatcherPriority.Normal, (_, _) => UpdatePainel(), Dispatcher);
            _painelTimer.Start();

            UpdatePainel();
        }
        else
        {
            _painelTimer?.Stop();
            SalvarPosicaoDoPainel();
            _painel?.Hide();
        }

        _settings.PainelVisivel = ChkPainel.IsChecked == true;
        _settings.Save();
    }

    /// <summary>Liga ou desliga o "sempre no topo" do painel.</summary>
    private void ChkPainelOpcao_Changed(object sender, RoutedEventArgs e)
    {
        if (_painel is not null) _painel.Topmost = ChkPainelTopo.IsChecked == true;
        _settings.PainelSempreNoTopo = ChkPainelTopo.IsChecked == true;
        _settings.Save();
    }

    /// <summary>Devolve o painel para o canto superior direito do monitor principal.</summary>
    private void PainelCanto_Click(object sender, RoutedEventArgs e)
    {
        if (_painel is null) return;

        var area = SystemParameters.WorkArea;
        _painel.Left = area.Right - _painel.Width - 12;
        _painel.Top = area.Top + 12;

        SalvarPosicaoDoPainel();
    }

    /// <summary>Atualiza as linhas do painel (a cada meio segundo).</summary>
    private void UpdatePainel()
    {
        if (_painel is null || !_painel.IsVisible) return;

        try
        {
            // A mesma leitura que o overlay usa: um número só no app.
            var s = BottleneckAnalyzer.Sample(_monitor, _gpus.FirstOrDefault(), null);

            var ventoinhas = new List<PainelVentoinha>();
            try
            {
                ventoinhas = _ventoinhas.Cartoes()
                    .Select(c => new PainelVentoinha(VentoinhaRegras.NomeLegivel(c.Nome), c.RpmAtual))
                    .ToList();
            }
            catch
            {
                // A leitura das ventoinhas é um extra: sem ela, o painel
                // mostra os outros blocos.
            }

            var retrato = new PainelRetrato(
                s.CpuTemp, s.CpuTotal, s.GpuTemp, s.GpuLoad, s.RamPct, ventoinhas);

            _painel.Update(PainelRegras.Linhas(retrato));
        }
        catch (Exception ex)
        {
            // Um painel que falha não pode derrubar o app: ele simplesmente some.
            _painelTimer?.Stop();
            _painel?.Hide();
            RegistroDiario.Registrar("Falha ao atualizar o painel flutuante: " + ex.Message, "PAINEL");
        }
    }

    /// <summary>Guarda a posição do painel (em DIPs).</summary>
    private void SalvarPosicaoDoPainel()
    {
        if (_painel is null || !_painel.IsVisible) return;

        _settings.PainelX = _painel.Left;
        _settings.PainelY = _painel.Top;
    }

    /// <summary>
    /// Devolve o painel para a posição guardada — ou para o canto, se a
    /// posição guardada não estiver em nenhum monitor (o dono trocou de
    /// monitor).
    /// </summary>
    private void RestaurarPosicaoDoPainel()
    {
        if (_painel is null) return;

        if (_settings.PainelX is double x && _settings.PainelY is double y
            && PainelWindow.DentroDaTela(x, y, 260, 120))
        {
            _painel.Left = x;
            _painel.Top = y;
            return;
        }

        var area = SystemParameters.WorkArea;
        _painel.Left = area.Right - 260 - 12;
        _painel.Top = area.Top + 12;
    }

    /// <summary>Prepara a página (chamada quando o menu "Painel" é aberto).</summary>
    private void InitPainel()
    {
        // O interruptor reflete o que está ligado agora.
        ChkPainel.IsChecked = _painel is not null && _painel.IsVisible;
        ChkPainelTopo.IsChecked = _settings.PainelSempreNoTopo;
    }
}
