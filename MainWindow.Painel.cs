using System.Windows;
using System.Windows.Threading;

namespace TranalSentinel;

/// <summary>Painel flutuante: liga e desliga a janela do painel, o relógio que a atualiza e a posição guardada.</summary>
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

            // O "sempre no topo" vem da configuração guardada, e não do
            // interruptor: no arranque (ou logo depois de ligar o painel)
            // a página ainda não foi aberta, e o interruptor está no estado
            // padrão do XAML — desligado. Assim, o painel voltaria sem
            // ficar acima das outras janelas.
            _painel.Topmost = _settings.PainelSempreNoTopo;

            if (!_painel.IsVisible) _painel.Show();

            // As linhas primeiro, a medição depois. O tamanho da janela vem
            // do conteúdo dela, e o painel começa vazio: medido antes das
            // linhas, o canto seria calculado com um painel sem nada
            // dentro, e a janela cresceria para a direita depois — por
            // cima da borda da tela, bem onde fica o botão de fechar.
            //
            // E se a primeira leitura falhar, o painel se desliga: nesse
            // caso não há o que posicionar, e o relógio não fica rodando
            // para um painel escondido.
            if (UpdatePainel())
            {
                _painel.UpdateLayout();
                RestaurarPosicaoDoPainel();

                _painelTimer ??= new DispatcherTimer(
                    TimeSpan.FromMilliseconds(500), DispatcherPriority.Normal, (_, _) => UpdatePainel(), Dispatcher);
                _painelTimer.Start();
            }
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
        var (largura, altura) = TamanhoDoPainel();
        _painel.Left = area.Right - largura - 12;
        _painel.Top = area.Top + 12;

        SalvarPosicaoDoPainel();
    }

    /// <summary>
    /// O tamanho do painel, em DIPs.
    ///
    /// <para>
    /// A janela tem <c>SizeToContent</c>, então o tamanho vem do conteúdo
    /// medido (<c>ActualWidth</c>), e não do <c>Width</c> pedido — que
    /// nesse caso não tem valor. Antes da primeira medição (o painel ainda
    /// escondido), o app usa um tamanho de reserva, para a conferência com
    /// a tela não ficar sem parâmetro.
    /// </para>
    /// </summary>
    private (double largura, double altura) TamanhoDoPainel()
    {
        double largura = _painel is { ActualWidth: > 0 } ? _painel.ActualWidth : 260;
        double altura = _painel is { ActualHeight: > 0 } ? _painel.ActualHeight : 120;
        return (largura, altura);
    }

    /// <summary>
    /// Atualiza as linhas do painel (a cada meio segundo).
    ///
    /// <para>
    /// Devolve <c>true</c> quando a atualização deu certo, e <c>false</c>
    /// quando o painel não pôde ser lido (ou nem estava visível). Quem
    /// liga o painel usa isso para não ficar com o relógio rodando e com o
    /// painel fora da tela.
    /// </para>
    /// </summary>
    private bool UpdatePainel()
    {
        if (_painel is null || !_painel.IsVisible) return false;

        try
        {
            // Durante a cena de GPU as leituras do driver ficam pausadas: cada
            // consulta trava o driver por um instante e aparecia como engasgo
            // no 1% low do benchmark. O painel para de ler junto — e mostra
            // travessão em vez do número velho.
            if (_gpuBenchActive)
            {
                _painel.Update(PainelRegras.LinhasSemLeitura());
                return true;
            }

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
            return true;
        }
        catch (Exception ex)
        {
            // Um painel que falha não pode derrubar o app: ele simplesmente
            // some. O interruptor desliga junto, senão a página continuaria
            // dizendo que o painel está ligado.
            _painelTimer?.Stop();
            ChkPainel.IsChecked = false;
            _painel?.Hide();
            RegistroDiario.Registrar("Falha ao atualizar o painel flutuante: " + ex.Message, "PAINEL");
            return false;
        }
    }

    /// <summary>
    /// Guarda a posição do painel (em DIPs) e escreve no arquivo.
    ///
    /// <para>
    /// A gravação no arquivo mora aqui, e não em quem chama: se a posição
    /// ficasse só na memória, o painel voltaria para o canto na próxima
    /// vez que o app abrisse.
    /// </para>
    /// </summary>
    private void SalvarPosicaoDoPainel()
    {
        // A posição é guardada mesmo com o painel escondido: a janela
        // continua sabendo onde está, e é essa posição que volta quando o
        // painel aparece de novo. (O botão "VOLTAR PARA O CANTO" também
        // funciona com o painel escondido.)
        if (_painel is null) return;

        // Uma janela que ainda não foi posicionada tem Left/Top sem valor
        // (NaN). Gravar isso no arquivo apagaria a posição que o dono tinha
        // deixado — número nenhum é melhor do que número inventado.
        if (!PainelRegras.PosicaoValida(_painel.Left, _painel.Top)) return;

        _settings.PainelX = _painel.Left;
        _settings.PainelY = _painel.Top;
        _settings.Save();
    }

    /// <summary>
    /// Devolve o painel para a posição guardada — ou para o canto, se a
    /// posição guardada não estiver em nenhum monitor (o dono trocou de
    /// monitor).
    /// </summary>
    private void RestaurarPosicaoDoPainel()
    {
        if (_painel is null) return;

        var (largura, altura) = TamanhoDoPainel();

        if (_settings.PainelX is double x && _settings.PainelY is double y
            && PainelWindow.DentroDaTela(x, y, largura, altura))
        {
            _painel.Left = x;
            _painel.Top = y;
            return;
        }

        var area = SystemParameters.WorkArea;
        _painel.Left = area.Right - largura - 12;
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
