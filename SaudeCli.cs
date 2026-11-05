using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace TranalSentinel;

/// <summary>
/// Achado do diagnóstico no formato de arquivo (o que vai para o JSON).
/// </summary>
/// <remarks>
/// É um formato de arquivo, não uma segunda implementação do diagnóstico: as
/// regras são as mesmas da aba TRANAL SAÚDE (SaudeRegras). O DTO existe só
/// para o JSON ser estável e legível, independente de como o modelo interno
/// mudar.
/// </remarks>
public sealed class AchadoSalvo
{
    /// <summary>Severidade em texto (ex.: "Alto").</summary>
    public string Severidade { get; set; } = "";
    /// <summary>Área do problema (ex.: "Travamentos").</summary>
    public string Area { get; set; } = "";
    /// <summary>O que foi encontrado.</summary>
    public string Mensagem { get; set; } = "";
    /// <summary>O que fazer a respeito.</summary>
    public string Acao { get; set; } = "";
}

/// <summary>Travamento no formato de arquivo.</summary>
public sealed class TravamentoSalvo
{
    /// <summary>Data e hora do travamento.</summary>
    public DateTime Data { get; set; }
    /// <summary>Código de bugcheck ("0" = travamento total, sem tela azul).</summary>
    public string Bugcheck { get; set; } = "0";
    /// <summary>Detalhe do evento.</summary>
    public string Detalhe { get; set; } = "";
}

/// <summary>Instantâneo do diagnóstico, gravado em disco.</summary>
public sealed class InstantaneoSaude
{
    /// <summary>Quando a análise foi feita.</summary>
    public DateTime GeradoEm { get; set; }
    /// <summary>Achados encontrados.</summary>
    public List<AchadoSalvo> Achados { get; set; } = new();
    /// <summary>Travamentos registrados na janela analisada.</summary>
    public List<TravamentoSalvo> Travamentos { get; set; } = new();
}

/// <summary>O que aconteceu com um achado entre dois instantâneos.</summary>
public enum TipoDiferenca
{
    /// <summary>O problema foi resolvido (estava antes, não está agora).</summary>
    Resolveu,
    /// <summary>O problema apareceu.</summary>
    Novo,
    /// <summary>O problema continua igual.</summary>
    Persiste,
}

/// <summary>Uma diferença entre dois instantâneos do diagnóstico.</summary>
public sealed record DiferencaSaude(TipoDiferenca Tipo, string Achado, string Detalhe);

/// <summary>
/// Diagnóstico pela linha de comando: <c>--saude coletar|comparar|relatar</c>.
/// </summary>
/// <remarks>
/// <para>
/// É a mesma análise da aba TRANAL SAÚDE, exposta em linha de comando: o
/// diagnóstico é rodado periodicamente (por um script, pelo Agendador de
/// Tarefas) e duas execuções são comparadas depois, para ver o que mudou.
/// </para>
/// <para>
/// Como o executável é de subsistema Windows (sem console), toda saída é
/// gravada em arquivo <b>e</b> escrita no console — assim funciona tanto
/// aberto por clique quanto por terminal.
/// </para>
/// </remarks>
public static class SaudeCli
{
    /// <summary>Flag que liga o modo de linha de comando.</summary>
    public const string Argumento = "--saude";

    /// <summary>Pasta padrão dos instantâneos.</summary>
    public static string Pasta => Path.Combine(AppSettings.Folder, "saude");

    /// <summary>Executa a CLI. Devolve 0 (ok), 1 (erro) ou 2 (uso incorreto).</summary>
    public static int Executar(string[] args)
    {
        try
        {
            var resto = args.SkipWhile(a => !string.Equals(a, Argumento, StringComparison.OrdinalIgnoreCase)).Skip(1).ToList();
            string comando = resto.Count > 0 ? resto[0].ToLowerInvariant() : "";

            return comando switch
            {
                "coletar" => Coletar(resto),
                "comparar" => Comparar(resto),
                "relatar" => Relatar(resto),
                _ => Uso(),
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Falha no diagnóstico: " + ex.Message);
            return 1;
        }
    }

    private static int Uso()
    {
        string texto =
            "TRANAL SENTINEL — diagnóstico pela linha de comando\n" +
            "\n" +
            "  TranalSentinel.exe --saude coletar [--saida <pasta>]\n" +
            "      Roda a análise e grava um instantâneo em JSON e outro em TXT.\n" +
            "\n" +
            "  TranalSentinel.exe --saude comparar <antes.json> <depois.json>\n" +
            "      Compara dois instantâneos: o que sumiu, o que apareceu, o que continua.\n" +
            "\n" +
            "  TranalSentinel.exe --saude relatar [--saida <arquivo>]\n" +
            "      Roda a análise e escreve o relatório em texto.\n";
        Console.WriteLine(texto);
        GravarSaida(texto, Path.Combine(Pasta, "uso.txt"));
        return 2;
    }

    private static int Coletar(List<string> resto)
    {
        string pasta = Valor(resto, "--saida") ?? Pasta;
        Directory.CreateDirectory(pasta);

        var relatorio = SaudeDiagnostico.Executar();
        var instantaneo = ParaInstantaneo(relatorio);

        string carimbo = relatorio.GeradoEm.ToString("yyyyMMdd-HHmmss");
        string arquivoJson = Path.Combine(pasta, $"saude-{carimbo}.json");
        string arquivoTxt = Path.Combine(pasta, $"saude-{carimbo}.txt");

        File.WriteAllText(arquivoJson,
            JsonSerializer.Serialize(instantaneo, new JsonSerializerOptions { WriteIndented = true }));
        string texto = RelatorioTexto(relatorio);
        File.WriteAllText(arquivoTxt, texto);

        RegistroDiario.Registrar($"Diagnóstico por linha de comando: {relatorio.Total} achado(s).", "SAUDE");

        Console.WriteLine(texto);
        Console.WriteLine($"Instantâneo gravado em: {arquivoJson}");
        Console.WriteLine($"Relatório gravado em:   {arquivoTxt}");
        return 0;
    }

    private static int Relatar(List<string> resto)
    {
        var relatorio = SaudeDiagnostico.Executar();
        string texto = RelatorioTexto(relatorio);
        Console.WriteLine(texto);

        string saida = Valor(resto, "--saida");
        if (!string.IsNullOrWhiteSpace(saida))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(saida))!);
            File.WriteAllText(saida, texto);
            Console.WriteLine($"Relatório gravado em: {saida}");
        }
        else
        {
            GravarSaida(texto, Path.Combine(Pasta, $"relatorio-{relatorio.GeradoEm:yyyyMMdd-HHmmss}.txt"));
        }
        return 0;
    }

    private static int Comparar(List<string> resto)
    {
        if (resto.Count < 3)
        {
            Console.Error.WriteLine("Uso: TranalSentinel.exe --saude comparar <antes.json> <depois.json>");
            return 2;
        }
        string caminhoAntes = resto[1];
        string caminhoDepois = resto[2];

        var antes = Ler(caminhoAntes);
        var depois = Ler(caminhoDepois);
        if (antes == null || depois == null) return 1;

        var diferencas = CompararAchados(antes.Achados, depois.Achados);

        var texto = new StringBuilder();
        texto.AppendLine("COMPARAÇÃO DO DIAGNÓSTICO");
        texto.AppendLine($"Antes:  {caminhoAntes}  ({antes.GeradoEm:dd/MM/yyyy HH:mm})");
        texto.AppendLine($"Depois: {caminhoDepois}  ({depois.GeradoEm:dd/MM/yyyy HH:mm})");
        texto.AppendLine();
        texto.AppendLine($"RESOLVIDOS: {diferencas.Count(d => d.Tipo == TipoDiferenca.Resolveu)}");
        texto.AppendLine($"NOVOS:      {diferencas.Count(d => d.Tipo == TipoDiferenca.Novo)}");
        texto.AppendLine($"PERSISTEM:  {diferencas.Count(d => d.Tipo == TipoDiferenca.Persiste)}");
        texto.AppendLine();
        foreach (var d in diferencas)
        {
            string marca = d.Tipo switch
            {
                TipoDiferenca.Resolveu => "  ✓ RESOLVIDO",
                TipoDiferenca.Novo => "  ✗ NOVO",
                _ => "  · PERSISTE",
            };
            texto.AppendLine(marca + "  " + d.Achado + (d.Detalhe.Length > 0 ? " — " + d.Detalhe : ""));
        }

        Console.WriteLine(texto.ToString());
        GravarSaida(texto.ToString(), Path.Combine(Pasta, $"comparacao-{DateTime.Now:yyyyMMdd-HHmmss}.txt"));
        RegistroDiario.Registrar($"Comparação do diagnóstico: {diferencas.Count} diferença(s).", "SAUDE");
        return 0;
    }

    /// <summary>
    /// Compara os achados de dois instantâneos.
    /// </summary>
    /// <remarks>
    /// A chave de um achado é "área + mensagem": o problema em si, não o texto
    /// da ação sugerida (que pode mudar de redação sem mudar o problema).
    /// </remarks>
    public static List<DiferencaSaude> CompararAchados(IReadOnlyList<AchadoSalvo> antes, IReadOnlyList<AchadoSalvo> depois)
    {
        var lista = new List<DiferencaSaude>();
        var antesPorChave = new Dictionary<string, AchadoSalvo>(StringComparer.OrdinalIgnoreCase);
        foreach (var a in antes) antesPorChave[Chave(a)] = a;

        var depoisPorChave = new Dictionary<string, AchadoSalvo>(StringComparer.OrdinalIgnoreCase);
        foreach (var a in depois) depoisPorChave[Chave(a)] = a;

        foreach (var par in depoisPorChave)
        {
            if (!antesPorChave.TryGetValue(par.Key, out var antigo))
            {
                lista.Add(new DiferencaSaude(TipoDiferenca.Novo, par.Value.Mensagem, par.Value.Area));
                continue;
            }
            if (antigo.Severidade != par.Value.Severidade)
            {
                lista.Add(new DiferencaSaude(TipoDiferenca.Novo, par.Value.Mensagem,
                    $"{par.Value.Area} — severidade {antigo.Severidade} → {par.Value.Severidade}"));
                continue;
            }
            lista.Add(new DiferencaSaude(TipoDiferenca.Persiste, par.Value.Mensagem, par.Value.Area));
        }

        foreach (var par in antesPorChave)
        {
            if (depoisPorChave.ContainsKey(par.Key)) continue;
            lista.Add(new DiferencaSaude(TipoDiferenca.Resolveu, par.Value.Mensagem, par.Value.Area));
        }

        return lista;
    }

    private static string Chave(AchadoSalvo a) => (a.Area + "|" + a.Mensagem).Trim();

    private static InstantaneoSaude? Ler(string caminho)
    {
        try
        {
            if (!File.Exists(caminho))
            {
                Console.Error.WriteLine("Arquivo não encontrado: " + caminho);
                return null;
            }
            var dados = JsonSerializer.Deserialize<InstantaneoSaude>(File.ReadAllText(caminho));
            if (dados == null)
            {
                Console.Error.WriteLine("Arquivo ilegível: " + caminho);
                return null;
            }
            return dados;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Não deu para ler " + caminho + ": " + ex.Message);
            return null;
        }
    }

    private static InstantaneoSaude ParaInstantaneo(RelatorioSaude relatorio) => new()
    {
        GeradoEm = relatorio.GeradoEm,
        Achados = relatorio.Achados.Select(a => new AchadoSalvo
        {
            Severidade = a.Severidade.ToString(),
            Area = a.Area,
            Mensagem = a.Mensagem,
            Acao = a.Acao,
        }).ToList(),
        Travamentos = relatorio.Travamentos.Select(t => new TravamentoSalvo
        {
            Data = t.Data,
            Bugcheck = t.Bugcheck,
            Detalhe = t.Detalhe,
        }).ToList(),
    };

    private static string RelatorioTexto(RelatorioSaude relatorio)
    {
        var texto = new StringBuilder();
        texto.AppendLine("TRANAL SENTINEL — DIAGNÓSTICO DE SAÚDE");
        texto.AppendLine($"Gerado em: {relatorio.GeradoEm:dd/MM/yyyy HH:mm}");
        texto.AppendLine($"Achados: {relatorio.Total}  ·  críticos: {relatorio.Criticos}  ·  altos: {relatorio.Altos}");
        texto.AppendLine();
        texto.AppendLine($"TRAVAMENTOS ({relatorio.Travamentos.Count})");
        foreach (var t in relatorio.Travamentos)
            texto.AppendLine($"  {t.DataFormatada}  bugcheck {t.Bugcheck}  {t.Detalhe}");
        texto.AppendLine();
        texto.AppendLine("ACHADOS");
        foreach (var a in relatorio.Achados)
        {
            texto.AppendLine($"  [{a.Selo}] {a.Area}: {a.Mensagem}");
            if (a.Acao.Length > 0) texto.AppendLine($"        -> {a.Acao}");
        }
        return texto.ToString();
    }

    private static string? Valor(List<string> args, string nome)
    {
        int i = args.FindIndex(a => string.Equals(a, nome, StringComparison.OrdinalIgnoreCase));
        if (i < 0 || i + 1 >= args.Count) return null;
        return args[i + 1];
    }

    private static void GravarSaida(string texto, string caminho)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(caminho)!);
            File.WriteAllText(caminho, texto);
        }
        catch { }
    }
}
