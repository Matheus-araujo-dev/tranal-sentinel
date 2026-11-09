using System.Windows;
using System.Windows.Threading;

namespace TranalSentinel;

/// <summary>
/// A página TRANAL VENTOINHAS: o RPM de cada ventoinha, o recorde dela e o
/// veredito sobre o desgaste.
///
/// <para>
/// A tela não decide nada: ela consome os cartões que o núcleo monta
/// (<see cref="VentoinhaRegras.CartaoParaTela"/>) e o histórico que o banco
/// devolve. Os números e as frases são do núcleo — assim a tela e o relatório
/// nunca discordam entre si.
/// </para>
/// </summary>
public partial class MainWindow
{
    /// <summary>
    /// A assinatura do que está na tela.
    ///
    /// <para>
    /// A janela atualiza a cada segundo, e redesenhar os cartões a cada
    /// segundo seria desperdício. A página só é refeita quando o conteúdo
    /// muda de verdade.
    /// </para>
    /// </summary>
    private string _assinaturaVentoinhas = "";

    /// <summary>Prepara a página (chamada quando o menu "Ventoinhas" é aberto).</summary>
    private void InitVentoinhas()
    {
        _assinaturaVentoinhas = "";
        MostrarVentoinhas();
    }

    /// <summary>
    /// O botão de atualizar: relê as ventoinhas e redesenha a página.
    /// </summary>
    private void BtnVentoinhasAtualizar_Click(object sender, RoutedEventArgs e)
    {
        _assinaturaVentoinhas = "";
        MostrarVentoinhas();
    }

    /// <summary>Redesenha a página inteira (os cartões e o histórico).</summary>
    private void MostrarVentoinhas()
    {
        AtualizarVentoinhas();
        MostrarHistoricoVentoinhas();
    }

    /// <summary>
    /// Atualiza os cartões de cada ventoinha (sem mexer no histórico, que é
    /// do banco).
    /// </summary>
    private void AtualizarVentoinhas()
    {
        try
        {
            var cartoes = _ventoinhas.Cartoes();

            // A assinatura decide se o cartão é redesenhado: ela precisa
            // traer tudo o que o cartão mostra (inclusive a temperatura do
            // máximo do dia), senão a tela ficaria com um texto velho.
            string assinatura = string.Join("|", cartoes.Select(c =>
                c.Id + "|" + c.Titulo + "|" + c.Texto + "|" + c.RpmAtual + "|"
                + c.RpmMaximoDoDia + "|" + c.TemperaturaNoMaximoDoDia + "|"
                + c.AmostrasDoDia));

            if (assinatura == _assinaturaVentoinhas) return;
            _assinaturaVentoinhas = assinatura;

            VentoinhasCards.ItemsSource = cartoes.Select(VentoinhaRegras.CartaoParaTela).ToList();

            VentoinhasVazio.Text = cartoes.Count == 0
                ? "Nenhuma ventoinha encontrada ainda. O app espera o sensor da placa-mãe e o da placa de vídeo aparecerem."
                : "";
            VentoinhasVazio.Visibility = cartoes.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            // O aviso de gravação: o histórico que não está sendo gravado
            // precisa aparecer — senão o app cala sobre um dado que está se
            // perdendo.
            VentoinhasAviso.Text = _ventoinhas.UltimoErro;
            VentoinhasAviso.Visibility = _ventoinhas.UltimoErro.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            VentoinhasAviso.Text = "Não foi possível mostrar as ventoinhas: " + ex.Message;
            VentoinhasAviso.Visibility = Visibility.Visible;
        }
    }

    /// <summary>
    /// O histórico dos últimos dias (o "RPM com o tempo").
    /// </summary>
    private void MostrarHistoricoVentoinhas()
    {
        try
        {
            var leitura = _ventoinhas.Historico(
                DateTime.Now.Date.AddDays(-(Banco.DiasRetidosVentoinha - 1)));

            VentoinhasHistorico.ItemsSource = VentoinhaRegras.LinhasDoHistorico(leitura.Linhas);

            // "Não tem histórico" e "não deu para ler" são frases diferentes:
            // a segunda é uma falha, e a tela precisa dizer qual é.
            VentoinhasHistoricoVazio.Text = leitura.Linhas.Count == 0
                ? (leitura.Completa
                    ? "Ainda não há histórico diário de ventoinhas guardado."
                    : "Não deu para ler o histórico das ventoinhas no banco de dados.")
                : "";
            VentoinhasHistoricoVazio.Visibility = leitura.Linhas.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            VentoinhasHistorico.ItemsSource = null;
            VentoinhasHistoricoVazio.Text = "Não foi possível ler o histórico das ventoinhas: " + ex.Message;
            VentoinhasHistoricoVazio.Visibility = Visibility.Visible;
        }
    }

    /// <summary>
    /// Sobe o relógio que grava o que o app sabe das ventoinhas.
    /// </summary>
    private void IniciarVentoinhas()
    {
        _timerVentoinha = new DispatcherTimer { Interval = MonitorVentoinhas.IntervaloGravacao };
        _timerVentoinha.Tick += (_, _) => _ventoinhas.Gravar();
        _timerVentoinha.Start();
    }
}
