using System.IO;
using System.Windows;
using System.Windows.Media;

namespace TranalSentinel;

/// <summary>
/// Banner de "nova versão disponível" (mesmo formato do aviso de sensores na lateral).
///
/// A checagem acontece em segundo plano e só mostra o banner quando existe versão nova.
/// O download é feito pelo núcleo (<see cref="AutoUpdate"/>); aqui é só a interface.
///
/// O app não troca o próprio executável: ele baixa o instalador (<c>TranalSentinel-Setup.exe</c>),
/// confere o tamanho, roda o instalador em modo silencioso e fecha. O instalador troca os arquivos
/// e reabre o app (DEC-APPS-DISTRIBUICAO-001).
/// </summary>
public partial class MainWindow
{
    private bool _atualizacaoBaixando;
    private ReleaseGitHub? _releaseNova;
    private Versao? _versaoNova;
    private System.Windows.Threading.DispatcherTimer? _timerAtualizacao;

    /// <summary>
    /// Dispara a checagem de nova versão em segundo plano e agenda rechecagens
    /// a cada hora enquanto o app estiver aberto.
    /// </summary>
    public void IniciarVerificacaoDeAtualizacao()
    {
        ChecarAtualizacaoAgora();

        if (_timerAtualizacao is null)
        {
            _timerAtualizacao = new System.Windows.Threading.DispatcherTimer(System.Windows.Threading.DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromHours(1),
            };
            _timerAtualizacao.Tick += (_, _) => ChecarAtualizacaoAgora();
            _timerAtualizacao.Start();
        }
    }

    private void ChecarAtualizacaoAgora()
    {
        if (_atualizacaoBaixando || _releaseNova is not null) return;

        Task.Run(() =>
        {
            ResultadoAtualizacao resultado = AutoUpdate.Checar();
            if (!resultado.HaVersaoNova || resultado.Release is null || resultado.VersaoNova is null) return;

            try
            {
                Dispatcher.Invoke(() => MostrarBannerAtualizacao(resultado.Release, resultado.VersaoNova));
            }
            catch (InvalidOperationException)
            {
                // A janela já está fechando e o Dispatcher não aceita mais a mensagem: o aviso de nova versão é dispensado.
            }
            catch (TaskCanceledException)
            {
                // A consulta ao GitHub foi cancelada junto com o fechamento: o aviso de nova versão é dispensado.
            }
        });
    }

    /// <summary>Mostra o aviso de nova versão na lateral.</summary>
    private void MostrarBannerAtualizacao(ReleaseGitHub release, Versao versao)
    {
        if (UpdateWarn is null || UpdateWarnText is null) return;

        _releaseNova = release;
        _versaoNova = versao;

        string tamanho = release.Tamanho > 0 ? DriveCard.Fmt(release.Tamanho) : "tamanho desconhecido";
        UpdateWarnText.Text = $"Versão {versao.Completa} disponível ({tamanho}). "
                            + "O instalador é baixado agora; o app fecha, atualiza sozinho e abre de novo.";

        if (UpdateProgress is not null) UpdateProgress.Visibility = Visibility.Collapsed;
        if (UpdateProgressText is not null) UpdateProgressText.Text = "";
        ProgressoAtualizacao(0, release.Tamanho);
        if (BtnAtualizar is not null)
        {
            BtnAtualizar.IsEnabled = true;
            BtnAtualizar.Content = "ATUALIZAR";
        }

        UpdateWarn.Visibility = Visibility.Visible;
    }

    /// <summary>Baixa o instalador da versão nova, confere o tamanho, roda o instalador e fecha o app.</summary>
    private async void BtnAtualizar_Click(object sender, RoutedEventArgs e)
    {
        if (_atualizacaoBaixando) return;
        ReleaseGitHub? release = _releaseNova;
        if (release is null) return;

        _atualizacaoBaixando = true;
        if (BtnAtualizar is not null)
        {
            BtnAtualizar.IsEnabled = false;
            BtnAtualizar.Content = "BAIXANDO…";
        }
        if (UpdateProgress is not null) UpdateProgress.Visibility = Visibility.Visible;
        if (UpdateProgressText is not null) UpdateProgressText.Text = "Iniciando o download…";

        var progresso = new Progress<long>(lidos => ProgressoAtualizacao(lidos, release.Tamanho));

        try
        {
            string destino = AutoUpdate.CaminhoInstaladorBaixado;
            DownloadConcluido download = await AutoUpdate.BaixarAsync(release, destino, progresso);

            // O arquivo tem que ter o tamanho exato da release: instalador cortado não é rodado.
            if (release.Tamanho > 0 && download.TamanhoBaixado != release.Tamanho)
                throw new IOException($"O arquivo baixado tem {DriveCard.Fmt(download.TamanhoBaixado)} e o esperado é {DriveCard.Fmt(release.Tamanho)}.");

            string versao = _versaoNova?.Completa ?? release.Tag;
            string mensagem = $"ATUALIZAÇÃO: instalador da {versao} baixado ({DriveCard.Fmt(download.TamanhoBaixado)}). "
                            + "O app fecha para o instalador trocar os arquivos e o instalador o reabre no fim.";
            Log(mensagem);
            AutoUpdate.RegistrarNoLog($"{mensagem} Arquivo: {destino}. Log do instalador: {AutoUpdate.CaminhoLogInstalador}");

            if (!AutoUpdate.IniciarInstalador(destino))
            {
                AutoUpdate.RegistrarNoLog("Falha ao iniciar o instalador.");
                throw new IOException("Não foi possível iniciar o instalador.");
            }

            if (UpdateProgressText is not null)
                UpdateProgressText.Text = "Fechando para atualizar; o app abre de novo sozinho…";

            // O instalador espera o app sair (até 30 s) antes de trocar os arquivos.
            // Fechar pelo caminho normal grava o tráfego do dia e o resto do estado.
            await Task.Delay(1000);
            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            string mensagem = "Falha no download da atualização: " + ex.Message;
            if (UpdateProgressText is not null) UpdateProgressText.Text = mensagem;
            Log(mensagem);
            AutoUpdate.RegistrarNoLog(mensagem);
            if (BtnAtualizar is not null)
            {
                BtnAtualizar.IsEnabled = true;
                BtnAtualizar.Content = "TENTAR DE NOVO";
            }
        }
        finally
        {
            _atualizacaoBaixando = false;
        }
    }

    /// <summary>Esconde o aviso de nova versão (o usuário olha depois).</summary>
    private void BtnAtualizarDepois_Click(object sender, RoutedEventArgs e)
    {
        if (UpdateWarn is not null) UpdateWarn.Visibility = Visibility.Collapsed;
        _releaseNova = null;
        _versaoNova = null;
    }

    /// <summary>Atualiza a barra de progresso do download (bytes baixados / total).</summary>
    private void ProgressoAtualizacao(long baixados, long total)
    {
        double fracao = total > 0 ? Math.Clamp((double)baixados / total, 0, 1) : 0;
        if (UpdateProgressFill?.RenderTransform is ScaleTransform escala) escala.ScaleX = fracao;
        if (UpdateProgressText is not null && total > 0)
            UpdateProgressText.Text = $"{DriveCard.Fmt(baixados)} de {DriveCard.Fmt(total)}";
    }
}
