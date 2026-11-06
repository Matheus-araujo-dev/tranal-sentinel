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

    /// <summary>
    /// Os testes que rodaram nesta máquina, mas não têm nota utilizável (o
    /// benchmark falhou). Não dá para comparar o que não tem nota.
    /// </summary>
    public List<string> SemNotaLocal { get; init; } = new();

    /// <summary>Os testes do PC importado que rodaram, mas ficaram sem nota.</summary>
    public List<string> SemNotaImportado { get; init; } = new();
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

        BenchMaquina maquinaImportada = importado.Maquina ?? new BenchMaquina();

        var semNotaLocal = new List<string>();
        var semNotaImportado = new List<string>();
        var chavesSemNotaLocal = new HashSet<string>(StringComparer.Ordinal);
        var chavesSemNotaImportado = new HashSet<string>(StringComparer.Ordinal);

        var melhoresLocais = Melhores(resultadosLocais, semNotaLocal, chavesSemNotaLocal);
        var melhoresImportados = Melhores(importado.Resultados ?? new List<BenchResultado>(), semNotaImportado, chavesSemNotaImportado);

        var linhas = new List<LinhaComparacao>();
        var soLocal = new List<string>();
        var soImportado = new List<string>();

        foreach (string chave in melhoresLocais.Keys.OrderBy(k => k, StringComparer.Ordinal))
        {
            BenchResultado local_ = melhoresLocais[chave];

            if (!melhoresImportados.TryGetValue(chave, out BenchResultado? importado_))
            {
                // O teste existe no outro PC, mas falhou lá: isso já é dito
                // como "sem nota", não como "só existe nesta máquina".
                if (!chavesSemNotaImportado.Contains(chave)) soLocal.Add(BenchCatalogo.Nome(chave));
                continue;
            }

            double notaLocal = local_.NotaOuZero;
            double notaImportada = importado_.NotaOuZero;
            double diferenca = notaImportada > 0
                ? (notaLocal - notaImportada) / notaImportada * 100.0
                : 0;

            linhas.Add(new LinhaComparacao
            {
                Chave = chave,
                Teste = BenchCatalogo.Nome(chave),
                NotaLocal = notaLocal,
                NotaImportada = notaImportada,
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

            // Mesma regra do outro lado: o teste existe aqui, mas falhou
            // nesta máquina — já é dito como "sem nota".
            if (!chavesSemNotaLocal.Contains(chave)) soImportado.Add(BenchCatalogo.Nome(chave));
        }

        return new RelatorioComparacao
        {
            MaquinaLocal = local,
            MaquinaImportada = maquinaImportada,
            Diferencas = DiferencasDeHardware(local, maquinaImportada),
            Linhas = linhas,
            ExportadoEm = importado.ExportadoEm,
            AppOrigem = importado.App ?? "",
            SoLocal = soLocal,
            SoImportado = soImportado,
            SemNotaLocal = semNotaLocal,
            SemNotaImportado = semNotaImportado
        };
    }

    /// <summary>
    /// O melhor resultado de cada teste (em todos os benchmarks, nota maior é
    /// melhor). É o que se compara: o pico de cada PC, como no Cinebench.
    ///
    /// <para>
    /// Resultado sem nota utilizável (o benchmark rodou, mas falhou) fica
    /// fora da comparação — comparar nota quebrada ou zero seria mostrar
    /// número errado. O teste entra na lista de "sem nota" para o app
    /// avisar, em vez de sumir.
    /// </para>
    /// </summary>
    private static Dictionary<string, BenchResultado> Melhores(IEnumerable<BenchResultado> resultados, List<string> semNota, HashSet<string> chavesSemNota)
    {
        var melhores = new Dictionary<string, BenchResultado>(StringComparer.Ordinal);
        var invalidas = new HashSet<string>(StringComparer.Ordinal);

        foreach (BenchResultado resultado in resultados)
        {
            if (resultado == null || string.IsNullOrEmpty(resultado.Chave)) continue;

            if (!BenchResultado.NotaUtilizavel(resultado.Nota))
            {
                invalidas.Add(resultado.Chave);
                continue;
            }

            if (!melhores.TryGetValue(resultado.Chave, out BenchResultado? atual) || resultado.NotaOuZero > atual.NotaOuZero)
            {
                melhores[resultado.Chave] = resultado;
            }
        }

        // Só entra na lista de "sem nota" o teste que NÃO tem nota boa em
        // nenhuma das execuções — um teste que rodou bem uma vez tem nota.
        foreach (string chave in invalidas.OrderBy(c => c, StringComparer.Ordinal))
        {
            chavesSemNota.Add(chave);
            if (!melhores.ContainsKey(chave)) semNota.Add(BenchCatalogo.Nome(chave));
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
