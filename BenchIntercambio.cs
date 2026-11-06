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
    /// A chave do teste: "render:multi", "render:single",
    /// "render:sustained:multi", "gpu", "mem", "disk", "sim" ou
    /// "rt:preset:altura".
    /// </summary>
    public string Chave { get; init; } = "";

    /// <summary>A nota. Em todos os testes, maior é melhor.</summary>
    public double Nota { get; init; }

    /// <summary>O detalhe do resultado, em texto.</summary>
    public string Resumo { get; init; } = "";

    /// <summary>
    /// A nota só existe se for um número finito e maior que zero.
    ///
    /// <para>
    /// Nota quebrada (NaN, infinito) ou zero é benchmark que falhou — e
    /// benchmark que falhou não é resultado. A regra mora aqui para que
    /// exportar, importar e comparar usem o mesmo critério: melhor sem
    /// resultado do que com resultado errado.
    /// </para>
    /// </summary>
    public static bool NotaUtilizavel(double nota) => double.IsFinite(nota) && nota > 0;
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

        // Aceita "NaN" e "Infinity" na LEITURA para que um arquivo com uma
        // linha quebrada ainda traga as linhas boas — a linha quebrada é
        // descartada pela regra da nota, não pelo arquivo inteiro. Na
        // escrita os valores são saneados, então nada disso vai para o disco.
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
            if (run.Multi is double multi && BenchResultado.NotaUtilizavel(multi))
            {
                lista.Add(new BenchResultado
                {
                    Data = run.Date,
                    Chave = ChaveRender("multi", run.Sustained),
                    Nota = multi,
                    Resumo = ResumoRender("multi-core", run)
                });
            }

            if (run.Single is double single && BenchResultado.NotaUtilizavel(single))
            {
                lista.Add(new BenchResultado
                {
                    Data = run.Date,
                    Chave = ChaveRender("single", run.Sustained),
                    Nota = single,
                    Resumo = ResumoRender("single-core", run)
                });
            }
        }

        foreach (BenchRecord registro in registros ?? Enumerable.Empty<BenchRecord>())
        {
            if (registro == null || string.IsNullOrEmpty(registro.Test)) continue;

            // Benchmark que falhou (nota zero ou quebrada) não vira resultado:
            // ele sai da lista e a tela avisa que este PC não tem nota.
            if (!BenchResultado.NotaUtilizavel(registro.Score)) continue;

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

        // O que vai para o arquivo é o que é aproveitável: nota quebrada
        // (NaN, infinito) ou zero é benchmark que falhou, e benchmark que
        // falhou não é resultado. A memória fora do normal também vira zero
        // em vez de NaN.
        BenchMaquina maquina = arquivo.Maquina ?? new BenchMaquina();
        if (!double.IsFinite(maquina.MemoriaGb)) maquina = maquina with { MemoriaGb = 0 };

        BenchArquivo saneado = arquivo with
        {
            Maquina = maquina,
            Resultados = (arquivo.Resultados ?? new List<BenchResultado>())
                .Where(r => r != null && !string.IsNullOrEmpty(r.Chave) && BenchResultado.NotaUtilizavel(r.Nota))
                .ToList()
        };

        File.WriteAllText(caminho, JsonSerializer.Serialize(saneado, Opcoes));
    }

    /// <summary>
    /// Lê o arquivo de intercâmbio. Devolve null com o motivo quando o
    /// arquivo não é do formato ou a versão é de um app mais novo.
    /// </summary>
    public static BenchArquivo? Ler(string caminho, out string? erro) => Ler(caminho, out erro, out _);

    /// <summary>
    /// Lê o arquivo de intercâmbio, dizendo quantos resultados foram
    /// descartados por terem nota inválida (NaN, infinito, zero ou chave
    /// vazia).
    /// </summary>
    public static BenchArquivo? Ler(string caminho, out string? erro, out int descartados)
    {
        erro = null;
        descartados = 0;

        try
        {
            if (!File.Exists(caminho))
            {
                erro = "Arquivo não encontrado.";
                return null;
            }

            string texto = File.ReadAllText(caminho);

            // Formato e versão primeiro: um arquivo de uma versão futura é
            // recusado com a explicação, antes de o app tentar entender o
            // conteúdo dele (que é de outro formato e não vai entender).
            try
            {
                using JsonDocument documento = JsonDocument.Parse(texto);
                JsonElement raiz = documento.RootElement;

                if (!raiz.TryGetProperty("Formato", out JsonElement formato) || formato.GetString() != Formato)
                {
                    erro = "Este arquivo não é de benchmarks do TRANAL SENTINEL.";
                    return null;
                }

                if (raiz.TryGetProperty("Versao", out JsonElement versao) &&
                    versao.TryGetInt32(out int numero) && numero > Versao)
                {
                    erro = $"O arquivo é da versão {numero} do formato e este app entende até a {Versao}. Atualize o TRANAL SENTINEL.";
                    return null;
                }
            }
            catch (JsonException)
            {
                erro = "Não deu para ler o arquivo: o conteúdo não é um arquivo de benchmarks válido.";
                return null;
            }

            BenchArquivo? arquivo = JsonSerializer.Deserialize<BenchArquivo>(texto, Opcoes);
            if (arquivo == null)
            {
                erro = "O arquivo está vazio ou não é um arquivo de benchmarks.";
                return null;
            }

            // O JSON pode trazer "Maquina": null — o inicializador do registro
            // só vale quando a propriedade está AUSENTE. Sem esta guarda, a
            // comparação estouraria com NullReferenceException.
            BenchMaquina? maquina = arquivo.Maquina;
            if (maquina == null)
            {
                erro = "O arquivo não tem a identificação da máquina de origem.";
                return null;
            }

            if (!double.IsFinite(maquina.MemoriaGb)) maquina = maquina with { MemoriaGb = 0 };

            // Resultado sem nota aproveitável é resultado ausente: fica fora
            // da comparação, e o app avisa quantos foram.
            var validos = new List<BenchResultado>();
            foreach (BenchResultado resultado in arquivo.Resultados ?? new List<BenchResultado>())
            {
                if (resultado == null || string.IsNullOrEmpty(resultado.Chave) ||
                    !BenchResultado.NotaUtilizavel(resultado.Nota))
                {
                    descartados++;
                    continue;
                }

                validos.Add(resultado);
            }

            return arquivo with { Maquina = maquina, Resultados = validos };
        }
        catch (JsonException)
        {
            erro = "Não deu para ler o arquivo: o conteúdo não é um arquivo de benchmarks válido.";
            return null;
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
            Gpu = GpuInfoCollector.NomeDaPlacaPrincipal(),
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

    /// <summary>
    /// A chave do render. O teste sustentado (10 minutos, limitado
    /// termicamente) é OUTRO teste que a passada curta — sem isso na chave, a
    /// comparação confrontaria uma passada curta com uma de 10 minutos.
    /// </summary>
    private static string ChaveRender(string nucleo, bool sustentado) =>
        sustentado ? $"render:sustained:{nucleo}" : $"render:{nucleo}";

    /// <summary>O resumo do render: o núcleo, se foi sustentado e a CPU do dia.</summary>
    private static string ResumoRender(string nucleo, BenchRun run) =>
        $"{nucleo}{(run.Sustained ? " · sustentado" : "")} · {run.Cpu}".Trim(' ', '·');

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
        catch
        {
            // O WMI pode não responder (serviço parado, máquina sob carga):
            // o campo fica vazio e a comparação avisa que a informação não
            // veio, em vez de o app inventar um valor.
        }

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
        catch
        {
            // Sem resposta do WMI: a memória vai como zero, e a comparação
            // trata memória desconhecida como "não sei", sem alarme falso.
        }

        return 0;
    }
}
