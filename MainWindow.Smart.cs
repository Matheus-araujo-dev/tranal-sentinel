using System.Windows;
using System.Windows.Controls;

namespace TranalSentinel;

/// <summary>
/// Página TRANAL S.M.A.R.T.: a tabela de atributos de cada disco.
///
/// <para>
/// É a mesma leitura que o CrystalDiskInfo faz: id, nome, flags, valor
/// normalizado, pior valor, limiar e o valor cru de cada atributo. O app
/// só lê — nada é enviado ao disco.
///
/// </para>
/// <para>
/// O caminho do NVMe (a página SMART/Health lida direto do disco) exige
/// execução como administrador. Quando a leitura não acontece, a página
/// explica o que faltou, em vez de mostrar uma tela vazia.
/// </para>
/// </summary>
public partial class MainWindow
{
    /// <summary>Trava da leitura (o botão não fica clicando sozinho).</summary>
    private bool _smartBusy;

    /// <summary>True quando a leitura desta sessão já aconteceu (a página não volta ao texto inicial).</summary>
    private bool _smartJaLeu;

    /// <summary>Prepara a página (chamada quando o menu "S.M.A.R.T." é aberto).</summary>
    private void InitSmart()
    {
        if (SmartStatus == null) return;
        if (_smartJaLeu) return;

        SmartStatus.Text = "Clique em LER AGORA para ler o S.M.A.R.T. de cada disco. A leitura só informa: nada é enviado ao disco.";
    }

    private async void BtnSmartLer_Click(object sender, RoutedEventArgs e) => await LerSmartAsync();

    /// <summary>Lê o S.M.A.R.T. de todos os discos e monta os cartões.</summary>
    private async Task LerSmartAsync()
    {
        if (_smartBusy) return;
        _smartBusy = true;
        BtnSmartLer.IsEnabled = false;
        SmartStatus.Text = "Lendo o S.M.A.R.T. dos discos…";
        try
        {
            // A leitura é de I/O do Windows: vai para fora da thread da tela.
            var discos = await Task.Run(() => SmartLeitor.LerTodos());
            SmartDiscos.ItemsSource = discos;
            _smartJaLeu = true;

            int comDados = discos.Count(d => d.TemDados);
            int semDados = discos.Count - comDados;
            SmartStatus.Text = discos.Count == 0
                ? "Nenhum disco encontrado."
                : $"{discos.Count} disco(s) · {comDados} com S.M.A.R.T." +
                  (semDados > 0 ? $" · {semDados} sem S.M.A.R.T. (veja a observação de cada um)" : "") +
                  $" · lido às {DateTime.Now:HH:mm:ss}";
        }
        catch (Exception ex) { SmartStatus.Text = "Falhou: " + ex.Message; }
        finally
        {
            _smartBusy = false;
            BtnSmartLer.IsEnabled = true;
        }
    }
}
