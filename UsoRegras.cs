namespace TranalSentinel;

/// <summary>
/// Um período de uso: um programa ficou em frente da tela de tal hora até
/// tal hora.
/// </summary>
/// <param name="Inicio">Quando o programa passou para a frente.</param>
/// <param name="Fim">Quando outro programa passou para a frente (ou "agora").</param>
/// <param name="Processo">O nome do processo.</param>
/// <param name="Titulo">O título da janela.</param>
public sealed record SessaoUso(DateTime Inicio, DateTime Fim, string Processo, string Titulo)
{
    /// <summary>Quanto tempo durou a sessão.</summary>
    public TimeSpan Duracao => Fim - Inicio;
}

/// <summary>Quanto tempo um programa ficou em frente, e que parte do dia isso foi.</summary>
public sealed record UsoPorApp(string Processo, TimeSpan Tempo, double Percentual);

/// <summary>O relatório de uso do dia.</summary>
public sealed record RelatorioUso
{
    /// <summary>Os programas, do que mais ficou em frente para o menos.</summary>
    public List<UsoPorApp> PorApp { get; init; } = new();

    /// <summary>As sessões mais longas.</summary>
    public List<SessaoUso> MaioresSessoes { get; init; } = new();

    /// <summary>O programa que está em frente agora (vazio quando não há histórico).</summary>
    public string EmDestaque { get; init; } = "";

    /// <summary>O título da janela em foco (vazio quando não há).</summary>
    public string EmDestaqueTitulo { get; init; } = "";

    /// <summary>Quanto tempo o app ficou observing a tela, no total.</summary>
    public TimeSpan Total { get; init; }

    /// <summary>
    /// O programa que estava em frente quando a CPU ficou mais quente — a
    /// resposta para "quem fez o calor". Vazio quando não há histórico de
    /// sensores para comparar.
    /// </summary>
    public string QuemFezOCalor { get; init; } = "";

    /// <summary>A temperatura máxima da CPU no período (null quando não há histórico).</summary>
    public float? TempMaximaCpu { get; init; }
}

/// <summary>A explicação de por que um alerta disparou.</summary>
public sealed record ExplicacaoAlerta(bool TemDados, string Resumo);

/// <summary>
/// A regra do histórico de uso: quanto tempo cada programa ficou em frente,
/// quais foram as sessões mais longas e quem estava em frente quando a
/// máquina ficou quente.
///
/// <para>
/// É função pura: entra a lista de trocas e a de amostras, sai o relatório.
/// Nenhum arquivo é lido aqui — quem lê é a coleta.
/// </para>
/// </summary>
public static class UsoRegras
{
    /// <summary>Quanto tempo antes do momento a janela olha (para o alerta).</summary>
    private static readonly TimeSpan Janela = TimeSpan.FromMinutes(2);

    /// <summary>Quantas sessões longas o relatório mostra.</summary>
    public const int MaioresSessoesMostradas = 5;

    /// <summary>
    /// Monta as sessões: cada troca começa uma sessão que dura até a
    /// próxima troca (a última dura até <paramref name="agora"/>).
    ///
    /// <para>
    /// As trocas são ordenadas pelo horário antes de virarem sessão: o
    /// histórico é um arquivo de acréscimo e o relógio pode ter andado para
    /// trás. Sessão com duração zero ou negativa é descartada — duas
    /// trocas no mesmo instante não são "tempo em que o app ficou aberto".
    /// </para>
    /// </summary>
    public static List<SessaoUso> MontarSessoes(IReadOnlyList<TrocaUso> trocas, DateTime agora)
    {
        var sessoes = new List<SessaoUso>();
        if (trocas == null || trocas.Count == 0) return sessoes;

        var ordenadas = trocas.Where(t => t != null).OrderBy(t => t.Momento).ToList();

        for (int i = 0; i < ordenadas.Count; i++)
        {
            DateTime inicio = ordenadas[i].Momento;
            DateTime fim = i + 1 < ordenadas.Count ? ordenadas[i + 1].Momento : agora;

            if (fim <= inicio) continue;

            sessoes.Add(new SessaoUso(inicio, fim, ordenadas[i].Processo, ordenadas[i].Titulo));
        }

        return sessoes;
    }

    /// <summary>
    /// O relatório de uso: o ranking, as sessões mais longas, o que está em
    /// frente e quem estava em frente quando a CPU ficou mais quente.
    /// </summary>
    public static RelatorioUso Calcular(IReadOnlyList<TrocaUso> trocas, DateTime agora, IReadOnlyList<AmostraSensor> amostras)
    {
        var sessoes = MontarSessoes(trocas, agora);

        var porApp = sessoes
            .GroupBy(s => s.Processo, StringComparer.OrdinalIgnoreCase)
            .Select(g => new { Processo = g.Key, Tempo = TimeSpan.FromTicks(g.Sum(s => s.Duracao.Ticks)) })
            .OrderByDescending(g => g.Tempo)
            .ThenBy(g => g.Processo, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var total = porApp.Aggregate(TimeSpan.Zero, (acc, g) => acc + g.Tempo);

        var ranking = porApp
            .Select(g => new UsoPorApp(
                g.Processo,
                g.Tempo,
                total > TimeSpan.Zero ? g.Tempo.TotalSeconds / total.TotalSeconds * 100.0 : 0))
            .ToList();

        var maiores = sessoes
            .OrderByDescending(s => s.Duracao)
            .Take(MaioresSessoesMostradas)
            .ToList();

        // O que está em frente agora: a última troca registrada.
        TrocaUso? ultima = trocas == null || trocas.Count == 0
            ? null
            : trocas.OrderBy(t => t.Momento).LastOrDefault();

        var relatorio = new RelatorioUso
        {
            PorApp = ranking,
            MaioresSessoes = maiores,
            EmDestaque = ultima?.Processo ?? "",
            EmDestaqueTitulo = ultima?.Titulo ?? "",
            Total = total
        };

        // Quem estava em frente quando a CPU ficou mais quente.
        if (amostras != null && amostras.Count > 0)
        {
            var amostraQuente = amostras
                .Where(a => a.CpuTemp != null)
                .OrderByDescending(a => a.CpuTemp!.Value)
                .FirstOrDefault();

            if (amostraQuente != null)
            {
                relatorio = relatorio with
                {
                    TempMaximaCpu = amostraQuente.CpuTemp,
                    QuemFezOCalor = ProgramaEm(amostraQuente.Momento, trocas)
                };
            }
        }

        return relatorio;
    }

    /// <summary>
    /// Explica um alerta: o que os sensores faziam nos dois minutos antes e
    /// qual programa estava em frente na hora.
    /// </summary>
    public static ExplicacaoAlerta ExplicarAlerta(DateTime momento, IReadOnlyList<AmostraSensor> amostras, IReadOnlyList<TrocaUso> trocas)
    {
        var janela = new List<AmostraSensor>();

        if (amostras != null)
        {
            janela = amostras
                .Where(a => a.Momento <= momento && a.Momento >= momento - Janela)
                .OrderBy(a => a.Momento)
                .ToList();
        }

        string programa = ProgramaEm(momento, trocas);

        if (janela.Count == 0)
        {
            return new ExplicacaoAlerta(false,
                programa.Length > 0
                    ? $"o app não estava gravando sensores nesse momento ({programa} estava em frente)"
                    : "o app não estava gravando sensores nesse momento");
        }

        string sensores = ResumoAmostras.Descrever(janela);
        string comPrograma = programa.Length > 0 ? $", com {programa} em frente" : "";

        return new ExplicacaoAlerta(true,
            sensores.Length > 0
                ? $"{sensores}{comPrograma}"
                : $"sem número de sensor na janela, mas {programa} estava em frente");
    }

    /// <summary>
    /// O programa que estava em frente no momento (a última troca antes
    /// dele). Vazio quando não há histórico.
    /// </summary>
    public static string ProgramaEm(DateTime momento, IReadOnlyList<TrocaUso>? trocas)
    {
        if (trocas == null || trocas.Count == 0) return "";

        TrocaUso? ultima = trocas
            .Where(t => t.Momento <= momento)
            .OrderBy(t => t.Momento)
            .LastOrDefault();

        return ultima?.Processo ?? "";
    }
}
