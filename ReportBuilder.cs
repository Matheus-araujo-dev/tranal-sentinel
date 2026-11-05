using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace TranalSentinel;

public sealed record ReportNode(string Name, string? Value, List<ReportNode>? Children);
public sealed record ReportSensor(string Hardware, string Category, string Name, string Value, string Min, string Max);
public sealed record ReportBench(DateTime Date, string Test, string Summary);
public sealed record ReportDisk(string Name, string Details, string Verdict, string Reasons, List<KeyValuePair<string, string>> Metrics);
public sealed record ReportGpu(string Name, string Subtitle, List<ReportNode> Sections);

/// <summary>Tudo que entra no relatório exportado.</summary>
public sealed class ReportData
{
    public DateTime Date { get; init; } = DateTime.Now;
    public string Machine { get; init; } = Environment.MachineName;
    public string AppVersion { get; init; } = "";
    public string? LogoBase64 { get; init; }
    public List<ReportNode> System { get; init; } = new();
    public List<ReportGpu> Gpus { get; init; } = new();
    public List<ReportDisk> Disks { get; init; } = new();
    public List<ReportDisk> Power { get; init; } = new();
    public List<ReportDisk> Network { get; init; } = new();
    public List<ReportSensor> Sensors { get; init; } = new();
    public List<ReportBench> Benchmarks { get; init; } = new();
}

/// <summary>Gera o relatório em HTML (arquivo único, abre em qualquer navegador), JSON ou CSV (planilha para o Excel em português). Números de série ficam de fora.</summary>
public static class ReportBuilder
{
    public static ReportNode? ToNode(InfoNode n)
    {
        string h = n.Header ?? "";
        // privacidade: o relatório costuma ser enviado a terceiros
        if (h.Contains("série", StringComparison.OrdinalIgnoreCase) || h.Contains("serial", StringComparison.OrdinalIgnoreCase)) return null;
        var kids = n.Children.Select(ToNode).Where(k => k != null).Cast<ReportNode>().ToList();
        return new ReportNode(h, string.IsNullOrEmpty(n.Value) ? null : n.Value, kids.Count > 0 ? kids : null);
    }

    public static string Json(ReportData d) => JsonSerializer.Serialize(new
    {
        gerado_em = d.Date.ToString("s"),
        computador = d.Machine,
        versao_do_programa = d.AppVersion,
        sistema = d.System,
        placas_de_video = d.Gpus,
        discos = d.Disks.Select(x => new
        {
            x.Name, x.Details, saude = x.Verdict, x.Reasons,
            metricas = x.Metrics.ToDictionary(m => m.Key, m => m.Value)
        }),
        energia = d.Power.Select(x => new { x.Name, x.Details, estado = x.Verdict, x.Reasons, metricas = x.Metrics.ToDictionary(m => m.Key, m => m.Value) }),
        rede = d.Network.Select(x => new { x.Name, x.Details, estado = x.Verdict, x.Reasons, metricas = x.Metrics.ToDictionary(m => m.Key, m => m.Value) }),
        sensores = d.Sensors,
        benchmarks = d.Benchmarks,
    }, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });

    /// <summary>
    /// Gera o relatório em CSV: uma planilha por bloco, pronta para o Excel em português
    /// (separador ponto e vírgula, decimal com vírgula, acentos em UTF-8 com BOM).
    /// </summary>
    /// <remarks>
    /// O texto já começa com BOM (U+FEFF) — é o que faz o Excel ler a acentuação.
    /// Grave o arquivo com <c>new UTF8Encoding(false)</c>, senão o BOM sai duplicado.
    /// </remarks>
    public static string Csv(ReportData d)
    {
        var sb = new StringBuilder(64 * 1024);
        sb.Append('\uFEFF'); // BOM: sem ele o Excel descarta a acentuação

        BlocoCsv(sb, "Relatório", new[] { "Campo", "Valor" }, new[]
        {
            new[] { "Gerado em", d.Date.ToString("dd/MM/yyyy HH:mm") },
            new[] { "Computador", d.Machine },
            new[] { "Versão do programa", d.AppVersion },
            new[] { "Observação", "Números de série foram omitidos deste relatório." },
            new[] { "Fonte", "Gerado pelo TRANAL SENTINEL. Dados lidos do Windows, do driver da placa de vídeo e dos sensores da placa-mãe; podem variar conforme o hardware." },
        });

        BlocoDiscosCsv(sb, "Saúde dos discos", d.Disks);
        BlocoDiscosCsv(sb, "Bateria e energia", d.Power);
        BlocoDiscosCsv(sb, "Teste de rede", d.Network);

        var sistema = new List<string[]>();
        ArvoreCsv(d.System, "", sistema);
        BlocoCsv(sb, "Sistema", new[] { "Seção", "Item", "Valor" }, sistema);

        foreach (var g in d.Gpus)
        {
            var linhas = new List<string[]>();
            if (g.Subtitle.Length > 0) linhas.Add(new[] { "Placa de vídeo", "Subtítulo", g.Subtitle });
            ArvoreCsv(g.Sections, "Placa de vídeo", linhas);
            BlocoCsv(sb, $"Placa de vídeo · {g.Name}", new[] { "Seção", "Item", "Valor" }, linhas);
        }

        BlocoCsv(sb, "Sensores (leitura no momento da exportação)", new[] { "Hardware", "Categoria", "Sensor", "Valor", "Mín.", "Máx." },
            d.Sensors.Select(s => new[] { s.Hardware, s.Category, s.Name, Valor(s.Value), Valor(s.Min), Valor(s.Max) }));

        BlocoCsv(sb, "Benchmarks e testes recentes", new[] { "Data", "Teste", "Resultado" },
            d.Benchmarks.Select(b => new[] { b.Date.ToString("dd/MM/yyyy HH:mm"), b.Test, Valor(b.Summary) }));

        return sb.ToString();
    }

    /// <summary>Bloco de discos (saúde, energia e rede): uma linha por disco e um bloco de métricas para cada disco.</summary>
    private static void BlocoDiscosCsv(StringBuilder sb, string titulo, List<ReportDisk> itens)
    {
        BlocoCsv(sb, titulo, new[] { "Disco", "Detalhes", "Saúde", "Motivos" },
            itens.Select(x => new[] { x.Name, Valor(x.Details), x.Verdict, Valor(x.Reasons) }));
        foreach (var item in itens)
        {
            if (item.Metrics.Count == 0) continue;
            BlocoCsv(sb, $"{titulo} · métricas · {item.Name}", new[] { "Métrica", "Valor" },
                item.Metrics.Select(m => new[] { m.Key, Valor(m.Value) }));
        }
    }

    /// <summary>Achata a árvore de informações em linhas "seção;item;valor", guardando o caminho dos grupos.</summary>
    private static void ArvoreCsv(List<ReportNode>? nodes, string caminho, List<string[]> linhas)
    {
        if (nodes == null) return;
        foreach (var n in nodes)
        {
            if (n.Children is { Count: > 0 })
            {
                if (n.Value != null) linhas.Add(new[] { caminho, n.Name, Valor(n.Value) });
                ArvoreCsv(n.Children, caminho.Length == 0 ? n.Name : $"{caminho} > {n.Name}", linhas);
            }
            else linhas.Add(new[] { caminho, n.Name, Valor(n.Value) });
        }
    }

    /// <summary>Escreve um bloco: título, cabeçalho das colunas e as linhas.</summary>
    private static void BlocoCsv(StringBuilder sb, string titulo, string[] colunas, IEnumerable<string[]> linhas)
    {
        if (sb.Length > 1) sb.Append("\r\n"); // linha em branco separa os blocos
        sb.Append(Celula(titulo)).Append("\r\n");
        sb.Append(string.Join(";", colunas.Select(Celula))).Append("\r\n");
        foreach (var linha in linhas)
            sb.Append(string.Join(";", linha.Select(Celula))).Append("\r\n");
    }

    /// <summary>
    /// Escapa a célula: ponto e vírgula, aspas e quebra de linha pedem aspas
    /// (com aspas duplicadas dentro).
    /// </summary>
    /// <remarks>
    /// Também protege contra fórmula: uma célula que começa com =, +, - ou @
    /// seria executada pelo Excel ao abrir o arquivo. O prefixo de apóstrofo
    /// faz o Excel tratar o texto como texto — e não muda o valor, porque o
    /// Excel descarta o apóstrofo na importação de CSV.
    /// </remarks>
    private static string Celula(string? s)
    {
        string v = s ?? "";
        v = NeutralizarFormula(v);
        if (v.Contains(';') || v.Contains('"') || v.Contains('\n') || v.Contains('\r'))
            return "\"" + v.Replace("\"", "\"\"") + "\"";
        return v;
    }

    /// <summary>
    /// Coloca um apóstrofo na frente de um valor que o Excel leria como
    /// fórmula. Número negativo não é fórmula — fica como está.
    /// </summary>
    private static string NeutralizarFormula(string valor)
    {
        if (valor.Length == 0) return valor;
        char c = valor[0];
        if (c is not ('=' or '+' or '-' or '@' or '\t' or '\r')) return valor;

        // "-5" ou "-5,3" é número, não fórmula.
        if (c == '-' && valor.Length > 1 && (char.IsDigit(valor[1]) || valor[1] == ',')) return valor;

        return "'" + valor;
    }

    /// <summary>
    /// Escreve o número no padrão do Excel em português: decimal com vírgula.
    /// </summary>
    /// <remarks>
    /// Só um número com <b>uma</b> casa decimal vira vírgula. Com duas ou
    /// mais, é quase sempre versão ("551.23" é a versão do driver, não uma
    /// medida) — e virar "551,23" corromperia o dado num relatório feito para
    /// ser lido por outros.
    /// </remarks>
    private static string Valor(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        // número puro: o ponto decimal vira vírgula (só com uma casa)
        if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double numero))
        {
            string texto = numero.ToString(CultureInfo.InvariantCulture);
            int ponto = texto.IndexOf('.');
            if (ponto < 0) return texto;
            int casas = texto.Length - ponto - 1;
            return casas == 1 ? texto.Replace('.', ',') : texto;
        }
        // número com unidade ("38.4 °C"): só a parte decimal do número vira vírgula — "12.345 GB" é milhar e fica como está
        return Regex.Replace(s, @"(?<![\d.])\d+\.\d{1,2}(?![\d.])", m => m.Value.Replace('.', ','));
    }

    private static void Section(StringBuilder sb, string title, List<ReportDisk> items)
    {
        if (items.Count == 0) return;
        sb.Append($"<section><h2>{E(title)}</h2>");
        foreach (var disk in items)
        {
            string cls = disk.Verdict switch { "SAUDÁVEL" or "ESTÁVEL" or "EM USO" => "ok", "SEM DADOS" => "na", _ => "bad" };
            sb.Append($"<div class=\"card\"><div class=\"row\"><b>{E(disk.Name)}</b><span class=\"badge {cls}\">{E(disk.Verdict)}</span></div>");
            if (disk.Details.Length > 0) sb.Append($"<p class=\"muted\">{E(disk.Details)}</p>");
            sb.Append($"<p>{E(disk.Reasons)}</p>");
            if (disk.Metrics.Count > 0)
            {
                sb.Append("<table>");
                foreach (var m in disk.Metrics) sb.Append($"<tr><td class=\"k\">{E(m.Key)}</td><td>{E(m.Value)}</td></tr>");
                sb.Append("</table>");
            }
            sb.Append("</div>");
        }
        sb.Append("</section>");
    }

    private static string E(string? s) => WebUtility.HtmlEncode(s ?? "");

    public static string Html(ReportData d)
    {
        var sb = new StringBuilder(64 * 1024);
        sb.Append("<!doctype html><html lang=\"pt-BR\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">");
        sb.Append($"<title>TRANAL SENTINEL · Relatório · {E(d.Machine)}</title><style>{Css}</style></head><body><div class=\"wrap\">");

        sb.Append("<header>");
        if (!string.IsNullOrEmpty(d.LogoBase64)) sb.Append($"<img class=\"logo\" alt=\"\" src=\"data:image/png;base64,{d.LogoBase64}\">");
        sb.Append($"<div><h1>TRANAL SENTINEL</h1><p class=\"sub\">Relatório de hardware · {E(d.Machine)} · {d.Date:dd/MM/yyyy HH:mm}");
        if (d.AppVersion.Length > 0) sb.Append($" · v{E(d.AppVersion)}");
        sb.Append("</p></div></header>");
        sb.Append("<p class=\"note\">Números de série foram omitidos deste relatório.</p>");

        // Resumo dos discos no topo (o que mais interessa de imediato)
        Section(sb, "Saúde dos discos", d.Disks);
        Section(sb, "Bateria e energia", d.Power);
        Section(sb, "Teste de rede", d.Network);

        if (d.System.Count > 0)
        {
            sb.Append("<section><h2>Sistema</h2>");
            foreach (var n in d.System) { sb.Append($"<div class=\"card\"><h3>{E(n.Name)}</h3>"); Tree(sb, n.Children, 0); sb.Append("</div>"); }
            sb.Append("</section>");
        }

        foreach (var g in d.Gpus)
        {
            sb.Append($"<section><h2>Placa de vídeo · {E(g.Name)}</h2>");
            if (g.Subtitle.Length > 0) sb.Append($"<p class=\"muted\">{E(g.Subtitle)}</p>");
            foreach (var s in g.Sections) { sb.Append($"<div class=\"card\"><h3>{E(s.Name)}</h3>"); Tree(sb, s.Children, 0); sb.Append("</div>"); }
            sb.Append("</section>");
        }

        if (d.Sensors.Count > 0)
        {
            sb.Append("<section><h2>Sensores (leitura no momento da exportação)</h2>");
            foreach (var grp in d.Sensors.GroupBy(s => s.Hardware))
            {
                sb.Append($"<div class=\"card\"><h3>{E(grp.Key)}</h3><table><tr><th>Categoria</th><th>Sensor</th><th>Valor</th><th>Mín.</th><th>Máx.</th></tr>");
                foreach (var s in grp)
                    sb.Append($"<tr><td class=\"muted\">{E(s.Category)}</td><td>{E(s.Name)}</td><td class=\"num\">{E(s.Value)}</td><td class=\"num muted\">{E(s.Min)}</td><td class=\"num muted\">{E(s.Max)}</td></tr>");
                sb.Append("</table></div>");
            }
            sb.Append("</section>");
        }

        if (d.Benchmarks.Count > 0)
        {
            sb.Append("<section><h2>Benchmarks e testes recentes</h2><div class=\"card\"><table><tr><th>Data</th><th>Teste</th><th>Resultado</th></tr>");
            foreach (var b in d.Benchmarks)
                sb.Append($"<tr><td class=\"muted\">{b.Date:dd/MM/yyyy HH:mm}</td><td>{E(b.Test)}</td><td>{E(b.Summary)}</td></tr>");
            sb.Append("</table></div></section>");
        }

        sb.Append("<footer>Gerado pelo TRANAL SENTINEL. Dados lidos do Windows, do driver da placa de vídeo e dos sensores da placa-mãe; podem variar conforme o hardware.</footer>");
        sb.Append("</div></body></html>");
        return sb.ToString();
    }

    private static void Tree(StringBuilder sb, List<ReportNode>? nodes, int depth)
    {
        if (nodes == null || nodes.Count == 0) return;
        sb.Append(depth == 0 ? "<table>" : "<table class=\"nested\">");
        foreach (var n in nodes)
        {
            if (n.Children != null)
            {
                sb.Append($"<tr><td colspan=\"2\" class=\"grp\">{E(n.Name)}{(n.Value != null ? " · " + E(n.Value) : "")}</td></tr><tr><td colspan=\"2\" class=\"inner\">");
                Tree(sb, n.Children, depth + 1);
                sb.Append("</td></tr>");
            }
            else sb.Append($"<tr><td class=\"k\">{E(n.Name)}</td><td>{E(n.Value)}</td></tr>");
        }
        sb.Append("</table>");
    }

    private const string Css = @"
:root{--bg:#fff;--fg:#111;--muted:#666;--line:#e3e3e3;--card:#fafafa;--ok:#111;--badbg:#111;--badfg:#fff}
@media (prefers-color-scheme:dark){:root{--bg:#000;--fg:#f2f2f2;--muted:#8c8c8c;--line:#222;--card:#0c0c0c;--ok:#f2f2f2;--badbg:#f2f2f2;--badfg:#000}}
*{box-sizing:border-box}body{margin:0;background:var(--bg);color:var(--fg);font:14px/1.5 'Segoe UI',system-ui,sans-serif}
.wrap{max-width:980px;margin:0 auto;padding:32px 20px 60px}
header{display:flex;gap:16px;align-items:center;margin-bottom:6px}.logo{width:44px;height:44px}
h1{margin:0;font-size:26px;letter-spacing:.5px}h2{margin:34px 0 12px;font-size:17px;letter-spacing:.4px}h3{margin:0 0 8px;font-size:14px}
.sub,.muted{color:var(--muted)}.sub{margin:2px 0 0}.note{color:var(--muted);font-size:12px;margin:0 0 4px}
.card{background:var(--card);border:1px solid var(--line);border-radius:6px;padding:14px 16px;margin:0 0 12px}
.row{display:flex;justify-content:space-between;gap:12px;align-items:center}
.badge{font-size:11px;font-weight:600;letter-spacing:.6px;padding:3px 10px;border-radius:3px;border:1px solid var(--ok);color:var(--ok)}
.badge.bad,.badge.na{background:var(--badbg);color:var(--badfg);border-color:var(--badbg)}.badge.na{opacity:.6}
table{width:100%;border-collapse:collapse}td,th{padding:5px 8px;border-bottom:1px solid var(--line);vertical-align:top;text-align:left}
th{color:var(--muted);font-weight:600;font-size:12px}.k{color:var(--muted);width:38%}.num{font-family:Consolas,monospace;text-align:right;white-space:nowrap}
.grp{font-weight:600;padding-top:12px}.inner{padding:0 0 0 14px;border-bottom:0}.nested td{font-size:13px}
footer{margin-top:40px;color:var(--muted);font-size:12px}
@media print{body{background:#fff;color:#000}.card{break-inside:avoid}}
";
}
