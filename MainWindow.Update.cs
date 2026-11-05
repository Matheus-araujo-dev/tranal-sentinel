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
/// O app não troca o próprio executável: ele baixa o novo, confere o tamanho, chama o próprio
/// executável com a flag <c>--atualizar</c> e avisa que a troca acontece quando o app fechar.
/// </summary>
public partial class MainWindow
{
    private bool _atualizacaoVerificada;   // uma checagem por sessão (não fica spammando)
    private bool _atualizacaoBaixando;
    private ReleaseGitHub? _releaseNova;
    private Versao? _versaoNova;

    /// <summary>
    /// Dispara a checagem de nova versão em segundo plano.
    /// Seguro para chamar mesmo sem rede: se falhar, nada aparece na tela.
    /// </summary>
    public void IniciarVerificacaoDeAtualizacao()
    {
        if (_atualizacaoVerificada) return;
        _atualizacaoVerificada = true;

        // A consulta ao GitHub roda fora da thread da janela: pode demorar ou não responder.
        Task.Run(() =>
        {
            ResultadoAtualizacao resultado = AutoUpdate.Checar();
            if (!resultado.HaVersaoNova || resultado.Release is null || resultado.VersaoNova is null) return;

            try
            {
                Dispatcher.Invoke(() => MostrarBannerAtualizacao(resultado.Release, resultado.VersaoNova));
            }
            catch (InvalidOperationException) { }   // a janela já está fechando
            catch (TaskCanceledException) { }
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
                            + "O executável novo é baixado agora e entra no lugar quando o app fechar.";

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

    /// <summary>Baixa a versão nova, confere o tamanho e prepara a troca do executável.</summary>
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
            string destino = AutoUpdate.CaminhoArquivoNovo;
            DownloadConcluido download = await AutoUpdate.BaixarAsync(release, destino, progresso);

            // O arquivo tem que ter o tamanho exato da release: download cortado não vira executável.
            if (release.Tamanho > 0 && download.TamanhoBaixado != release.Tamanho)
                throw new IOException($"O arquivo baixado tem {DriveCard.Fmt(download.TamanhoBaixado)} e o esperado é {DriveCard.Fmt(release.Tamanho)}.");

            // O log vem antes do processo filho: é ele que informa qual processo está esperando a troca.
            string versao = _versaoNova?.Completa ?? release.Tag;
            string mensagem = $"ATUALIZAÇÃO: {versao} baixada ({DriveCard.Fmt(download.TamanhoBaixado)}). "
                            + $"A troca do executável fica pendente até o app fechar (pid {Environment.ProcessId}).";
            Log(mensagem);
            AutoUpdate.RegistrarNoLog($"{mensagem} Arquivo: {destino}");

            if (!AutoUpdate.IniciarTrocaDeExecutavel(destino))
            {
                AutoUpdate.RegistrarNoLog("Falha ao iniciar o processo que troca o executável.");
                throw new IOException("Não foi possível iniciar a troca do executável.");
            }

            if (UpdateProgressText is not null)
                UpdateProgressText.Text = "Pronto. Feche o TRANAL SENTINEL para concluir a atualização.";
            if (BtnAtualizar is not null) BtnAtualizar.Content = "ATUALIZAR";
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
