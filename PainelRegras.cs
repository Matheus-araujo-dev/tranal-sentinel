namespace TranalSentinel;

/// <summary>
/// Uma ventoinha como o painel a mostra: o nome legível e o RPM de agora
/// (null quando ela não está dando leitura).
/// </summary>
/// <param name="Nome">O nome do sensor ("Fan #1"), nunca o id interno.</param>
/// <param name="Rpm">As rotações por minuto (null quando não deu para ler).</param>
public sealed record PainelVentoinha(string Nome, double? Rpm);

/// <summary>
/// As regras puras do monitor no desktop: a ventoinha que ele mostra, o canto
/// escolhido e a posição guardada. O que o monitor mostra (as linhas) é o que
/// o dono marcou em "O que mostrar" (<see cref="OverlayMetrics"/>), igual no
/// desktop e no jogo.
///
/// <para>
/// A ventoinha mostrada é a de <b>maior RPM entre as que estão dando
/// leitura</b> (a que está trabalhando). Sem isso, o painel mostraria a
/// ventoinha parada e o dono pensaria que o PC está com ela morta.
/// </para>
/// </summary>
public static class PainelRegras
{
    /// <summary>O separador entre as partes de uma linha (" · ").</summary>
    private const string Separador = " · ";

    /// <summary>
    /// A ventoinha que o painel mostra: a de maior RPM entre as que estão
    /// dando leitura. Se nenhuma estiver dando leitura, a primeira que o app
    /// conhece (sem RPM — o número que não existe não é inventado).
    /// </summary>
    public static PainelVentoinha? EscolherVentoinha(IReadOnlyList<PainelVentoinha> ventoinhas)
    {
        if (ventoinhas.Count == 0) return null;

        var comLeitura = ventoinhas.Where(v => v.Rpm is not null).ToList();

        if (comLeitura.Count > 0)
        {
            // O desempate é pelo nome: duas ventoinhas com o mesmo RPM têm
            // que dar sempre o mesmo painel.
            return comLeitura
                .OrderByDescending(v => v.Rpm!.Value)
                .ThenBy(v => v.Nome, StringComparer.Ordinal)
                .First();
        }

        return ventoinhas.OrderBy(v => v.Nome, StringComparer.Ordinal).First();
    }

    /// <summary>
    /// O valor da linha da ventoinha ("Fan #1 · 3.409 RPM"). O monitor usa o
    /// mesmo texto na linha VENT, quando o dono escolhe mostrá-la.
    /// </summary>
    public static string ValorDaVentoinha(PainelVentoinha? ventoinha)
    {
        if (ventoinha is null) return SensorTexto.Ausente;

        string rpm = SensorTexto.Rpm(ventoinha.Rpm);
        if (string.IsNullOrWhiteSpace(ventoinha.Nome)) return rpm;

        return ventoinha.Nome + Separador + rpm;
    }

    /// <summary>
    /// Diz se a posição do painel pode ser gravada.
    ///
    /// <para>
    /// Uma janela que ainda não foi posicionada tem <c>Left</c>/<c>Top</c>
    /// sem valor (<see cref="double.NaN"/>). Gravar isso no arquivo
    /// apagaria a posição que o dono tinha deixado — número nenhum é
    /// melhor do que número inventado.
    /// </para>
    /// </summary>
    public static bool PosicaoValida(double x, double y) => !double.IsNaN(x) && !double.IsNaN(y);

    /// <summary>A distância (em DIPs) entre o monitor e a borda da tela.</summary>
    public const double MargemDoCanto = 12;

    /// <summary>
    /// Onde o monitor fica no desktop para o canto escolhido ("TL", "TR", "BL"
    /// ou "BR"), dentro da área de trabalho (sem a barra de tarefas).
    ///
    /// <para>
    /// É a mesma escolha de canto do jogo: o dono escolhe na página MONITOR e
    /// ela vale nos dois modos. Canto desconhecido vale como superior esquerdo.
    /// </para>
    /// </summary>
    public static (double X, double Y) PosicaoNoCanto(string? canto, double largura, double altura,
                                                       double areaEsquerda, double areaTopo,
                                                       double areaDireita, double areaBase)
    {
        string c = canto ?? "TL";
        double x = c.EndsWith('R') ? areaDireita - largura - MargemDoCanto : areaEsquerda + MargemDoCanto;
        double y = c.StartsWith('B') ? areaBase - altura - MargemDoCanto : areaTopo + MargemDoCanto;
        return (x, y);
    }

    /// <summary>
    /// A posição do painel está em algum monitor?
    /// </summary>
    ///
    /// <para>
    /// A posição é guardada em DIPs (a unidade do WPF) e a tela virtual é lida
    /// na mesma unidade. Se o dono trocou de monitor, o painel volta para o
    /// canto em vez de ficar fora da tela.
    ///
    /// <para>
    /// A pergunta é aritmética pura, com a tela como argumento: por isso ela
    /// mora no núcleo, e a janela só consome.
    /// </para>
    /// </summary>
    public static bool DentroDaTela(double left, double top, double largura, double altura,
                                    double telaX, double telaY, double telaLargura, double telaAltura)
    {
        return left + largura > telaX && left < telaX + telaLargura
            && top + altura > telaY && top < telaY + telaAltura;
    }
}
