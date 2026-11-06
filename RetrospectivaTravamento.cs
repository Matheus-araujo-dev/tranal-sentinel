namespace TranalSentinel;

/// <summary>
/// O que os sensores estavam fazendo na hora de um travamento.
///
/// <para>
/// É a resposta para a pergunta que o TRANAL SAÚDE não conseguia responder
/// sozinho: o congelamento aconteceu, mas e a temperatura da CPU, a carga da
/// GPU e o clock nos dois minutos antes? O log contínuo de sensores
/// (<see cref="SensorHistorico"/>) tem esses números; aqui eles viram uma
/// frase para a tela.
/// </para>
/// </summary>
public sealed class RetrospectivaTravamento
{
    /// <summary>
    /// O momento do travamento — o mesmo do evento Kernel-Power 41 (quando o
    /// Windows registrou o boot seguinte).
    /// </summary>
    public DateTime Data { get; init; }

    /// <summary>
    /// Havia log de sensores para este travamento? Quando é falso, o
    /// <see cref="Resumo"/> explica por quê (o app não estava rodando, ou
    /// nunca chegou a gravar nada).
    /// </summary>
    public bool TemDados { get; init; }

    /// <summary>O texto pronto para a tela (vazio quando não há o que dizer).</summary>
    public string Resumo { get; init; } = "";
}

/// <summary>
/// Junta o log de sensores com os travamentos: o que a máquina estava fazendo
/// nos minutos que antecederam o congelamento.
///
/// <para>
/// A regra mora aqui e é pura: entra a lista de travamentos e a lista de
/// amostras, sai a lista de retrospectivas (na mesma ordem dos travamentos).
/// Nenhum arquivo é lido aqui — quem lê é a coleta.
/// </para>
/// </summary>
public static class CorrelatorTravamento
{
    /// <summary>Quanto tempo antes do travamento a janela olha.</summary>
    private static readonly TimeSpan Janela = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Margem do instante do congelamento: o log pode ter uma amostra gravada
    /// já durante a falha (a máquina morre logo depois de escrever).
    /// </summary>
    private static readonly TimeSpan Margem = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Até quanto tempo antes do congelamento o app pode ter estado de pé. Se a
    /// última amostra for mais antiga que isto, o PC estava desligado (ou o
    /// app não estava rodando) e não há o que correlacionar.
    /// </summary>
    private static readonly TimeSpan Tolerancia = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Correlaciona os travamentos com o log de sensores.
    ///
    /// <para>
    /// A âncora é a hora em que a máquina parou — o <see cref="TravamentoInfo.InstanteDesligamento"/>,
    /// que vem do evento 6008 (o desligamento inesperado). O <see cref="TravamentoInfo.Data"/>
    /// é o boot seguinte: se o PC só foi reiniciado horas depois, usar o boot
    /// como âncora faria o app dizer "não estava rodando" num congelamento em
    /// que ele estava — e jogaria fora a janela que interessa.
    /// </para>
    ///
    /// <para>
    /// Quando o Windows não deixou o instante do desligamento, cai no boot e o
    /// comportamento é o conservador: só há retrospectiva se o log for recente
    /// em relação a ele.
    /// </para>
    ///
    /// <para>
    /// Devolve uma retrospectiva por travamento, na mesma ordem em que os
    /// travamentos chegaram.
    /// </para>
    /// </summary>
    public static List<RetrospectivaTravamento> Correlacionar(
        List<TravamentoInfo> travamentos, List<AmostraSensor> amostras)
    {
        var resultado = new List<RetrospectivaTravamento>();
        var ordenadas = amostras.OrderBy(a => a.Momento).ToList();

        foreach (var travamento in travamentos)
        {
            // A hora do congelamento (quando o Windows registrou) ou, na falta
            // dela, o boot.
            var ancora = travamento.InstanteDesligamento ?? travamento.Data;

            // O log pode ter uma amostra já durante a falha (a máquina morre
            // depois de gravar): a margem cobre isso.
            var candidatas = ordenadas.Where(a => a.Momento <= ancora + Margem).ToList();

            if (candidatas.Count == 0)
            {
                resultado.Add(new RetrospectivaTravamento
                {
                    Data = travamento.Data,
                    TemDados = false,
                    Resumo = "o app não estava rodando na hora do travamento"
                });
                continue;
            }

            // A amostra do crash é a mais recente antes do congelamento.
            var amostra = candidatas[candidatas.Count - 1];

            if (ancora - amostra.Momento > Tolerancia)
            {
                resultado.Add(new RetrospectivaTravamento
                {
                    Data = travamento.Data,
                    TemDados = false,
                    Resumo = "o app não estava rodando na hora do travamento"
                });
                continue;
            }

            // A janela: os 2 minutos que antecederam o crash.
            var janela = candidatas.Where(a => a.Momento >= amostra.Momento - Janela).ToList();

            resultado.Add(new RetrospectivaTravamento
            {
                Data = travamento.Data,
                TemDados = true,
                Resumo = ResumoAmostras.Descrever(janela)
            });
        }

        return resultado;
    }
}
