using System.Globalization;

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

    /// <summary>Números no padrão brasileiro (vírgula decimal).</summary>
    private static readonly CultureInfo Br = CultureBrasil();

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
                Resumo = MontarResumo(janela)
            });
        }

        return resultado;
    }

    /// <summary>
    /// Monta a frase da retrospectiva com os números da janela. Sensor que
    /// nunca apareceu simplesmente não entra — o resumo não inventa valor.
    /// </summary>
    private static string MontarResumo(List<AmostraSensor> janela)
    {
        var itens = new List<string>();

        var tempCpu = Maximo(janela, a => a.CpuTemp);
        if (tempCpu != null) itens.Add($"CPU a {Numero(tempCpu.Value)} °C");

        var tempGpu = Maximo(janela, a => a.GpuTemp);
        if (tempGpu != null) itens.Add($"GPU a {Numero(tempGpu.Value)} °C");

        var cargaCpu = Media(janela, a => a.CpuCarga);
        if (cargaCpu != null) itens.Add($"carga da CPU {Numero(cargaCpu.Value)} %");

        var cargaGpu = Maximo(janela, a => a.GpuCarga);
        if (cargaGpu != null) itens.Add($"GPU {Numero(cargaGpu.Value)} %");

        var ram = Ultimo(janela, a => a.RamUso);
        if (ram != null) itens.Add($"RAM {Numero(ram.Value)} %");

        var leitura = Ultimo(janela, a => a.DiscoLeitura);
        var escrita = Ultimo(janela, a => a.DiscoEscrita);
        if (leitura != null || escrita != null)
            itens.Add($"disco {Numero(Math.Max(leitura ?? 0, escrita ?? 0))} MB/s");

        var clock = Ultimo(janela, a => a.CpuClock);
        if (clock != null) itens.Add($"clock da CPU {Numero(clock.Value / 1000f)} GHz");

        return string.Join(" · ", itens);
    }

    /// <summary>O maior valor da janela (null quando o sensor nunca apareceu).</summary>
    private static float? Maximo(List<AmostraSensor> janela, Func<AmostraSensor, float?> seletor)
    {
        var valores = janela.Select(seletor).Where(v => v != null).Select(v => v!.Value).ToList();
        return valores.Count > 0 ? valores.Max() : null;
    }

    /// <summary>A média da janela (null quando o sensor nunca apareceu).</summary>
    private static float? Media(List<AmostraSensor> janela, Func<AmostraSensor, float?> seletor)
    {
        var valores = janela.Select(seletor).Where(v => v != null).Select(v => v!.Value).ToList();
        return valores.Count > 0 ? valores.Average() : null;
    }

    /// <summary>O último valor da janela (null quando o sensor nunca apareceu).</summary>
    private static float? Ultimo(List<AmostraSensor> janela, Func<AmostraSensor, float?> seletor)
    {
        for (int i = janela.Count - 1; i >= 0; i--)
        {
            var valor = seletor(janela[i]);
            if (valor != null) return valor;
        }
        return null;
    }

    /// <summary>Número no padrão brasileiro (vírgula decimal).</summary>
    private static string Numero(float valor) => valor.ToString("0.##", Br);

    /// <summary>
    /// A cultura brasileira. Se o Windows vier sem ela (modo invariante),
    /// o texto sai com ponto decimal em vez de quebrar a tela.
    /// </summary>
    private static CultureInfo CultureBrasil()
    {
        try { return CultureInfo.GetCultureInfo("pt-BR"); }
        catch { return CultureInfo.InvariantCulture; }
    }
}
