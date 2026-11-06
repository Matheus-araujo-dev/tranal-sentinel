using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TranalSentinel;

/// <summary>As specs da máquina, para saber se dois PCs são comparáveis.</summary>
public sealed record BenchMaquina
{
    /// <summary>Nome do computador, como o Windows mostra.</summary>
    public string Nome { get; init; } = "";

    /// <summary>Modelo do processador.</summary>
    public string Cpu { get; init; } = "";

    /// <summary>Modelo da placa de vídeo.</summary>
    public string Gpu { get; init; } = "";

    /// <summary>Memória instalada, em gigabytes.</summary>
    public double MemoriaGb { get; init; }

    /// <summary>Sistema operacional, como o Windows informa.</summary>
    public string Sistema { get; init; } = "";
}

/// <summary>Um resultado de benchmark dentro do arquivo de intercâmbio.</summary>
public sealed record BenchResultado
{
    /// <summary>Quando o teste rodou.</summary>
    public DateTime Data { get; init; }

    /// <summary>
    /// A chave do teste: "render:multi", "render:single", "gpu", "mem",
    /// "disk", "sim" ou "rt:preset:altura".
    /// </summary>
    public string Chave { get; init; } = "";

    /// <summary>A nota. Em todos os testes, maior é melhor.</summary>
    public double Nota { get; init; }

    /// <summary>O detalhe do resultado, em texto.</summary>
    public string Resumo { get; init; } = "";
}

/// <summary>
/// O arquivo de intercâmbio: como o resultado de um PC viaja para o outro.
///
/// <para>
/// É um JSON com a identificação da máquina e os resultados. Quem recebe
/// importa e o TRANAL SENTINEL compara com os resultados locais, avisando
/// quando as máquinas são diferentes demais para a comparação valer.
/// </para>
/// </summary>
public sealed record BenchArquivo
{
    /// <summary>Formato do arquivo (para o app saber o que está lendo).</summary>
    public string Formato { get; init; } = BenchIntercambio.Formato;

    /// <summary>Versão do formato. Uma versão nova sabe ler as antigas.</summary>
    public int Versao { get; init; } = BenchIntercambio.Versao;

    /// <summary>De qual app (e versão) o arquivo saiu.</summary>
    public string App { get; init; } = "";

    /// <summary>Quando o arquivo foi gerado.</summary>
    public DateTime ExportadoEm { get; init; }

    /// <summary>As specs da máquina de origem.</summary>
    public BenchMaquina Maquina { get; init; } = new();

    /// <summary>Os resultados exportados.</summary>
    public List<BenchResultado> Resultados { get; init; } = new();
}

/// <summary>
/// O formato de intercâmbio de benchmarks entre PCs.
///
/// <para>
/// O arquivo é um JSON legível: se um dia o formato precisar mudar, a
/// <see cref="Versao"/> diz o que o leitor está lendo, e o app diz na tela
/// quando o arquivo veio de uma versão que ele não conhece.
/// </para>
/// </summary>
public static class BenchIntercambio
{
    /// <summary>Identificador do formato.</summary>
    public const string Formato = "tranal-bench";

    /// <summary>Versão do formato.</summary>
    public const int Versao = 1;

    /// <summary>Extensão do arquivo exportado.</summary>
    public const string Extensao = ".tranalbench.json";

    /// <summary>Nome sugerido do arquivo na hora de exportar.</summary>
    public static string NomeSugerido() =>
        $"TRANAL-BENCH-{Environment.MachineName}-{DateTime.Now:yyyyMMdd-HHmm}{Extensao}";

    private static readonly JsonSerializerOptions Opcoes = new()
    {
        WriteIndented = true,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
    };

    /// <summary>
    /// Converte o que o app guardou em resultados de intercâmbio. É a mesma
    /// função dos dois lados — o local e o importado —, para que a comparação
    /// seja justa por construção.
    /// </summary>
    public static List<BenchResultado> Resultados(IEnumerable<BenchRun> renders, IEnumerable<BenchRecord> registros)
    {
        var lista = new List<BenchResultado>();

        foreach (BenchRun run in renders ?? Enumerable.Empty<BenchRun>())
        {
            if (run.Multi is double multi)
            {
                lista.Add(new BenchResultado
                {
                    Data = run.Date,
                    Chave = "render:multi",
                    Nota = multi,
                    Resumo = $"multi-core · {run.Cpu}".Trim(' ', '·')
                });
            }

            if (run.Single is double single)
            {
                lista.Add(new BenchResultado
                {
                    Data = run.Date,
                    Chave = "render:single",
                    Nota = single,
                    Resumo = $"single-core · {run.Cpu}".Trim(' ', '·')
                });
            }
        }

        foreach (BenchRecord registro in registros ?? Enumerable.Empty<BenchRecord>())
        {
            lista.Add(new BenchResultado
            {
                Data = registro.Date,
                Chave = registro.Test,
                Nota = registro.Score,
                Resumo = registro.Summary
            });
        }

        return lista;
    }

    /// <summary>Monta o arquivo de intercâmbio com o que esta máquina tem.</summary>
    public static BenchArquivo Montar(IEnumerable<BenchRun> renders, IEnumerable<BenchRecord> registros, BenchMaquina maquina)
    {
        ArgumentNullException.ThrowIfNull(maquina);

        return new BenchArquivo
        {
            App = "TRANAL SENTINEL " + VersaoDoApp(),
            ExportadoEm = DateTime.Now,
            Maquina = maquina,
            Resultados = Resultados(renders, registros)
        };
    }

    /// <summary>Grava o arquivo de intercâmbio.</summary>
    public static void Gravar(BenchArquivo arquivo, string caminho)
    {
        ArgumentNullException.ThrowIfNull(arquivo);
        ArgumentNullException.ThrowIfNull(caminho);

        string? pasta = Path.GetDirectoryName(caminho);
        if (!string.IsNullOrEmpty(pasta)) Directory.CreateDirectory(pasta);

        File.WriteAllText(caminho, JsonSerializer.Serialize(arquivo, Opcoes));
    }

    /// <summary>
    /// Lê o arquivo de intercâmbio. Devolve null com o motivo quando o
    /// arquivo não é do formato ou a versão é de um app mais novo.
    /// </summary>
    public static BenchArquivo? Ler(string caminho, out string? erro)
    {
        erro = null;

        try
        {
            if (!File.Exists(caminho))
            {
                erro = "Arquivo não encontrado.";
                return null;
            }

            BenchArquivo? arquivo = JsonSerializer.Deserialize<BenchArquivo>(File.ReadAllText(caminho), Opcoes);
            if (arquivo == null)
            {
                erro = "O arquivo está vazio ou não é um arquivo de benchmarks.";
                return null;
            }

            if (!string.Equals(arquivo.Formato, Formato, StringComparison.Ordinal))
            {
                erro = "Este arquivo não é de benchmarks do TRANAL SENTINEL.";
                return null;
            }

            if (arquivo.Versao > Versao)
            {
                erro = $"O arquivo é da versão {arquivo.Versao} do formato e este app entende até a {Versao}. Atualize o TRANAL SENTINEL.";
                return null;
            }

            return arquivo;
        }
        catch (Exception ex)
        {
            erro = "Não deu para ler o arquivo: " + ex.Message;
            return null;
        }
    }

    /// <summary>As specs desta máquina, para o arquivo de intercâmbio.</summary>
    public static BenchMaquina MaquinaAtual()
    {
        return new BenchMaquina
        {
            Nome = Environment.MachineName,
            Cpu = PrimeiroValor("SELECT Name FROM Win32_Processor", "Name"),
            Gpu = PrimeiroValor("SELECT Name FROM Win32_VideoController", "Name"),
            MemoriaGb = MemoriaTotalGb(),
            Sistema = PrimeiroValor("SELECT Caption FROM Win32_OperatingSystem", "Caption")
        };
    }

    /// <summary>A versão do app, para o arquivo dizer de onde veio.</summary>
    public static string VersaoDoApp()
    {
        string? informational = typeof(BenchIntercambio).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        return informational ?? typeof(BenchIntercambio).Assembly.GetName().Version?.ToString() ?? "";
    }

    private static string PrimeiroValor(string consulta, string propriedade)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(consulta);
            foreach (ManagementObject objeto in searcher.Get())
            {
                string? valor = objeto[propriedade]?.ToString();
                if (!string.IsNullOrWhiteSpace(valor)) return valor.Trim();
            }
        }
        catch { }

        return "";
    }

    private static double MemoriaTotalGb()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT TotalPhysicalMemory FROM Win32_ComputerSystem");
            foreach (ManagementObject objeto in searcher.Get())
            {
                if (objeto["TotalPhysicalMemory"] is ulong bytes)
                {
                    return Math.Round(bytes / 1024.0 / 1024.0 / 1024.0, 1);
                }
            }
        }
        catch { }

        return 0;
    }
}
