using System.Globalization;

namespace TranalSentinel;

/// <summary>
/// Quanto um programa enviou e recebeu pela rede.
///
/// <para>
/// O nome é o do programa (sem o caminho), e o mesmo nome pode ser de vários
/// processos ao mesmo tempo — o Chrome abre um processo por aba. Por isso a
/// soma por nome acontece no ranking, e não aqui.
/// </para>
/// </summary>
/// <param name="Processo">O nome do programa.</param>
/// <param name="Enviados">Bytes enviados pela rede.</param>
/// <param name="Recebidos">Bytes recebidos pela rede.</param>
public sealed record TrafegoPrograma(string Processo, long Enviados, long Recebidos)
{
    /// <summary>Enviados e recebidos juntos.</summary>
    public long Total => Enviados + Recebidos;
}

/// <summary>
/// Uma linha do ranking do tráfego (o que a tabela da tela mostra).
/// </summary>
/// <param name="Processo">O nome do programa.</param>
/// <param name="Total">O total, já em unidade legível.</param>
/// <param name="Detalhe">Quanto entrou e quanto saiu.</param>
public sealed record TrafegoLinha(string Processo, string Total, string Detalhe);

/// <summary>
/// O cartão do tráfego, com os textos prontos para a tela.
///
/// <para>
/// O formato é o mesmo dos outros cartões do app (<c>CardTemplate</c> do
/// <c>Theme.xaml</c>): título, meta, selo, reasons e métricas. A tela só
/// mostra — quem escreve o texto é este núcleo, para o cartão e o relatório
/// falarem do mesmo número do mesmo jeito.
/// </para>
/// </summary>
/// <param name="Title">O título do cartão.</param>
/// <param name="Meta">A linha underneath do título (de onde veio o número).</param>
/// <param name="VerdictText">O selo do canto (o total, ou o aviso).</param>
/// <param name="IsAlert">O selo é de alerta (o número não é confiável).</param>
/// <param name="Reasons">A explicação do que o número significa.</param>
/// <param name="Metrics">As métricas em pares (rótulo e valor).</param>
public sealed record TrafegoCartao(
    string Title,
    string Meta,
    string VerdictText,
    bool IsAlert,
    string Reasons,
    IReadOnlyList<KeyValuePair<string, string>> Metrics);

/// <summary>
/// O relatório do tráfego de um dia: o cartão e o ranking.
///
/// <para>
/// Os dois saem da mesma conta. A tela não monta o ranking por um caminho e
/// o cartão por outro — é assim que a tela passa a mostrar um total que não
/// bate com a lista logo abaixo dele.
/// </para>
/// </summary>
/// <param name="Cartao">O cartão pronto para a tela.</param>
/// <param name="Ranking">Os programas que mais usaram a rede.</param>
public sealed record TrafegoRelatorio(TrafegoCartao Cartao, IReadOnlyList<TrafegoPrograma> Ranking);

/// <summary>
/// As regras do cartão TRANAL REDE: o ranking por programa e os textos.
///
/// <para>
/// A regra mora aqui, e não na tela nem no coletor. A tela, o histórico e a
/// linha de comando precisam falar do mesmo número do mesmo jeito — e a
/// formatação não pode depender do Windows: o mesmo texto sai em pt-BR no
/// PC do dono e na máquina de integração (que roda em en-US).
/// </para>
///
/// <para>
/// O número do tráfego é o que a ETW do Windows mede: os bytes dos pacotes,
/// com os cabeçalhos TCP/IP dentro. Medido em 09/11/2026 no PC do dono, 100 MB
/// baixados por HTTPS apareceram como 105.115.183 bytes (+0,25%) — os
/// cabeçalhos são tráfego de verdade, e a diferença é dita na tela em vez de
/// ser escondida.
/// </para>
/// </summary>
public static class TrafegoRegras
{
    /// <summary>Quantos programas o cartão mostra.</summary>
    public const int MaximoNoRanking = 10;

    /// <summary>
    /// A diferença medida entre o que foi baixado e o que a ETW contou.
    /// É o cabeçalho TCP/IP, que também viaja pela rede.
    /// </summary>
    public const string NotaDaMedicao =
        "O total inclui os cabeçalhos TCP/IP: em 100 MB baixados a medição deu 105.115.183 bytes (+0,25%, medido em 09/11/2026).";

    /// <summary>Número no padrão brasileiro (vírgula decimal), sem casas demais.</summary>
    public static string Numero(double valor) =>
        valor.ToString("0.##", CultureInfo.InvariantCulture).Replace('.', ',');

    /// <summary>
    /// Uma quantidade de bytes em unidade legível (B, KB, MB, GB, TB).
    ///
    /// <para>
    /// As unidades são de 1024 (o que o Windows usa). Zero é "0 B" — e um
    /// número que não existe é "—", que é outra coisa: a tela precisa
    /// distinguir "não teve tráfego" de "não deu para medir".
    /// </para>
    /// </summary>
    public static string TextoDosBytes(long bytes)
    {
        if (bytes < 0) return "—";

        if (bytes < 1024) return bytes.ToString(CultureInfo.InvariantCulture) + " B";

        string[] unidades = { "KB", "MB", "GB", "TB", "PB" };
        double valor = bytes;
        int i = -1;
        while (valor >= 1024 && i < unidades.Length - 1)
        {
            valor /= 1024;
            i++;
        }

        return Numero(Math.Round(valor, 1)) + " " + unidades[i];
    }

    /// <summary>
    /// Os totais de um conjunto de programas: quanto entrou, quanto saiu e
    /// quanto foi o total.
    /// </summary>
    /// <param name="Itens">Os programas medidos.</param>
    /// <param name="Enviados">O total enviado.</param>
    /// <param name="Recebidos">O total recebido.</param>
    /// <param name="Total">Enviados e recebidos juntos.</param>
    public static (long Enviados, long Recebidos, long Total) Totais(IEnumerable<TrafegoPrograma> itens)
    {
        long enviados = 0;
        long recebidos = 0;

        foreach (var par in Agrupar(itens).Values)
        {
            enviados += par.Enviados;
            recebidos += par.Recebidos;
        }

        return (enviados, recebidos, enviados + recebidos);
    }

    /// <summary>
    /// O ranking: os programas que mais usaram a rede, do maior para o menor.
    ///
    /// <para>
    /// Os processos do mesmo programa são somados pelo nome (o Chrome abre um
    /// processo por aba). Quem não trafficou sai da lista: um programa com
    /// zero byte não é "o 5º que mais usou a rede" — não usou.
    /// </para>
    /// </summary>
    public static IReadOnlyList<TrafegoPrograma> Ranking(IEnumerable<TrafegoPrograma> itens, int limite)
    {
        var lista = Agrupar(itens)
            .Where(p => p.Value.Enviados + p.Value.Recebidos > 0)
            .Select(p => new TrafegoPrograma(p.Value.Nome, p.Value.Enviados, p.Value.Recebidos))
            .OrderByDescending(p => p.Total)
            // Desempate pelo nome: sem isso, dois programas com o mesmo total
            // trocavam de lugar a cada leitura, e a lista "pula" na tela.
            .ThenBy(p => p.Processo, StringComparer.OrdinalIgnoreCase)
            .ToList();

        int teto = limite <= 0 ? MaximoNoRanking : limite;
        return lista.Count > teto ? lista.Take(teto).ToList() : lista;
    }

    /// <summary>
    /// Soma os processos pelo nome do programa.
    ///
    /// <para>
    /// A comparação do nome ignora maiúsculas e minúsculas: o Windows não
    /// garante que dois executáveis com o mesmo nome usem a mesma grafia, e
    /// o "Chrome" e o "chrome" são o mesmo programa para quem lê a tela.
    /// O nome que aparece é o primeiro que apareceu.
    /// </para>
    /// </summary>
    private static Dictionary<string, (string Nome, long Enviados, long Recebidos)> Agrupar(
        IEnumerable<TrafegoPrograma> itens)
    {
        var somados = new Dictionary<string, (string Nome, long Enviados, long Recebidos)>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in itens)
        {
            string nome = string.IsNullOrWhiteSpace(item.Processo) ? "(sem nome)" : item.Processo.Trim();

            var atual = somados.TryGetValue(nome, out var v) ? v : (Nome: nome, Enviados: 0L, Recebidos: 0L);
            somados[nome] = (atual.Nome,
                atual.Enviados + Math.Max(0, item.Enviados),
                atual.Recebidos + Math.Max(0, item.Recebidos));
        }

        return somados;
    }

    /// <summary>
    /// O texto do selo do cartão: o total do programa.
    /// </summary>
    public static string TextoDoTotal(TrafegoPrograma item) => TextoDosBytes(item.Total);

    /// <summary>
    /// A linha de detalhe do cartão: quanto entrou e quanto saiu.
    /// </summary>
    public static string TextoDoDetalhe(TrafegoPrograma item) =>
        $"{TextoDosBytes(item.Recebidos)} recebidos · {TextoDosBytes(item.Enviados)} enviados";

    /// <summary>
    /// O resumo do topo do cartão: quanto foi usada a rede no todo.
    /// </summary>
    public static string TextoDoResumo(IReadOnlyList<TrafegoPrograma> itens) =>
        TextoDosBytes(Totais(itens).Total);

    /// <summary>
    /// O rótulo do dia: "HOJE", "ONTEM" ou a data.
    ///
    /// <para>
    /// A regra mora em <see cref="DiaTexto"/> — o TRANAL USO tem o mesmo
    /// seletor de dia, e as duas páginas precisam falar do mesmo dia do
    /// mesmo jeito.
    /// </para>
    /// </summary>
    public static string RotuloDoDia(DateTime dia, DateTime hoje) => DiaTexto.RotuloMaiusculo(dia, hoje);

    /// <summary>
    /// As linhas do ranking, com os textos prontos.
    /// </summary>
    public static IReadOnlyList<TrafegoLinha> Linhas(IReadOnlyList<TrafegoPrograma> ranking) =>
        ranking.Select(p => new TrafegoLinha(p.Processo, TextoDoTotal(p), TextoDoDetalhe(p))).ToList();

    /// <summary>
    /// Monta o cartão e o ranking do dia.
    ///
    /// <para>
    /// <paramref name="leituraCompleta"/> diz se os números vieram inteiros.
    /// Quando não vieram, o cartão não mostra número nenhum: um total pela
    /// metade é pior do que um cartão que diz "não deu para ler" — é a mesma
    /// regra que vale para o histórico de uso e para o banco.
    /// </para>
    ///
    /// <para>
    /// <paramref name="erro"/> é o motivo de a medição não estar acontecendo
    /// (falta de administrador, outra ferramenta usando o log do kernel). O
    /// cartão diz o motivo em vez de mostrar um zero que parece "não teve
    /// tráfego".
    /// </para>
    ///
    /// <para>
    /// <paramref name="eventosPerdidos"/> é o número de pacotes que a coleta
    /// deixou de fora (a fila do log do kernel encheu). Quando isso acontece o
    /// total é menor do que foi o real — e o cartão avisa.
    /// </para>
    /// </summary>
    /// <param name="numeros">Os programas medidos no dia.</param>
    /// <param name="dia">O dia do relatório.</param>
    /// <param name="hoje">O dia de hoje (para o rótulo do dia).</param>
    /// <param name="agora">A hora do relatório (para a meta do cartão).</param>
    /// <param name="leituraCompleta">Se os números vieram inteiros.</param>
    /// <param name="erro">O motivo de a medição não estar acontecendo (null quando está).</param>
    /// <param name="eventosPerdidos">Quantos pacotes a coleta deixou de fora (-1 quando não deu para contar).</param>
    /// <param name="aviso">Um aviso honesto sobre a conta (por exemplo, que o total é só o medido depois que o app subiu).</param>
    public static TrafegoRelatorio Relatorio(
        IReadOnlyList<TrafegoPrograma> numeros,
        DateTime dia,
        DateTime hoje,
        DateTime agora,
        bool leituraCompleta,
        string? erro,
        int eventosPerdidos,
        string? aviso = null)
    {
        string titulo = "TRÁFEGO DE " + RotuloDoDia(dia, hoje);

        string meta = dia.Date == hoje.Date
            ? "Medido pelo Windows · até " + agora.ToString("HH:mm:ss", CultureInfo.InvariantCulture)
            : "Histórico guardado neste computador · " + dia.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

        if (!leituraCompleta)
        {
            return new TrafegoRelatorio(
                new TrafegoCartao(
                    titulo,
                    meta,
                    "NÃO DEU PARA LER",
                    true,
                    "O histórico de tráfego deste dia não pôde ser lido inteiro — o cartão BANCO DE DADOS DO HISTÓRICO, na aba Diagnóstico, diz o que houve. Nenhum número é inventado para tapar o buraco.",
                    MetricasDesconhecidas()),
                new List<TrafegoPrograma>());
        }

        var ranking = Ranking(numeros, MaximoNoRanking);
        var (enviados, recebidos, total) = Totais(numeros);

        bool semMedicao = !string.IsNullOrEmpty(erro);
        bool semNada = total <= 0;

        // -1 é "não deu para contar" — e é diferente de zero (nenhum pacote
        // descartado). O cartão diz que não deu, em vez de mostrar zero.
        bool perdidosDesconhecidos = eventosPerdidos < 0;
        int perdidos = Math.Max(0, eventosPerdidos);

        string selo = semMedicao && semNada
            ? "SEM MEDIÇÃO"
            : TextoDosBytes(total);

        string reasons = MontarReasons(ranking, total, erro, perdidos, perdidosDesconhecidos, semNada, agora, aviso);

        var metricas = new List<KeyValuePair<string, string>>
        {
            new("TOTAL", TextoDosBytes(total)),
            new("RECEBIDO", TextoDosBytes(recebidos)),
            new("ENVIADO", TextoDosBytes(enviados)),
            new("PROGRAMAS", ranking.Count > 0
                ? Numero(ranking.Count) + (ranking.Count == 1 ? " programa" : " programas")
                : "0 programas"),
        };

        if (perdidos > 0)
            metricas.Add(new KeyValuePair<string, string>("PACOTES DEIXADOS DE FORA", Numero(perdidos)));

        bool alerta = semMedicao || perdidos > 0 || perdidosDesconhecidos;

        return new TrafegoRelatorio(
            new TrafegoCartao(titulo, meta, selo, alerta, reasons, metricas),
            ranking);
    }

    /// <summary>
    /// A explicação do cartão: quem usou a rede, quanto, e o que tirou a
    /// confiança do número.
    /// </summary>
    private static string MontarReasons(
        IReadOnlyList<TrafegoPrograma> ranking,
        long total,
        string? erro,
        int perdidos,
        bool perdidosDesconhecidos,
        bool semNada,
        DateTime agora,
        string? aviso)
    {
        var partes = new List<string>();

        if (!string.IsNullOrEmpty(erro))
            partes.Add(erro);

        if (!string.IsNullOrEmpty(aviso))
            partes.Add(aviso);

        if (semNada)
        {
            partes.Add("Nenhum programa registrou tráfego de internet desde que o app começou a medir" +
                $" (hoje é {agora.ToString("HH:mm", CultureInfo.InvariantCulture)}). O app só mede enquanto está rodando.");
        }
        else
        {
            partes.Add(DescribeOsPrimeiros(ranking, total));
            partes.Add(NotaDaMedicao);
        }

        if (perdidos > 0)
        {
            partes.Add($"A coleta deixou de fora {Numero(perdidos)} pacotes (a fila do log do kernel encheu): " +
                "o total é menor do que foi o real.");
        }
        else if (perdidosDesconhecidos)
        {
            partes.Add("Não deu para contar os pacotes que a coleta deixou de fora nesse dia: " +
                "o total pode ser menor do que foi o real.");
        }

        return string.Join(" ", partes);
    }

    /// <summary>
    /// A frase dos programas que mais usaram a rede (os três primeiros).
    /// </summary>
    private static string DescribeOsPrimeiros(IReadOnlyList<TrafegoPrograma> ranking, long total)
    {
        if (ranking.Count == 0) return "Nenhum programa registrou tráfego.";

        int quantos = Math.Min(3, ranking.Count);

        var partes = new List<string>();
        for (int i = 0; i < quantos; i++)
        {
            var item = ranking[i];
            int parte = total > 0 ? (int)Math.Round(100.0 * item.Total / total) : 0;
            partes.Add($"{item.Processo} usou {TextoDosBytes(item.Total)} ({parte}% do total)");
        }

        string frase = string.Join(", ", partes);

        if (ranking.Count > quantos)
        {
            int extras = ranking.Count - quantos;
            frase += $". Além deles, mais {Numero(extras)} {(extras == 1 ? "programa" : "programas")} usaram a rede.";
        }

        return frase + ".";
    }

    /// <summary>
    /// As métricas de um cartão sem número: tudo em "—".
    /// </summary>
    private static IReadOnlyList<KeyValuePair<string, string>> MetricasDesconhecidas() =>
        new List<KeyValuePair<string, string>>
        {
            new("TOTAL", "—"),
            new("RECEBIDO", "—"),
            new("ENVIADO", "—"),
            new("PROGRAMAS", "—"),
        };
}
