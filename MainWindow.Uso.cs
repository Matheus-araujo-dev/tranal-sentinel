using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace TranalSentinel;

/// <summary>
/// Página TRANAL USO: que programa ficou em frente na tela, quanto tempo,
/// e quem estava na frente quando a máquina ficou mais quente.
///
/// <para>
/// A tela só lê o que o núcleo já gravou (<see cref="UsoHistorico"/> e
/// <see cref="SensorHistorico"/>) e a regra que interpreta isso mora em
/// <see cref="UsoRegras"/>. Nada é recalculado aqui — a tela só mostra.
///
/// </para>
/// <para>
/// É somente leitura: o app não muda nada no sistema por causa desta aba.
/// O histórico de uso é um recurso de leitura, e ele fica no computador do
/// usuário.
/// </para>
/// </summary>
public partial class MainWindow
{
    /// <summary>
    /// O dia que a página está mostrando. O padrão é hoje; o seletor troca.
    /// </summary>
    private DateTime _usoDia = DateTime.Today;

    /// <summary>
    /// Trava da troca de dia: o seletor dispara o evento quando a página
    /// monta a lista, e isso não é a pessoa escolhendo outro dia.
    /// </summary>
    private bool _usoTrocandoDia;

    /// <summary>
    /// Quantas leituras estão em andamento (o botão fica travado enquanto
    /// houver uma).
    /// </summary>
    private int _usoCargas;

    /// <summary>Uma linha do ranking de uso (o que a tabela mostra).</summary>
    private sealed record LinhaUso(string Processo, string Tempo, string Percentual);

    /// <summary>Uma linha das sessões mais longas.</summary>
    private sealed record LinhaSessaoUso(string Processo, string Titulo, string Inicio, string Duracao);

    /// <summary>Um dia que o seletor oferece.</summary>
    private sealed record OpcaoDiaUso(DateTime Data, string Rotulo);

    /// <summary>Os dados do dia carregado (a leitura acontece fora da thread da tela).</summary>
    private sealed record DadosUso(DateTime Dia, List<TrocaUso> Trocas, PeriodoUso Periodo, RelatorioUso Relatorio);

    /// <summary>
    /// Prepara a aba "Uso" (chamada quando o menu "Uso" é aberto).
    /// </summary>
    private void InitUso()
    {
        if (UsoDia == null) return;

        CarregarDiasUso();
        _ = AtualizarUsoAsync();
    }

    /// <summary>
    /// Monta a lista de dias do seletor. Hoje fica sempre na lista — é o dia
    /// que a pessoa quer ver primeiro.
    /// </summary>
    private void CarregarDiasUso()
    {
        DateTime hoje = DateTime.Today;

        // Os dias com histórico, do mais novo para o mais antigo.
        var dias = UsoHistorico.DiasComUso();

        var opcoes = dias
            .Select(d => new OpcaoDiaUso(d.Date, UsoTexto.RotuloDia(d.Date, hoje)))
            .ToList();

        if (!opcoes.Any(o => o.Data == hoje))
            opcoes.Insert(0, new OpcaoDiaUso(hoje, UsoTexto.RotuloDia(hoje, hoje)));

        // O dia mostrado: o que a pessoa escolheu (se ainda existir), senão o
        // de hoje quando ele tem histórico, senão o mais recente que tem —
        // senão a página abriria vazia mesmo com histórico na máquina.
        DateTime escolhido = opcoes.Any(o => o.Data == _usoDia) ? _usoDia
                            : dias.Any(d => d.Date == hoje) ? hoje
                            : dias.Count > 0 ? dias[0].Date
                            : hoje;

        _usoTrocandoDia = true;
        try
        {
            UsoDia.ItemsSource = opcoes;
            UsoDia.SelectedItem = opcoes.FirstOrDefault(o => o.Data == escolhido);
        }
        finally
        {
            _usoTrocandoDia = false;
        }

        _usoDia = escolhido;
    }

    /// <summary>
    /// Troca o dia mostrado (o seletor de dia).
    /// </summary>
    private void UsoDia_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_usoTrocandoDia) return;
        if (UsoDia.SelectedItem is not OpcaoDiaUso opcao) return;

        _usoDia = opcao.Data;
        _ = AtualizarUsoAsync();
    }

    /// <summary>
    /// Botão "ATUALIZAR": recarrega o dia mostrado.
    /// </summary>
    private async void BtnUsoAtualizar_Click(object sender, RoutedEventArgs e) =>
        await AtualizarUsoAsync();

    /// <summary>
    /// Recarrega o dia: lê o histórico de uso e o de sensores e monta a tela.
    /// A leitura é I/O de arquivo — vai para fora da thread da tela.
    /// </summary>
    private async Task AtualizarUsoAsync()
    {
        DateTime dia = _usoDia;

        _usoCargas++;
        if (BtnUsoAtualizar != null) BtnUsoAtualizar.IsEnabled = false;

        DadosUso dados;
        try
        {
            dados = await Task.Run(() => CarregarUso(dia)).ConfigureAwait(true);
        }
        catch (Exception)
        {
            // Falha ao ler o histórico não pode derrubar a página: ela fica
            // mostrando o que sabe, que é o estado vazio honesto.
            dados = new DadosUso(dia, new List<TrocaUso>(), new PeriodoUso(dia, dia), new RelatorioUso());
        }
        finally
        {
            _usoCargas--;
            if (_usoCargas <= 0 && BtnUsoAtualizar != null) BtnUsoAtualizar.IsEnabled = true;
        }

        // Uma leitura antiga (a pessoa trocou o dia no meio da leitura) não
        // pode pintar a tela com o dia errado.
        if (dados.Dia != _usoDia) return;

        try
        {
            MostrarUso(dados);
        }
        catch (Exception)
        {
            // A aba do uso é uma leitura: se a montagem falhar, a página fica
            // como está. Derrubar o app por causa de um texto seria absurdo.
        }
    }

    /// <summary>
    /// Lê o dia e calcula o relatório. Roda fora da thread da tela.
    /// </summary>
    private static DadosUso CarregarUso(DateTime dia)
    {
        // As trocas do dia e a última do dia anterior: a que atravessa a
        // meia-noite. Sem ela, a noite inteira ficaria sem dono quando o app
        // rodou a noite toda sem trocar de programa.
        var trocas = UsoHistorico.LerComAnterior(dia);
        var amostras = AmostrasDoDia(dia);

        // O período é uma regra do núcleo: começa na primeira evidência de
        // que o app rodava e para na última. Sem isso, as horas em que o app
        // estava fechado seriam creditadas a um programa.
        var periodo = UsoRegras.Periodo(dia, DateTime.Now, amostras, trocas);

        var relatorio = UsoRegras.Calcular(trocas, periodo.Inicio, periodo.Fim, amostras);
        return new DadosUso(dia, trocas, periodo, relatorio);
    }

    /// <summary>
    /// As amostras de sensores do dia. O log de sensores é um arquivo só (com
    /// vários dias dentro), então a leitura é limitada ao dia pedido: o
    /// arquivo chega a dezoito megabytes e interpretá-lo inteiro custa meio
    /// segundo.
    /// </summary>
    private static List<AmostraSensor> AmostrasDoDia(DateTime dia)
    {
        DateTime inicio = dia.Date;
        DateTime fim = inicio.AddDays(1).AddMilliseconds(-1);
        return SensorHistorico.Ler(inicio, fim);
    }

    /// <summary>
    /// Coloca o que foi lido na tela.
    /// </summary>
    private void MostrarUso(DadosUso dados)
    {
        if (UsoVazio == null) return;

        var relatorio = dados.Relatorio;
        bool semUso = relatorio.PorApp.Count == 0;
        bool hoje = dados.Dia.Date == DateTime.Today;

        // Estado vazio honesto: o app só registra o uso enquanto está rodando.
        UsoVazio.Visibility = semUso ? Visibility.Visible : Visibility.Collapsed;

        // ---- tempo de tela
        if (semUso)
        {
            UsoTotal.Text = "—";
            UsoTotalObs.Text = "Sem registro neste dia.";
        }
        else
        {
            UsoTotal.Text = UsoTexto.Duracao(relatorio.Total);
            UsoTotalObs.Text = $"Observado das {UsoTexto.Hora(dados.Periodo.Inicio)} às {UsoTexto.Hora(dados.Periodo.Fim)}.";
        }

        // ---- o que estava em frente
        UsoDestaqueRotulo.Text = hoje ? "EM FRENTE AGORA" : "EM FRENTE NO FIM DAQUELE DIA";
        UsoDestaque.Text = string.IsNullOrEmpty(relatorio.EmDestaque) ? "o app não sabe" : relatorio.EmDestaque;
        UsoDestaqueTitulo.Text = relatorio.EmDestaqueTitulo;
        UsoDestaqueQuando.Text = dados.Trocas.Count == 0 ? "" : $"visto às {UsoTexto.Hora(dados.Trocas.Max(t => t.Momento))}";

        // ---- ranking
        var linhas = relatorio.PorApp
            .Select(u => new LinhaUso(u.Processo, UsoTexto.Duracao(u.Tempo), UsoTexto.Percentual(u.Percentual)))
            .ToList();

        UsoRanking.ItemsSource = linhas;
        UsoRanking.Visibility = linhas.Count == 0 ? Visibility.Collapsed : Visibility.Visible;

        // ---- sessões mais longas
        var sessoes = relatorio.MaioresSessoes
            .Select(s => new LinhaSessaoUso(s.Processo, string.IsNullOrWhiteSpace(s.Titulo) ? "—" : s.Titulo, UsoTexto.Hora(s.Inicio), UsoTexto.Duracao(s.Duracao)))
            .ToList();

        UsoSessoes.ItemsSource = sessoes;
        UsoSessoesVazio.Visibility = sessoes.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        // ---- quem fez o calor
        if (relatorio.TempMaximaCpu is null)
        {
            UsoCalor.Text = "O app não sabe: não há histórico de sensores deste dia.";
            UsoCalorTemp.Text = "";
            UsoCalorDica.Text = "O log de sensores só existe enquanto o app está rodando. Sem ele, o app não inventa temperatura.";
        }
        else
        {
            string calor = string.IsNullOrEmpty(relatorio.QuemFezOCalor)
                ? "Não há histórico de uso para dizer quem estava em frente."
                : $"{relatorio.QuemFezOCalor} estava em frente quando a CPU ficou mais quente.";

            UsoCalor.Text = calor;
            UsoCalorTemp.Text = $"Máxima da CPU no dia: {UsoTexto.Temperatura(relatorio.TempMaximaCpu)}.";
            UsoCalorDica.Text = "O que fazer: feche o programa que estava aberto ou espere a máquina esfriar antes de continuar com algo pesado. O app não muda nada sozinho.";
        }
    }
}
