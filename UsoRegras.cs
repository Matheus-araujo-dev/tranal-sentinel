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

    /// <summary>Quanto tempo o app ficou observando a tela, no total.</summary>
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
/// O período que se pode contar para um dia: da primeira evidência de que o
/// app rodava à última.
/// </summary>
/// <param name="Inicio">A primeira evidência do dia.</param>
/// <param name="Fim">A última evidência do dia.</param>
public sealed record PeriodoUso(DateTime Inicio, DateTime Fim);

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
    public static readonly TimeSpan Janela = TimeSpan.FromMinutes(2);

    /// <summary>Quantas sessões longas o relatório mostra.</summary>
    public const int MaioresSessoesMostradas = 5;

    /// <summary>
    /// Monta as sessões: cada troca começa uma sessão que dura até a
    /// próxima troca (a última dura até <paramref name="fim"/>).
    ///
    /// <para>
    /// A sessão é <b>cortada pelo período do dia</b>: uma sessão que começou
    /// antes da meia-noite e continua depois conta só a parte do dia. É o
    /// que impede duas coisas — o tempo de ontem virar tempo de hoje, e a
    /// noite inteira ficar sem dono quando o app rodou a noite toda sem
    /// trocar de programa.
    /// </para>
    ///
    /// <para>
    /// As trocas são ordenadas pelo horário antes de virarem sessão: o
    /// histórico é um arquivo de acréscimo e o relógio pode ter andado para
    /// trás. Sessão com duração zero ou negativa é descartada — duas
    /// trocas no mesmo instante não são "tempo em que o app ficou aberto".
    /// </para>
    /// </summary>
    public static List<SessaoUso> MontarSessoes(IReadOnlyList<TrocaUso> trocas, DateTime inicio, DateTime fim)
    {
        var sessoes = new List<SessaoUso>();
        if (trocas == null || trocas.Count == 0) return sessoes;

        var ordenadas = trocas.Where(t => t != null).OrderBy(t => t.Momento).ToList();

        for (int i = 0; i < ordenadas.Count; i++)
        {
            DateTime comeco = ordenadas[i].Momento;
            DateTime ultimoInstante = i + 1 < ordenadas.Count ? ordenadas[i + 1].Momento : fim;

            // Corta pelo período do dia.
            if (comeco < inicio) comeco = inicio;
            if (ultimoInstante > fim) ultimoInstante = fim;

            if (ultimoInstante <= comeco) continue;

            sessoes.Add(new SessaoUso(comeco, ultimoInstante, ordenadas[i].Processo, ordenadas[i].Titulo));
        }

        return sessoes;
    }

    /// <summary>
    /// O relatório de uso: o ranking, as sessões mais longas, o que está em
    /// frente e quem estava em frente quando a CPU ficou mais quente.
    /// </summary>
    public static RelatorioUso Calcular(IReadOnlyList<TrocaUso> trocas, DateTime inicio, DateTime fim, IReadOnlyList<AmostraSensor> amostras)
    {
        var sessoes = MontarSessoes(trocas, inicio, fim);

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
    /// O período que se pode contar para um dia: da primeira evidência de que
    /// o app rodava à última.
    ///
    /// <para>
    /// Hoje, o fim é <paramref name="agora"/>: o app está rodando. Num dia
    /// passado, é a última amostra de sensor gravada (o log escreve de 5 em
    /// 5 segundos enquanto o app roda) ou, sem ela, a última troca de uso
    /// registrada.
    /// </para>
    ///
    /// <para>
    /// O começo é a primeira evidência do dia, e isso é o que impede o tempo
    /// inflado: sem ele, a sessão que atravessa a meia-noite contaria as
    /// horas em que o app estava fechado (o PC desligado, por exemplo) como
    /// se o app tivesse estado olhando a tela a noite toda.
    /// </para>
    /// </summary>
    /// <param name="dia">O dia que a tela está mostrando.</param>
    /// <param name="agora">O momento atual.</param>
    /// <param name="amostras">As amostras de sensor do período.</param>
    /// <param name="trocas">As trocas de uso do período.</param>
    public static PeriodoUso Periodo(DateTime dia, DateTime agora, IReadOnlyList<AmostraSensor> amostras, IReadOnlyList<TrocaUso> trocas)
    {
        DateTime meiaNoite = dia.Date;

        DateTime? primeiraAmostra = PrimeiroDoDia(amostras, meiaNoite, a => a.Momento);
        DateTime? primeiraTroca = PrimeiroDoDia(trocas, meiaNoite, t => t.Momento);
        DateTime? ultimaAmostra = UltimoDoDia(amostras, meiaNoite, a => a.Momento);
        DateTime? ultimaTroca = UltimoDoDia(trocas, meiaNoite, t => t.Momento);

        DateTime? primeiroInstante = Menor(primeiraAmostra, primeiraTroca);
        DateTime? ultimoInstante = Maior(ultimaAmostra, ultimaTroca);

        // Hoje (ou um dia que ainda não aconteceu): o período vai até agora.
        if (meiaNoite >= agora.Date)
            return new PeriodoUso(primeiroInstante ?? agora, agora);

        // Dia passado sem nenhuma evidência: o app não rodou nele.
        if (!primeiroInstante.HasValue || !ultimoInstante.HasValue)
            return new PeriodoUso(meiaNoite, meiaNoite);

        return new PeriodoUso(primeiroInstante.Value, ultimoInstante.Value);
    }

    /// <summary>O menor de dois instantes (null quando nenhum existe).</summary>
    private static DateTime? Menor(DateTime? a, DateTime? b)
    {
        if (!a.HasValue) return b;
        if (!b.HasValue) return a;
        return a.Value <= b.Value ? a : b;
    }

    /// <summary>O maior de dois instantes (null quando nenhum existe).</summary>
    private static DateTime? Maior(DateTime? a, DateTime? b)
    {
        if (!a.HasValue) return b;
        if (!b.HasValue) return a;
        return a.Value >= b.Value ? a : b;
    }

    /// <summary>
    /// O instante mais recente de um dia (null quando não há item desse dia).
    /// </summary>
    private static DateTime? UltimoDoDia<T>(IReadOnlyList<T>? itens, DateTime dia, Func<T, DateTime> momento)
    {
        if (itens == null || itens.Count == 0) return null;

        DateTime? ultimo = null;
        foreach (var item in itens)
        {
            DateTime instante = momento(item);
            if (instante.Date != dia) continue;
            if (ultimo == null || instante > ultimo.Value) ultimo = instante;
        }

        return ultimo;
    }

    /// <summary>
    /// O instante mais antigo de um dia (null quando não há item desse dia).
    /// </summary>
    private static DateTime? PrimeiroDoDia<T>(IReadOnlyList<T>? itens, DateTime dia, Func<T, DateTime> momento)
    {
        if (itens == null || itens.Count == 0) return null;

        DateTime? primeiro = null;
        foreach (var item in itens)
        {
            DateTime instante = momento(item);
            if (instante.Date != dia) continue;
            if (primeiro == null || instante < primeiro.Value) primeiro = instante;
        }

        return primeiro;
    }

    /// <summary>
    /// Explica um alerta: o que os sensores faziam nos dois minutos antes e
    /// qual programa estava em frente na hora.
    ///
    /// <para>
    /// Devolve null quando o histórico não deu para ler: sem histórico
    /// lido, não há explicação — e a explicação inventada ("o app não
    /// estava gravando sensores nesse momento") seria uma afirmação falsa
    /// sobre o histórico de quem está usando o computador. A regra mora
    /// aqui, no núcleo puro: quem chama não tem como contorná-la.
    /// </para>
    /// </summary>
    /// <param name="momento">O começo do episódio do aviso (a janela olha para trás dele).</param>
    /// <param name="amostras">As amostras de sensor da janela (com o aviso de leitura completa).</param>
    /// <param name="trocas">As trocas de uso da janela (com o aviso de leitura completa).</param>
    public static ExplicacaoAlerta? ExplicarAlerta(DateTime momento, Leitura<AmostraSensor> amostras, Leitura<TrocaUso> trocas)
    {
        if (!amostras.Completa || !trocas.Completa) return null;

        return ExplicarAlerta(momento, amostras.Linhas, trocas.Linhas);
    }

    /// <summary>
    /// Explica um alerta com o histórico já lido (a explicação em si, sem o
    /// caso de "não deu para ler", que é do método de cima).
    ///
    /// <para>
    /// É interna de propósito: quem está de fora só tem a sobrecarga que
    /// recebe a <see cref="Leitura{T}"/>, e ela devolve null quando o
    /// histórico não deu para ler. Com a sobrecarga crua pública, um
    /// chamador novo podia passar listas sem o sinal <c>Completa</c> e
    /// receber a frase "o app não estava gravando sensores nesse momento"
    /// de um histórico que ninguém leu.
    /// </para>
    /// </summary>
    internal static ExplicacaoAlerta ExplicarAlerta(DateTime momento, IReadOnlyList<AmostraSensor> amostras, IReadOnlyList<TrocaUso> trocas)
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
                : programa.Length > 0
                    ? $"sem número de sensor na janela, mas {programa} estava em frente"
                    : "sem número de sensor na janela");
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

    /// <summary>
    /// A linha da tela dos alertas que diz por que o aviso disparou: a
    /// explicação, ou o aviso de que o histórico não deu para ser lido.
    /// Vazia quando não há explicação e a leitura deu certo (o aviso ainda
    /// não tem histórico — o app tenta de novo em instantes).
    ///
    /// <para>
    /// A regra mora aqui, no núcleo puro: a tela não pode mostrar a
    /// explicação inventada ("o app não estava gravando sensores nesse
    /// momento") quando o que falhou foi a LEITURA — nem ficar em silêncio,
    /// que o usuário leria como "não havia histórico".
    /// </para>
    /// </summary>
    /// <param name="explicacao">A explicação do aviso (null quando não há).</param>
    /// <param name="leituraFalhou">True quando a última leitura do histórico não deu para ser lida.</param>
    public static string TextoDoPorQueDisparou(ExplicacaoAlerta? explicacao, bool leituraFalhou)
    {
        if (explicacao != null) return "Por que disparou: " + explicacao.Resumo;

        if (leituraFalhou)
            return "Por que disparou: não deu para ler o histórico para explicar este aviso.";

        return "";
    }
}
