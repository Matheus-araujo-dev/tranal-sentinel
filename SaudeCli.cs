using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TranalSentinel;

/// <summary>O que mudou entre dois snapshots do diagnóstico de saúde.</summary>
public enum TipoDiferenca
{
    /// <summary>O achado estava no snapshot "antes" e sumiu: foi resolvido.</summary>
    Resolveu,

    /// <summary>O achado não estava no snapshot "antes" e apareceu: é novo.</summary>
    Novo,

    /// <summary>O achado continua existindo nas duas execuções.</summary>
    Persiste
}

/// <summary>
/// Uma diferença entre dois snapshots do diagnóstico de saúde.
/// </summary>
/// <param name="Tipo">Se o achado foi resolvido, apareceu ou continua existindo.</param>
/// <param name="Achado">Identificação do achado, no formato "Área · Mensagem".</param>
/// <param name="Detalhe">Severidade do achado (e a ação sugerida, quando faz sentido).</param>
public sealed record DiferencaSaude(TipoDiferenca Tipo, string Achado, string Detalhe);

/// <summary>Um contador que mudou de valor entre dois snapshots.</summary>
/// <param name="Nome">Nome do contador (ex.: "Travamentos (90 dias)").</param>
/// <param name="Antes">Valor no snapshot "antes".</param>
/// <param name="Depois">Valor no snapshot "depois".</param>
public sealed record MudancaContador(string Nome, string Antes, string Depois);

/// <summary>
/// Achado gravado no snapshot.
///
/// A severidade vai como texto (e não como número do enum) para o arquivo
/// continuar valendo mesmo se o app mudar o enum depois.
/// </summary>
public sealed class AchadoSalvo
{
    public string Severidade { get; set; } = "";
    public string Area { get; set; } = "";
    public string Mensagem { get; set; } = "";
    public string Acao { get; set; } = "";

    public AchadoSalvo() { }

    public AchadoSalvo(Achado achado)
    {
        Severidade = achado.Severidade.ToString();
        Area = achado.Area;
        Mensagem = achado.Mensagem;
        Acao = achado.Acao;
    }

    /// <summary>Volta para o modelo do app (severidade desconhecida vira Informativo).</summary>
    public Achado ParaAchado() => new()
    {
        Severidade = SaudeCli.LerSeveridade(Severidade),
        Area = Area,
        Mensagem = Mensagem,
        Acao = Acao
    };
}

/// <summary>Travamento gravado no snapshot (contador de congelamentos).</summary>
public sealed class TravamentoSalvo
{
    public DateTime Data { get; set; }
    public string Bugcheck { get; set; } = "";
    public string Detalhe { get; set; } = "";

    public TravamentoSalvo() { }

    public TravamentoSalvo(TravamentoInfo travamento)
    {
        Data = travamento.Data;
        Bugcheck = travamento.Bugcheck;
        Detalhe = travamento.Detalhe;
    }
}

/// <summary>Contador do sistema gravado no snapshot (um número que dá para comparar).</summary>
public sealed class ContadorSalvo
{
    public string Nome { get; set; } = "";
    public string Valor { get; set; } = "";

    public ContadorSalvo() { }

    public ContadorSalvo(string nome, string valor)
    {
        Nome = nome;
        Valor = valor;
    }
}

/// <summary>
/// Snapshot do diagnóstico de saúde: o JSON que o CLI grava e que o modo
/// "comparar" lê de volta.
///
/// É uma cópia do relatório com o formato garantido: o "achados" sempre existe
/// (mesmo vazio) e a severidade vai como texto. O formato não depende dos tipos
/// do modelo do app, então um snapshot antigo continua legível.
/// </summary>
public sealed class SnapshotSaude
{
    /// <summary>Versão do formato do snapshot (1 = primeira versão).</summary>
    public int VersaoFormato { get; set; } = 1;

    /// <summary>Quando o diagnóstico foi rodado.</summary>
    public DateTime GeradoEm { get; set; }

    /// <summary>Resumo em uma linha (vem do relatório).</summary>
    public string Resumo { get; set; } = "";

    /// <summary>Os achados do diagnóstico (o que a comparação olha).</summary>
    public List<AchadoSalvo> Achados { get; set; } = new();

    /// <summary>Os travamentos contados na janela de 90 dias.</summary>
    public List<TravamentoSalvo> Travamentos { get; set; } = new();

    /// <summary>Contadores do sistema (para comparar campo a campo).</summary>
    public List<ContadorSalvo> Contadores { get; set; } = new();

    /// <summary>A fotografia bruta do sistema (leitura do log de eventos, WMI e registro).</summary>
    public ColetaSaude Dados { get; set; } = new();

    /// <summary>Monta o snapshot a partir do relatório que o app já gerou.</summary>
    public static SnapshotSaude DeRelatorio(RelatorioSaude relatorio) => new()
    {
        GeradoEm = relatorio.GeradoEm,
        Resumo = relatorio.Resumo,
        Achados = relatorio.Achados.Select(a => new AchadoSalvo(a)).ToList(),
        Travamentos = relatorio.Travamentos.Select(t => new TravamentoSalvo(t)).ToList(),
        Contadores = MontarContadores(relatorio),
        Dados = relatorio.Dados
    };

    /// <summary>Contadores do sistema, no mesmo formato do relatório em texto.</summary>
    public static List<ContadorSalvo> MontarContadores(RelatorioSaude relatorio)
    {
        var dados = relatorio.Dados;

        var antivirus = !string.IsNullOrEmpty(dados.AntivirusRegistrado)
            ? dados.AntivirusRegistrado!
            : (dados.DriverAntivirus ?? "nenhum");

        var appsQueFalharam = relatorio.AppsNoBoot.Count == 0
            ? "nenhum"
            : string.Join(", ", relatorio.AppsNoBoot
                .OrderByDescending(a => a.Vezes)
                .Take(3)
                .Select(a => $"{a.Nome} ({a.Vezes}x)"));

        return new List<ContadorSalvo>
        {
            new("Travamentos (90 dias)", relatorio.Travamentos.Count.ToString()),
            new("Erros de hardware (WHEA)", dados.EventosWhea.ToString()),
            new("Telas azuis (minidump)", dados.Minidumps.ToString()),
            new("Resets de driver de vídeo", dados.ResetsDriverVideo.ToString()),
            new("Gerenciamento PCIe (ASPM)", dados.AspmAc == null ? "desconhecido" : SaudeRegras.NomeAspm(dados.AspmAc.Value)),
            new("Antivírus", $"{antivirus} · Defender: {dados.ModoDefender ?? "desconhecido"} · filtro de kernel: {dados.EstadoDriverAntivirus ?? "ausente"}"),
            new("Definições do antivírus", dados.DefinicoesAntivirusDias == null ? "—" : $"{dados.DefinicoesAntivirusDias} dias ({dados.DefinicoesAntivirusData})"),
            new("Memória do antivírus", $"{dados.AntivirusMemoriaMb} MB"),
            new("Compressão de memória", $"{dados.MemoriaCompressaoMb} MB"),
            new("Inicialização automática", $"{dados.AutostartAtivos} de {dados.AutostartTotal} ativos"),
            new("Hyper-V / VBS", dados.HypervisorAtivo ? "ativo" : "inativo"),
            new("Apps que falham junto dos travamentos", appsQueFalharam)
        };
    }

    /// <summary>Volta para o modelo do app, para a comparação usar as mesmas regras.</summary>
    public List<Achado> ParaAchados() => Achados.Select(a => a.ParaAchado()).ToList();

    /// <summary>Serializa o snapshot em JSON legível (indentado).</summary>
    public string ParaJson() => JsonSerializer.Serialize(this, SaudeCli.OpcoesJson);

    /// <summary>
    /// Lê um snapshot de volta.
    /// </summary>
    /// <exception cref="JsonException">O arquivo não é um JSON válido.</exception>
    /// <exception cref="FormatException">O JSON é válido, mas não é um snapshot do TRANAL SAÚDE.</exception>
    public static SnapshotSaude DeJson(string json)
    {
        using var documento = JsonDocument.Parse(json);

        if (documento.RootElement.ValueKind != JsonValueKind.Object)
            throw new FormatException("o arquivo não é um objeto JSON");

        bool temAchados = documento.RootElement.TryGetProperty("achados", out _);

        var snapshot = JsonSerializer.Deserialize<SnapshotSaude>(json, SaudeCli.OpcoesJson)
            ?? throw new FormatException("o arquivo está vazio");

        if (!temAchados)
            throw new FormatException("o arquivo não tem a lista \"achados\": não é um snapshot do TRANAL SAÚDE");

        return snapshot;
    }
}

/// <summary>
/// TRANAL SAÚDE na linha de comando.
///
/// É o mesmo diagnóstico da aba TRANAL SAÚDE, exposto como CLI: em vez de
/// duplicar as regras em um script PowerShell (duas implementações que divergem
/// com o tempo), o app roda o diagnóstico que já existe e grava o resultado.
///
/// Uso:
///   TranalSentinel.exe --saude coletar  [--saida &lt;pasta&gt;]
///   TranalSentinel.exe --saude comparar &lt;antes.json&gt; &lt;depois.json&gt;
///   TranalSentinel.exe --saude relatar  [--saida &lt;arquivo&gt;]
///
/// A CLI não abre nenhuma janela, não pede interação e é somente leitura: ela
/// lê o histórico do Windows e grava arquivos, nada mais.
/// </summary>
public static class SaudeCli
{
    /// <summary>Deu certo.</summary>
    public const int CodigoOk = 0;

    /// <summary>Erro de execução (arquivo ausente, JSON inválido, sem permissão...).</summary>
    public const int CodigoErro = 1;

    /// <summary>Uso incorreto da linha de comando.</summary>
    public const int CodigoUso = 2;

    /// <summary>Opções do JSON: indentado, em camelCase e sem escapar os acentos.</summary>
    internal static readonly JsonSerializerOptions OpcoesJson = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        // O modelo do app tem listas somente-leitura: sem "popular" o objeto
        // existente, elas voltariam vazias na leitura do snapshot.
        PreferredObjectCreationHandling = JsonObjectCreationHandling.Populate
    };

    /// <summary>Arquivos em UTF-8 sem BOM (o Bloco de Notas do Windows abre assim).</summary>
    private static readonly UTF8Encoding SemBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>Pasta padrão dos snapshots: %AppData%\TranalSentinel\saude.</summary>
    public static string PastaPadrao => Path.Combine(AppSettings.Folder, "saude");

    /// <summary>Texto de uso, impresso quando o comando não é reconhecido.</summary>
    public static string TextoUso => """
        TRANAL SENTINEL · TRANAL SAÚDE (linha de comando)

        Uso:
          TranalSentinel.exe --saude coletar  [--saida <pasta>]
          TranalSentinel.exe --saude comparar <antes.json> <depois.json>
          TranalSentinel.exe --saude relatar  [--saida <arquivo>]

        Modos:
          coletar   Roda o diagnóstico e grava um snapshot JSON e um relatório TXT
                    na pasta %AppData%\TranalSentinel\saude.
          comparar  Compara dois snapshots e mostra o que apareceu, o que foi
                    resolvido e o que continua existindo.
          relatar   Roda o diagnóstico e mostra o relatório em texto.

        O diagnóstico é somente leitura: nada é alterado, instalado ou reiniciado.
        """;

    /// <summary>
    /// Executa a CLI.
    /// </summary>
    /// <param name="args">Argumentos vindos da linha de comando.</param>
    /// <returns>0 (ok), 1 (erro de execução) ou 2 (uso incorreto).</returns>
    public static int Executar(string[] args)
    {
        try
        {
            PrepararConsole();

            var opcoes = Interpretar(args);
            if (opcoes == null)
            {
                Console.Out.WriteLine(TextoUso);
                return CodigoUso;
            }

            return opcoes.Modo switch
            {
                ModoCli.Coletar => ExecutarColetar(opcoes),
                ModoCli.Comparar => ExecutarComparar(opcoes),
                _ => ExecutarRelatar(opcoes)
            };
        }
        catch (Exception ex)
        {
            // A CLI nunca derruba o app: qualquer erro vira mensagem + código 1.
            Console.Error.WriteLine("Erro inesperado: " + ex.Message);
            return CodigoErro;
        }
    }

    /// <summary>
    /// Compara dois conjuntos de achados e diz o que apareceu, o que foi
    /// resolvido e o que persiste. Função pura: não lê arquivo nem toca no Windows.
    ///
    /// Como o modelo <see cref="Achado"/> não tem ID nem código, a chave de
    /// comparação é "Área + Mensagem sem números": as contagens mudam entre as
    /// execuções ("3 travamentos" vira "1 travamento"), mas o achado é o mesmo.
    /// A ação não entra na chave, porque o texto dela pode ser reescrito sem
    /// que o achado mude. A severidade também não entra: ela pode mudar
    /// (Alto para Médio) sem que o achado deixe de ser o mesmo.
    /// </summary>
    /// <param name="antes">Achados do snapshot antigo.</param>
    /// <param name="depois">Achados do snapshot novo.</param>
    /// <returns>A lista de diferenças, do mais grave para o menos grave.</returns>
    public static List<DiferencaSaude> CompararAchados(IReadOnlyList<Achado> antes, IReadOnlyList<Achado> depois)
    {
        var antesPorChave = AgruparPorChave(antes);
        var depoisPorChave = AgruparPorChave(depois);

        var itens = new List<ItemDiferenca>();

        foreach (var chave in depoisPorChave.Keys)
        {
            var listaDepois = depoisPorChave[chave];
            var listaAntes = antesPorChave.TryGetValue(chave, out var existentes) ? existentes : new List<Achado>();

            // O que sobrou no "depois" além do que já existia: achado novo.
            for (int i = listaAntes.Count; i < listaDepois.Count; i++)
                itens.Add(ItemNovo(listaDepois[i]));

            // O que existia e continua: persiste.
            int comuns = Math.Min(listaAntes.Count, listaDepois.Count);
            for (int i = 0; i < comuns; i++)
                itens.Add(ItemPersiste(listaAntes[i], listaDepois[i], listaDepois.Count));
        }

        foreach (var chave in antesPorChave.Keys)
        {
            var listaAntes = antesPorChave[chave];
            var listaDepois = depoisPorChave.TryGetValue(chave, out var existentes) ? existentes : new List<Achado>();

            // O que estava no "antes" e não está mais no "depois": foi resolvido.
            for (int i = listaDepois.Count; i < listaAntes.Count; i++)
                itens.Add(ItemResolveu(listaAntes[i]));
        }

        return itens
            .OrderBy(i => i.Tipo)
            .ThenBy(i => SaudeRegras.Ordem(i.Severidade))
            .ThenBy(i => i.Rotulo, StringComparer.CurrentCultureIgnoreCase)
            .Select(i => new DiferencaSaude(i.Tipo, i.Rotulo, i.Detalhe))
            .ToList();
    }

    /// <summary>Contadores que mudaram entre dois snapshots (a comparação "campo a campo").</summary>
    public static List<MudancaContador> CompararContadores(SnapshotSaude antes, SnapshotSaude depois)
    {
        var valoresAntes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var contador in antes.Contadores)
            valoresAntes.TryAdd(contador.Nome, contador.Valor);

        var valoresDepois = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var contador in depois.Contadores)
            valoresDepois.TryAdd(contador.Nome, contador.Valor);

        var mudancas = new List<MudancaContador>();

        // Na ordem do snapshot "depois": a saída sai sempre igual.
        foreach (var contador in depois.Contadores)
        {
            string valorAntes = valoresAntes.TryGetValue(contador.Nome, out string? anterior) && anterior != null
                ? anterior
                : "—";

            if (string.Equals(valorAntes, contador.Valor, StringComparison.Ordinal)) continue;
            mudancas.Add(new MudancaContador(contador.Nome, valorAntes, contador.Valor));
        }

        // Contador que só existia no snapshot antigo.
        foreach (var contador in antes.Contadores)
        {
            if (valoresDepois.ContainsKey(contador.Nome)) continue;
            mudancas.Add(new MudancaContador(contador.Nome, contador.Valor, "—"));
        }

        return mudancas;
    }

    /// <summary>Monta o relatório em texto (o mesmo que o CLI mostra e grava no TXT).</summary>
    public static string MontarRelatorio(RelatorioSaude relatorio)
    {
        var sb = new StringBuilder();

        sb.AppendLine("TRANAL SENTINEL · TRANAL SAÚDE");
        sb.AppendLine($"Diagnóstico do sistema · {relatorio.GeradoEm:dd/MM/yyyy HH:mm:ss}");
        sb.AppendLine(new string('=', 64));
        sb.AppendLine();
        sb.AppendLine($"RESUMO: {relatorio.Resumo}");
        sb.AppendLine();
        sb.AppendLine($"ACHADOS ({relatorio.Total})");

        if (relatorio.Total == 0)
        {
            sb.AppendLine("  Nenhum problema encontrado.");
        }
        else
        {
            int i = 1;
            foreach (var achado in relatorio.Achados)
            {
                sb.AppendLine($"  {i}. [{achado.Selo}] {Rotulo(achado)}");
                if (!string.IsNullOrWhiteSpace(achado.Acao))
                    sb.AppendLine($"     ação: {achado.Acao}");
                i++;
            }
        }

        sb.AppendLine();
        sb.AppendLine("CONTEXTO DO SISTEMA");
        foreach (var contador in SnapshotSaude.MontarContadores(relatorio))
            sb.AppendLine($"  {contador.Nome}: {contador.Valor}");

        if (relatorio.Travamentos.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("TRAVAMENTOS (90 dias)");
            foreach (var travamento in relatorio.Travamentos.Take(10))
            {
                string linha = $"  {travamento.DataFormatada} · bugcheck {travamento.Bugcheck} · {travamento.Detalhe}";

                // O que os sensores faziam antes do congelamento, quando o
                // log contínuo tem o que contar.
                if (!string.IsNullOrWhiteSpace(travamento.Retrospectiva))
                    linha += " · " + travamento.Retrospectiva;

                sb.AppendLine(linha);
            }
        }

        sb.AppendLine();
        sb.AppendLine("Diagnóstico somente leitura: nada foi alterado, instalado ou reiniciado.");

        return sb.ToString();
    }

    /// <summary>Monta o texto da comparação entre dois snapshots.</summary>
    public static string MontarComparacao(
        SnapshotSaude antes,
        SnapshotSaude depois,
        string caminhoAntes,
        string caminhoDepois,
        IReadOnlyList<DiferencaSaude> diferencas)
    {
        var sb = new StringBuilder();

        sb.AppendLine("TRANAL SENTINEL · TRANAL SAÚDE");
        sb.AppendLine("Comparação de snapshots");
        sb.AppendLine(new string('=', 64));
        sb.AppendLine();
        sb.AppendLine($"ANTES : {antes.GeradoEm:dd/MM/yyyy HH:mm:ss} · {caminhoAntes}");
        sb.AppendLine($"DEPOIS: {depois.GeradoEm:dd/MM/yyyy HH:mm:ss} · {caminhoDepois}");
        sb.AppendLine();

        var novos = diferencas.Where(d => d.Tipo == TipoDiferenca.Novo).ToList();
        var resolvidos = diferencas.Where(d => d.Tipo == TipoDiferenca.Resolveu).ToList();
        var persistes = diferencas.Where(d => d.Tipo == TipoDiferenca.Persiste).ToList();

        sb.AppendLine($"RESUMO: antes {antes.Achados.Count} achado(s) · depois {depois.Achados.Count} achado(s)");
        sb.AppendLine($"  novos: {novos.Count} · resolvidos: {resolvidos.Count} · persistentes: {persistes.Count}");
        sb.AppendLine();

        EscreverGrupo(sb, "NOVOS — apareceram", novos);
        EscreverGrupo(sb, "RESOLVIDOS — sumiram", resolvidos);
        EscreverGrupo(sb, "PERSISTENTES — continuam", persistes);

        var contadores = CompararContadores(antes, depois);
        if (contadores.Count > 0)
        {
            sb.AppendLine("CONTADORES QUE MUDARAM");
            foreach (var mudanca in contadores)
                sb.AppendLine($"  {mudanca.Nome}: {mudanca.Antes} → {mudanca.Depois}");
        }

        return sb.ToString();
    }

    /// <summary>Lê a severidade do snapshot; texto desconhecido vira Informativo.</summary>
    internal static Severidade LerSeveridade(string texto) =>
        Enum.TryParse<Severidade>(texto, true, out var severidade) ? severidade : Severidade.Informativo;

    // ------------------------------------------------------------------ modos

    private static int ExecutarColetar(OpcoesCli opcoes)
    {
        // O diagnóstico é o mesmo da aba TRANAL SAÚDE: nada de regra duplicada aqui.
        var relatorio = SaudeDiagnostico.Executar();
        var snapshot = SnapshotSaude.DeRelatorio(relatorio);

        string pasta = string.IsNullOrWhiteSpace(opcoes.Pasta) ? PastaPadrao : opcoes.Pasta!;
        string carimbo = relatorio.GeradoEm.ToString("yyyyMMdd-HHmmss");

        string arquivoJson = Path.Combine(pasta, $"saude-{carimbo}.json");
        string arquivoTxt = Path.Combine(pasta, $"saude-{carimbo}.txt");

        if (!TentarGravar(arquivoJson, snapshot.ParaJson(), out string erro))
        {
            Console.Error.WriteLine("Erro: " + erro);
            return CodigoErro;
        }

        if (!TentarGravar(arquivoTxt, MontarRelatorio(relatorio), out erro))
        {
            Console.Error.WriteLine("Erro: " + erro);
            return CodigoErro;
        }

        Console.WriteLine($"Snapshot JSON: {arquivoJson}");
        Console.WriteLine($"Relatório TXT: {arquivoTxt}");
        Console.WriteLine(relatorio.Resumo);
        return CodigoOk;
    }

    private static int ExecutarComparar(OpcoesCli opcoes)
    {
        if (!TentarLer(opcoes.Antes!, out string textoAntes, out string erro))
        {
            Console.Error.WriteLine("Erro: " + erro);
            return CodigoErro;
        }

        if (!TentarLer(opcoes.Depois!, out string textoDepois, out erro))
        {
            Console.Error.WriteLine("Erro: " + erro);
            return CodigoErro;
        }

        SnapshotSaude antes;
        try
        {
            antes = SnapshotSaude.DeJson(textoAntes);
        }
        catch (Exception ex) when (ex is JsonException or FormatException)
        {
            Console.Error.WriteLine($"Erro: \"{opcoes.Antes}\" não é um snapshot válido ({ex.Message}).");
            return CodigoErro;
        }

        SnapshotSaude depois;
        try
        {
            depois = SnapshotSaude.DeJson(textoDepois);
        }
        catch (Exception ex) when (ex is JsonException or FormatException)
        {
            Console.Error.WriteLine($"Erro: \"{opcoes.Depois}\" não é um snapshot válido ({ex.Message}).");
            return CodigoErro;
        }

        var diferencas = CompararAchados(antes.ParaAchados(), depois.ParaAchados());
        string texto = MontarComparacao(antes, depois, opcoes.Antes!, opcoes.Depois!, diferencas);

        string caminho = Path.Combine(PastaPadrao, $"saude-comparacao-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
        if (!TentarGravar(caminho, texto, out erro))
        {
            Console.Error.WriteLine("Erro: " + erro);
            return CodigoErro;
        }

        Console.WriteLine(texto);
        Console.WriteLine($"Comparação salva em: {caminho}");
        return CodigoOk;
    }

    private static int ExecutarRelatar(OpcoesCli opcoes)
    {
        var relatorio = SaudeDiagnostico.Executar();
        string texto = MontarRelatorio(relatorio);

        string caminho = string.IsNullOrWhiteSpace(opcoes.Arquivo)
            ? Path.Combine(PastaPadrao, $"saude-relatorio-{DateTime.Now:yyyyMMdd-HHmmss}.txt")
            : opcoes.Arquivo!;

        if (!TentarGravar(caminho, texto, out string erro))
        {
            Console.Error.WriteLine("Erro: " + erro);
            return CodigoErro;
        }

        Console.WriteLine(texto);
        Console.WriteLine($"Relatório salvo em: {caminho}");
        return CodigoOk;
    }

    // ------------------------------------------------------------- argumentos

    private enum ModoCli { Coletar, Comparar, Relatar }

    private sealed class OpcoesCli
    {
        public ModoCli Modo { get; init; }
        public string? Pasta { get; init; }
        public string? Arquivo { get; init; }
        public string? Antes { get; init; }
        public string? Depois { get; init; }
    }

    /// <summary>
    /// Lê os argumentos. Devolve null quando o comando não é reconhecido
    /// (aí quem chama imprime o texto de uso e devolve 2).
    /// </summary>
    private static OpcoesCli? Interpretar(string[]? args)
    {
        if (args == null || args.Length == 0) return null;

        // O "--saude" pode vir em qualquer posição: o app costuma procurar o
        // argumento em qualquer lugar da linha de comando.
        int posicao = Array.FindIndex(args, a => string.Equals(a, "--saude", StringComparison.OrdinalIgnoreCase));
        if (posicao < 0 || posicao + 1 >= args.Length) return null;

        string modo = args[posicao + 1].Trim();
        var resto = args.Skip(posicao + 2).ToArray();

        switch (modo.ToLowerInvariant())
        {
            case "coletar":
            {
                var (destino, ok) = LerSaida(resto);
                if (!ok) return null;
                return new OpcoesCli { Modo = ModoCli.Coletar, Pasta = destino };
            }

            case "relatar":
            {
                var (destino, ok) = LerSaida(resto);
                if (!ok) return null;
                return new OpcoesCli { Modo = ModoCli.Relatar, Arquivo = destino };
            }

            case "comparar":
            {
                if (resto.Length != 2) return null;
                if (resto.Any(a => a.StartsWith("--", StringComparison.Ordinal))) return null;
                if (resto.Any(string.IsNullOrWhiteSpace)) return null;
                return new OpcoesCli { Modo = ModoCli.Comparar, Antes = resto[0], Depois = resto[1] };
            }

            default:
                return null;
        }
    }

    /// <summary>Lê o "--saida" opcional (vazio quando o modo não tem).</summary>
    private static (string? Valor, bool Ok) LerSaida(string[] resto)
    {
        if (resto.Length == 0) return (null, true);
        if (resto.Length != 2) return (null, false);
        if (!string.Equals(resto[0], "--saida", StringComparison.OrdinalIgnoreCase)) return (null, false);
        if (string.IsNullOrWhiteSpace(resto[1])) return (null, false);
        return (resto[1], true);
    }

    // ---------------------------------------------------------------- arquivo

    /// <summary>Grava o arquivo, traduzindo a exceção do Windows para uma mensagem em português.</summary>
    private static bool TentarGravar(string caminho, string conteudo, out string erro)
    {
        try
        {
            string? pasta = Path.GetDirectoryName(caminho);
            if (!string.IsNullOrEmpty(pasta)) Directory.CreateDirectory(pasta);

            File.WriteAllText(caminho, conteudo, SemBom);
            erro = "";
            return true;
        }
        catch (UnauthorizedAccessException ex)
        {
            erro = $"sem permissão para gravar em \"{caminho}\" ({ex.Message})";
            return false;
        }
        catch (DirectoryNotFoundException ex)
        {
            erro = $"a pasta de \"{caminho}\" não existe e não pôde ser criada ({ex.Message})";
            return false;
        }
        catch (IOException ex)
        {
            erro = $"falha de disco ao gravar \"{caminho}\" ({ex.Message})";
            return false;
        }
    }

    /// <summary>Lê o arquivo, traduzindo a exceção do Windows para uma mensagem em português.</summary>
    private static bool TentarLer(string caminho, out string conteudo, out string erro)
    {
        conteudo = "";
        try
        {
            if (!File.Exists(caminho))
            {
                erro = $"arquivo não encontrado: \"{caminho}\"";
                return false;
            }

            conteudo = File.ReadAllText(caminho);
            erro = "";
            return true;
        }
        catch (UnauthorizedAccessException ex)
        {
            erro = $"sem permissão para ler \"{caminho}\" ({ex.Message})";
            return false;
        }
        catch (IOException ex)
        {
            erro = $"falha ao ler \"{caminho}\" ({ex.Message})";
            return false;
        }
    }

    // ------------------------------------------------------------ comparação

    /// <summary>Achado em processamento: guarda a severidade para ordenar a saída.</summary>
    private sealed record ItemDiferenca(TipoDiferenca Tipo, string Rotulo, string Detalhe, Severidade Severidade);

    private static ItemDiferenca ItemNovo(Achado achado) => new(
        TipoDiferenca.Novo,
        Rotulo(achado),
        $"severidade {achado.Selo}{ComAcao(achado)}",
        achado.Severidade);

    private static ItemDiferenca ItemResolveu(Achado achado) => new(
        TipoDiferenca.Resolveu,
        Rotulo(achado),
        $"severidade {achado.Selo}",
        achado.Severidade);

    private static ItemDiferenca ItemPersiste(Achado antes, Achado depois, int repeticoes)
    {
        string severidade = antes.Severidade == depois.Severidade
            ? $"severidade {depois.Selo}"
            : $"severidade {antes.Selo} → {depois.Selo}";

        string detalhe = repeticoes > 1
            ? $"{severidade} · {repeticoes} ocorrências"
            : severidade + ComAcao(depois);

        return new ItemDiferenca(TipoDiferenca.Persiste, Rotulo(depois), detalhe, depois.Severidade);
    }

    private static string ComAcao(Achado achado) =>
        string.IsNullOrWhiteSpace(achado.Acao) ? "" : $" · ação: {achado.Acao}";

    /// <summary>Identificação do achado: "Área · Mensagem" (sem a parte que muda).</summary>
    private static string Rotulo(Achado achado)
    {
        string area = (achado.Area ?? "").Trim();
        string mensagem = (achado.Mensagem ?? "").Trim();

        if (area.Length == 0) return mensagem;
        if (mensagem.Length == 0) return area;
        return area + " · " + mensagem;
    }

    private static Dictionary<string, List<Achado>> AgruparPorChave(IReadOnlyList<Achado> achados)
    {
        var mapa = new Dictionary<string, List<Achado>>(StringComparer.Ordinal);

        for (int i = 0; i < achados.Count; i++)
        {
            var achado = achados[i];
            if (achado == null) continue;

            string chave = ChaveDe(achado);
            if (!mapa.TryGetValue(chave, out var lista))
                mapa[chave] = lista = new List<Achado>();

            lista.Add(achado);
        }

        return mapa;
    }

    /// <summary>
    /// Chave estável do achado: "Área + Mensagem sem números".
    /// As contagens mudam entre as execuções, mas o achado é o mesmo.
    /// </summary>
    private static string ChaveDe(Achado achado)
    {
        string area = (achado.Area ?? "").Trim();
        string mensagem = SemNumeros(achado.Mensagem ?? "");
        return area.ToUpperInvariant() + "|" + mensagem.ToUpperInvariant();
    }

    /// <summary>Tira os números do texto: "3 travamentos" e "1 travamento" são o mesmo achado.</summary>
    private static string SemNumeros(string texto)
    {
        var sb = new StringBuilder(texto.Length);
        bool dentroDeNumero = false;

        foreach (char c in texto)
        {
            if (char.IsDigit(c))
            {
                if (!dentroDeNumero)
                {
                    sb.Append('#');
                    dentroDeNumero = true;
                }
                continue;
            }

            dentroDeNumero = false;
            sb.Append(c);
        }

        // Espaços repetidos não mudam o achado.
        return string.Join(" ", sb.ToString().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    // ------------------------------------------------------------- relatórios

    private static void EscreverGrupo(StringBuilder sb, string titulo, List<DiferencaSaude> diferencas)
    {
        sb.AppendLine($"{titulo} ({diferencas.Count})");

        if (diferencas.Count == 0)
        {
            sb.AppendLine("  (nenhum)");
            sb.AppendLine();
            return;
        }

        foreach (var diferenca in diferencas)
        {
            sb.AppendLine($"  - {diferenca.Achado}");
            if (!string.IsNullOrWhiteSpace(diferenca.Detalhe))
                sb.AppendLine($"    {diferenca.Detalhe}");
        }

        sb.AppendLine();
    }

    /// <summary>Deixa o console em UTF-8, para os acentos saírem certainhos (quando há console).</summary>
    private static void PrepararConsole()
    {
        try
        {
            if (!Console.IsOutputRedirected) Console.OutputEncoding = Encoding.UTF8;
        }
        catch { }
    }
}
