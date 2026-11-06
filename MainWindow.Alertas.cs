using System.Globalization;
using System.Windows;
using System.Windows.Controls;

namespace TranalSentinel;

/// <summary>
/// Página TRANAL ALERTAS: as regras que o app vigia em segundo plano.
///
/// <para>
/// Aqui o usuário escolhe o que vigiar (temperatura, uso da memória,
/// clock...), o limite, quanto tempo a condição precisa valer antes do
/// aviso e a gravidade dele. As regras ficam gravadas no PC do usuário e
/// o app avalia sozinho.
///
/// </para>
/// <para>
/// A tela é somente leitura do ponto de vista do sistema: o app não mexe
/// em nada por causa de uma regra — ele só avisa.
/// </para>
/// </summary>
public partial class MainWindow
{
    /// <summary>Trava das regras de alerta (a vigia roda em outra thread).</summary>
    private static readonly object TravaAlertas = new();

    /// <summary>O último sensor escolhido pelo usuário (a nova regra nasce com ele).</summary>
    private string _ultimoSensorAlerta = "";

    /// <summary>Assinatura da lista de alertas ativos (evita redesenhar a tela à toa).</summary>
    private string _assinaturaAtivos = "";

    /// <summary>As opções de severidade mostradas na tela (o enum cru fica feio).</summary>
    private static readonly SeveridadeOpcao[] Severidades =
    {
        new(Severidade.Informativo, "Informativo"),
        new(Severidade.Baixo, "Baixo"),
        new(Severidade.Medio, "Médio"),
        new(Severidade.Alto, "Alto"),
        new(Severidade.Critico, "Crítico"),
    };

    /// <summary>Uma severidade com o nome que o usuário lê.</summary>
    private sealed record SeveridadeOpcao(Severidade Valor, string Rotulo);

    /// <summary>Prepara a página (chamada quando o menu "Alertas" é aberto).</summary>
    private void InitAlertas()
    {
        // Se o usuário nunca configurou nada, as regras padrão entram aqui
        // (e são gravadas, para o usuário ver e poder editar).
        RegrasAtivas();
        MontarListaAlertas();
        AtualizarAlertasAtivos();
    }

    /// <summary>
    /// Monta (de novo) a lista de regras na tela. É chamado depois de
    /// qualquer mudança: adicionar, remover ou trocar o sensor.
    /// </summary>
    private void MontarListaAlertas()
    {
        AlertasLista.Children.Clear();

        List<RegraAlerta> regras = CopiaRegras();

        AlertasVazio.Visibility = regras.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        foreach (var regra in regras)
            AlertasLista.Children.Add(CriarCartaoRegra(regra));
    }

    /// <summary>
    /// Um cartão por regra: o sensor, a operação, o limite, o tempo mínimo,
    /// a severidade, se está ativa e o botão de remover.
    /// </summary>
    private Border CriarCartaoRegra(RegraAlerta regra)
    {
        var opcoes = OpcoesSensor();

        // A descrição da regra (a frase que explica o que ela vigia).
        TextBlock? descricao = null;

        var sensor = new ComboBox
        {
            Style = (Style)FindResource("DarkCombo"),
            ItemsSource = opcoes,
            DisplayMemberPath = nameof(SensorCatalogoItem.Rotulo),
            SelectedItem = opcoes.FirstOrDefault(o => o.Chave == regra.Sensor),
            Width = 200,
        };
        sensor.SelectionChanged += (_, _) => TrocarSensor(regra, sensor);

        var operacao = new ComboBox
        {
            Style = (Style)FindResource("DarkCombo"),
            ItemsSource = new[] { ">=", "<=" },
            SelectedItem = regra.Operacao,
            Width = 66,
        };
        operacao.SelectionChanged += (_, _) =>
        {
            if (operacao.SelectedItem is not string op) return;
            regra.Operacao = op;
            AtualizarDescricao(regra, descricao);
            SalvarRegras();
        };

        var limite = new TextBox
        {
            Text = regra.Limite.ToString("0.##", CultureInfo.CurrentCulture),
            Width = 80,
        };
        limite.LostFocus += (_, _) => AlterarLimite(regra, limite, descricao);

        var segundos = new TextBox
        {
            Text = regra.SegundosMinimos.ToString(CultureInfo.CurrentCulture),
            Width = 80,
        };
        segundos.LostFocus += (_, _) => AlterarSegundos(regra, segundos, descricao);

        var severidade = new ComboBox
        {
            Style = (Style)FindResource("DarkCombo"),
            ItemsSource = Severidades,
            DisplayMemberPath = nameof(SeveridadeOpcao.Rotulo),
            SelectedItem = Severidades.FirstOrDefault(s => s.Valor == regra.Severidade),
            Width = 120,
        };
        severidade.SelectionChanged += (_, _) =>
        {
            if (severidade.SelectedItem is not SeveridadeOpcao op) return;
            regra.Severidade = op.Valor;
            AtualizarDescricao(regra, descricao);
            SalvarRegras();
        };

        var ativa = new CheckBox { IsChecked = regra.Ativa, VerticalAlignment = VerticalAlignment.Center };
        ativa.Click += (_, _) =>
        {
            regra.Ativa = ativa.IsChecked == true;
            SalvarRegras();
        };

        var remover = new Button
        {
            Style = (Style)FindResource("OutlineButton"),
            Content = "REMOVER",
            Padding = new Thickness(12, 5, 12, 5),
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
        };
        remover.Click += (_, _) => RemoverRegra(regra);

        var grade = new Grid();
        for (int i = 0; i < 6; i++) grade.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grade.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        grade.Children.Add(Coluna("SENSOR", sensor, 0, 18));
        grade.Children.Add(Coluna("OPERAÇÃO", operacao, 1, 18));
        grade.Children.Add(Coluna("LIMITE", limite, 2, 18));
        grade.Children.Add(Coluna("SEG. MÍNIMOS", segundos, 3, 18));
        grade.Children.Add(Coluna("SEVERIDADE", severidade, 4, 18));

        var caixaAtiva = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 18, 4) };
        caixaAtiva.Children.Add(new TextBlock { Text = "ATIVA", Foreground = (System.Windows.Media.Brush)FindResource("Muted"), FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        caixaAtiva.Children.Add(ativa);
        Grid.SetColumn(caixaAtiva, 5);
        grade.Children.Add(caixaAtiva);

        var botaoRemover = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 4) };
        botaoRemover.Children.Add(remover);
        Grid.SetColumn(botaoRemover, 6);
        grade.Children.Add(botaoRemover);

        descricao = new TextBlock
        {
            Foreground = (System.Windows.Media.Brush)FindResource("Muted"),
            FontSize = 11.5,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 12, 0, 0),
        };
        AtualizarDescricao(regra, descricao);

        var painel = new StackPanel();
        painel.Children.Add(grade);
        painel.Children.Add(descricao);

        var cartao = new Border
        {
            Style = (Style)FindResource("CardBorder"),
            Margin = new Thickness(0, 0, 0, 12),
            Padding = new Thickness(16, 14, 16, 14),
        };
        cartao.Child = painel;
        return cartao;
    }

    /// <summary>Uma coluna da grade (rótulo em cima, controle embaixo).</summary>
    private Border Coluna(string titulo, FrameworkElement controle, int coluna, double margemDireita)
    {
        var caixa = new StackPanel { Margin = new Thickness(0, 0, margemDireita, 0) };
        caixa.Children.Add(new TextBlock { Text = titulo, Foreground = (System.Windows.Media.Brush)FindResource("Muted"), FontSize = 11, Margin = new Thickness(0, 0, 0, 4) });
        caixa.Children.Add(controle);

        var coluna_ = new Border { Child = caixa };
        Grid.SetColumn(coluna_, coluna);
        return coluna_;
    }

    /// <summary>
    /// Os sensores que a tela oferece: o catálogo mais a bateria (que o
    /// motor lê por um caminho próprio, fora da tomada).
    /// </summary>
    private static List<SensorCatalogoItem> OpcoesSensor()
    {
        var lista = SensorCatalogo.Itens.ToList();
        lista.Add(new SensorCatalogoItem
        {
            Chave = MonitorAlertas.SensorBateria,
            Rotulo = MonitorAlertas.BateriaRotulo,
            Unidade = MonitorAlertas.BateriaUnidade,
            OperacaoPadrao = "<=",
            LimitePadrao = 20,
            SeveridadePadrao = Severidade.Medio,
            Dica = MonitorAlertas.BateriaDica,
        });
        return lista;
    }

    /// <summary>
    /// Trocar o sensor de uma regra adota os padrões do novo sensor (a
    /// unidade muda, então o limite antigo não faria sentido).
    /// </summary>
    private void TrocarSensor(RegraAlerta regra, ComboBox combo)
    {
        if (combo.SelectedItem is not SensorCatalogoItem item) return;
        if (regra.Sensor == item.Chave) return;

        regra.Sensor = item.Chave;
        regra.Operacao = item.OperacaoPadrao;
        regra.Limite = item.LimitePadrao;
        regra.Severidade = item.SeveridadePadrao;
        // O nome da regra acompanha o sensor escolhido (o do catálogo).
        regra.Rotulo = null;
        _ultimoSensorAlerta = item.Chave;

        SalvarRegras();

        // A lista é redesenhada depois (fora do evento): tirar o combo da
        // tela enquanto ele dispara o próprio evento derruba o WPF.
        Dispatcher.BeginInvoke(new Action(MontarListaAlertas));
    }

    /// <summary>Altera o limite da regra (o que o usuário digitou no campo).</summary>
    private void AlterarLimite(RegraAlerta regra, TextBox campo, TextBlock? descricao)
    {
        // Um limite negativo (ou NaN/infinito) deixa a regra degenerada: com
        // ">=" ela vale sempre, com NaN ela nunca vale. O campo volta ao
        // valor anterior, como acontece com os segundos.
        if (!double.TryParse(campo.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out double valor)
            || double.IsNaN(valor) || double.IsInfinity(valor) || valor < 0)
        {
            campo.Text = regra.Limite.ToString("0.##", CultureInfo.CurrentCulture);
            return;
        }
        regra.Limite = valor;
        AtualizarDescricao(regra, descricao);
        SalvarRegras();
    }

    /// <summary>Altera o tempo mínimo da regra (em segundos).</summary>
    private void AlterarSegundos(RegraAlerta regra, TextBox campo, TextBlock? descricao)
    {
        if (!int.TryParse(campo.Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out int valor) || valor < 0)
        {
            campo.Text = regra.SegundosMinimos.ToString(CultureInfo.CurrentCulture);
            return;
        }
        regra.SegundosMinimos = valor;
        AtualizarDescricao(regra, descricao);
        SalvarRegras();
    }

    /// <summary>Remove a regra da lista (o usuário pode recriá-la quando quiser).</summary>
    private void RemoverRegra(RegraAlerta regra)
    {
        lock (TravaAlertas)
        {
            var regras = RegrasAtivas();
            regras.RemoveAll(r => r.Id == regra.Id);
            _settings.Save();
        }
        MontarListaAlertas();
    }

    /// <summary>Nova regra: nasce com o sensor escolhido (o último ou o primeiro do catálogo).</summary>
    private void BtnNovaRegra_Click(object sender, RoutedEventArgs e)
    {
        var opcoes = OpcoesSensor();
        var item = opcoes.FirstOrDefault(o => o.Chave == _ultimoSensorAlerta) ?? opcoes[0];

        var regra = new RegraAlerta
        {
            Id = "reg-" + Guid.NewGuid().ToString("N")[..8],
            Sensor = item.Chave,
            Operacao = item.OperacaoPadrao,
            Limite = item.LimitePadrao,
            SegundosMinimos = 0,
            Ativa = true,
            Severidade = item.SeveridadePadrao,
            Rotulo = null,
        };

        lock (TravaAlertas)
        {
            RegrasAtivas().Add(regra);
            _settings.Save();
        }
        MontarListaAlertas();
    }

    /// <summary>Restaura as três regras de fábrica (CPU 85 °C, disco 60 °C, bateria 20 %).</summary>
    private void BtnPadroes_Click(object sender, RoutedEventArgs e)
    {
        lock (TravaAlertas)
        {
            _settings.Alertas = RegrasAlertaPadrao.Criar();
            _settings.Save();
        }
        MontarListaAlertas();
    }

    /// <summary>Grava as regras (o usuário que decide o que fica gravado no PC).</summary>
    private void SalvarRegras()
    {
        lock (TravaAlertas) _settings.Save();
        AtualizarAlertasAtivos();
    }

    /// <summary>Atualiza a frase que explica a regra.</summary>
    private static void AtualizarDescricao(RegraAlerta regra, TextBlock? descricao)
    {
        if (descricao is null) return;
        descricao.Text = DescreverRegra(regra);
    }

    /// <summary>
    /// O texto que explica a regra em uma frase (o que ela vigia e o que
    /// acontece quando a condição vale).
    /// </summary>
    private static string DescreverRegra(RegraAlerta regra)
    {
        var item = SensorCatalogo.PorChave(regra.Sensor);
        string sensor = item?.Rotulo
            ?? (regra.Sensor == MonitorAlertas.SensorBateria ? MonitorAlertas.BateriaRotulo : regra.Sensor);
        string unidade = item?.Unidade
            ?? (regra.Sensor == MonitorAlertas.SensorBateria ? MonitorAlertas.BateriaUnidade : "");
        string espera = regra.SegundosMinimos > 0
            ? $" (a condição precisa valer por {regra.SegundosMinimos} s antes do aviso)"
            : " (avisa na hora)";
        string severidade = Severidades.FirstOrDefault(s => s.Valor == regra.Severidade)?.Rotulo ?? "Médio";

        return $"Avisa quando {sensor} {regra.Operacao} {regra.Limite:0.##} {unidade}{espera}. Severidade: {severidade}.";
    }

    /// <summary>Mostra os alertas que estão valendo agora.</summary>
    private void AtualizarAlertasAtivos()
    {
        IReadOnlyList<Alerta> ativos;
        try { ativos = _alertas.Ativos; }
        catch { return; }   // a vigia pode estar mudando a lista neste instante

        // Só redesenha quando a lista muda (a tela é atualizada a cada tique).
        // O texto entra na assinatura: o valor do sensor muda a cada ciclo e
        // o aviso ativo mostra o valor de agora, não o do disparo.
        string assinatura = string.Join("|", ativos.Select(a => a.Id + "|" + a.Texto));
        if (assinatura == _assinaturaAtivos) return;
        _assinaturaAtivos = assinatura;

        AlertasAtivos.Children.Clear();
        AlertasAtivosVazio.Visibility = ativos.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        foreach (var alerta in ativos)
        {
            var linha = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
            linha.Children.Add(new TextBlock { Text = alerta.Titulo, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
            linha.Children.Add(new TextBlock { Text = alerta.Texto, Foreground = (System.Windows.Media.Brush)FindResource("Muted"), FontSize = 11.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0) });
            AlertasAtivos.Children.Add(linha);
        }
    }
}
