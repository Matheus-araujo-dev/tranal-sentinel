using System;
using System.Collections.Generic;
using System.Linq;

namespace TranalSentinel;

/// <summary>Uma linha da comparação entre dois PCs.</summary>
public sealed class LinhaComparacao
{
    /// <summary>A chave do teste ("render:multi", "gpu"...).</summary>
    public string Chave { get; init; } = "";

    /// <summary>O nome do teste, para a tela.</summary>
    public string Teste { get; init; } = "";

    /// <summary>A nota desta máquina.</summary>
    public double NotaLocal { get; init; }

    /// <summary>A nota do PC importado.</summary>
    public double NotaImportada { get; init; }

    /// <summary>Quanto esta máquina é mais rápida, em porcentagem (positivo = mais rápida).</summary>
    public double DiferencaPercentual { get; init; }

    /// <summary>Quando o teste rodou nesta máquina.</summary>
    public DateTime DataLocal { get; init; }

    /// <summary>Quando o teste rodou no PC importado.</summary>
    public DateTime DataImportada { get; init; }

    /// <summary>O detalhe do resultado local.</summary>
    public string ResumoLocal { get; init; } = "";

    /// <summary>O detalhe do resultado importado.</summary>
    public string ResumoImportado { get; init; } = "";

    /// <summary>"mais rápido", "mais lento" ou "empate".</summary>
    public string Veredito { get; init; } = "";
}

/// <summary>O relatório da comparação entre dois PCs.</summary>
public sealed class RelatorioComparacao
{
    /// <summary>As specs desta máquina.</summary>
    public BenchMaquina MaquinaLocal { get; init; } = new();

    /// <summary>As specs do PC importado.</summary>
    public BenchMaquina MaquinaImportada { get; init; } = new();

    /// <summary>
    /// As diferenças de hardware entre os dois PCs. Vazia quando as máquinas
    /// são equivalentes (aí a comparação vale direto).
    /// </summary>
    public List<string> Diferencas { get; init; } = new();

    /// <summary>As linhas da comparação, uma por teste que existe nos dois PCs.</summary>
    public List<LinhaComparacao> Linhas { get; init; } = new();

    /// <summary>Quando o arquivo foi exportado do outro PC.</summary>
    public DateTime ExportadoEm { get; init; }

    /// <summary>De qual app o arquivo saiu.</summary>
    public string AppOrigem { get; init; } = "";

    /// <summary>True quando as duas máquinas são comparáveis sem ressalva.</summary>
    public bool Comparavel => Diferencas.Count == 0;

    /// <summary>Os testes que só existem em um dos PCs (para o app dizer).</summary>
    public List<string> SoLocal { get; init; } = new();

    /// <summary>Os testes que só existem no PC importado.</summary>
    public List<string> SoImportado { get; init; } = new();
}

/// <summary>
/// A regra da comparação entre benchmarks de PCs diferentes.
///
/// <para>
/// É função pura: recebe os dois conjuntos de resultados e devolve o
/// relatório. Não lê arquivo, não consulta a máquina — é o que os testes
/// cobrem.
/// </para>
/// </summary>
public static class BenchComparacao
{
    /// <summary>
    /// Diferença menor que isso conta como empate: a variação do próprio
    /// benchmark já é de alguns por cento entre execuções.
    /// </summary>
    public const double MargemEmpatePercentual = 3.0;

    /// <summary>
    /// Compara os resultados locais com os do arquivo importado.
    /// </summary>
    /// <param name="local">As specs desta máquina.</param>
    /// <param name="importado">O arquivo que veio do outro PC.</param>
    /// <param name="resultadosLocais">Os resultados desta máquina.</param>
    public static RelatorioComparacao Comparar(BenchMaquina local, BenchArquivo importado, IEnumerable<BenchResultado> resultadosLocais)
    {
        ArgumentNullException.ThrowIfNull(local);
        ArgumentNullException.ThrowIfNull(importado);
        ArgumentNullException.ThrowIfNull(resultadosLocais);

        var melhoresLocais = Melhores(resultadosLocais);
        var melhoresImportados = Melhores(importado.Resultados);

        var linhas = new List<LinhaComparacao>();
        var soLocal = new List<string>();
        var soImportado = new List<string>();

        foreach (string chave in melhoresLocais.Keys.OrderBy(k => k, StringComparer.Ordinal))
        {
            BenchResultado local_ = melhoresLocais[chave];

            if (!melhoresImportados.TryGetValue(chave, out BenchResultado? importado_))
            {
                soLocal.Add(BenchCatalogo.Nome(chave));
                continue;
            }

            double diferenca = importado_.Nota > 0
                ? (local_.Nota - importado_.Nota) / importado_.Nota * 100.0
                : 0;

            linhas.Add(new LinhaComparacao
            {
                Chave = chave,
                Teste = BenchCatalogo.Nome(chave),
                NotaLocal = local_.Nota,
                NotaImportada = importado_.Nota,
                DiferencaPercentual = diferenca,
                DataLocal = local_.Data,
                DataImportada = importado_.Data,
                ResumoLocal = local_.Resumo,
                ResumoImportado = importado_.Resumo,
                Veredito = diferenca > MargemEmpatePercentual ? "mais rápido"
                    : diferenca < -MargemEmpatePercentual ? "mais lento"
                    : "empate"
            });
        }

        foreach (string chave in melhoresImportados.Keys.OrderBy(k => k, StringComparer.Ordinal))
        {
            if (melhoresLocais.ContainsKey(chave)) continue;
            soImportado.Add(BenchCatalogo.Nome(chave));
        }

        return new RelatorioComparacao
        {
            MaquinaLocal = local,
            MaquinaImportada = importado.Maquina,
            Diferencas = DiferencasDeHardware(local, importado.Maquina),
            Linhas = linhas,
            ExportadoEm = importado.ExportadoEm,
            AppOrigem = importado.App,
            SoLocal = soLocal,
            SoImportado = soImportado
        };
    }

    /// <summary>
    /// O melhor resultado de cada teste (em todos os benchmarks, nota maior é
    /// melhor). É o que se compara: o pico de cada PC, como no Cinebench.
    /// </summary>
    private static Dictionary<string, BenchResultado> Melhores(IEnumerable<BenchResultado> resultados)
    {
        var melhores = new Dictionary<string, BenchResultado>(StringComparer.Ordinal);

        foreach (BenchResultado resultado in resultados)
        {
            if (resultado == null || string.IsNullOrEmpty(resultado.Chave)) continue;

            if (!melhores.TryGetValue(resultado.Chave, out BenchResultado? atual) || resultado.Nota > atual.Nota)
            {
                melhores[resultado.Chave] = resultado;
            }
        }

        return melhores;
    }

    /// <summary>
    /// As diferenças de hardware entre os dois PCs. São as que importam para
    /// a comparação: CPU e GPU diferentes mudam o resultado; a memória
    /// muda pouco; o sistema operacional muda o ambiente.
    /// </summary>
    public static List<string> DiferencasDeHardware(BenchMaquina local, BenchMaquina importado)
    {
        var diferencas = new List<string>();

        if (!Mesma(local.Cpu, importado.Cpu))
        {
            diferencas.Add($"CPU diferente: esta máquina tem {Curto(local.Cpu)}, a outra tem {Curto(importado.Cpu)}.");
        }

        if (!Mesma(local.Gpu, importado.Gpu))
        {
            diferencas.Add($"Placa de vídeo diferente: esta máquina tem {Curto(local.Gpu)}, a outra tem {Curto(importado.Gpu)}.");
        }

        if (local.MemoriaGb > 0 && importado.MemoriaGb > 0 && Math.Abs(local.MemoriaGb - importado.MemoriaGb) >= 1)
        {
            diferencas.Add($"Memória diferente: esta máquina tem {MemoriaTexto.Numero(local.MemoriaGb)} GB, a outra tem {MemoriaTexto.Numero(importado.MemoriaGb)} GB.");
        }

        if (!Mesma(local.Sistema, importado.Sistema))
        {
            diferencas.Add($"Sistema operacional diferente: esta máquina tem {Curto(local.Sistema)}, a outra tem {Curto(importado.Sistema)}.");
        }

        return diferencas;
    }

    private static bool Mesma(string a, string b) =>
        string.Equals((a ?? "").Trim(), (b ?? "").Trim(), StringComparison.OrdinalIgnoreCase);

    private static string Curto(string valor)
    {
        string limpo = (valor ?? "").Trim();
        if (limpo.Length == 0) return "desconhecida";
        return limpo.Length > 48 ? limpo[..48] + "…" : limpo;
    }
}
