using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace TranalSentinel;

/// <summary>
/// TRANAL COMPARAR na linha de comando: exporta os benchmarks desta máquina
/// para um arquivo e importa o arquivo de outro PC, comparando os dois.
///
/// <para>
/// Uso: <c>TranalSentinel.exe --comparar exportar &lt;arquivo&gt;</c>,
/// <c>--comparar importar &lt;arquivo&gt;</c> e
/// <c>--comparar comparar &lt;arquivoA&gt; &lt;arquivoB&gt;</c>.
///
/// É o mesmo caminho do app (mesmo formato, mesma regra de comparação), sem
/// abrir janela. Serve para passar o arquivo para o outro PC por script e
/// para conferir a comparação sem passar pela interface.
/// </para>
/// </summary>
public static class CompararCli
{
    /// <summary>Deu certo.</summary>
    public const int CodigoOk = 0;

    /// <summary>Erro de execução (arquivo inválido, não encontrado...).</summary>
    public const int CodigoErro = 1;

    /// <summary>Texto de uso.</summary>
    public static string TextoUso => """
        TRANAL SENTINEL · COMPARAR BENCHMARKS (linha de comando)

        Uso:
          TranalSentinel.exe --comparar exportar <arquivo>
          TranalSentinel.exe --comparar importar <arquivo>
          TranalSentinel.exe --comparar comparar <arquivoA> <arquivoB>

        Exportar grava os resultados de benchmark desta máquina (e a
        identificação dela) no arquivo. Importar lê o arquivo de outro PC e
        mostra a comparação com os resultados desta máquina. Comparar faz o
        mesmo entre dois arquivos, sem precisar de benchmark nesta máquina.
        """;

    /// <summary>Executa a CLI de comparação.</summary>
    public static int Executar(string[] args)
    {
        try
        {
            Console.OutputEncoding = Encoding.UTF8;

            int indice = Array.FindIndex(args, a => string.Equals(a, "--comparar", StringComparison.OrdinalIgnoreCase));
            if (indice < 0)
            {
                Console.WriteLine(TextoUso);
                return CodigoErro;
            }

            string acao = indice + 1 < args.Length ? args[indice + 1].ToLowerInvariant() : "";
            string caminho = indice + 2 < args.Length ? args[indice + 2] : "";

            if (string.IsNullOrWhiteSpace(caminho))
            {
                Console.WriteLine("Falta o nome do arquivo.");
                Console.WriteLine();
                Console.WriteLine(TextoUso);
                return CodigoErro;
            }

            if (acao == "exportar") return Exportar(caminho);
            if (acao == "importar") return Importar(caminho: caminho);

            if (acao == "comparar")
            {
                string caminhoB = indice + 3 < args.Length ? args[indice + 3] : "";
                if (string.IsNullOrWhiteSpace(caminhoB))
                {
                    Console.WriteLine("Falta o segundo arquivo.");
                    Console.WriteLine();
                    Console.WriteLine(TextoUso);
                    return CodigoErro;
                }

                return CompararDois(caminho, caminhoB);
            }

            Console.WriteLine("Ação desconhecida: " + acao);
            Console.WriteLine();
            Console.WriteLine(TextoUso);
            return CodigoErro;
        }
        catch (Exception ex)
        {
            Console.WriteLine("Falhou: " + ex.Message);
            return CodigoErro;
        }
    }

    private static int Exportar(string caminho)
    {
        AppSettings settings = AppSettings.Load();

        BenchArquivo arquivo = BenchIntercambio.Montar(
            settings.BenchRuns ?? new List<BenchRun>(),
            settings.BenchRecords ?? new List<BenchRecord>(),
            BenchIntercambio.MaquinaAtual());

        BenchIntercambio.Gravar(arquivo, caminho);

        Console.WriteLine("TRANAL SENTINEL · EXPORTAÇÃO DE BENCHMARKS");
        Console.WriteLine($"Arquivo: {caminho}");
        Console.WriteLine($"PC: {arquivo.Maquina.Nome} · {arquivo.Maquina.Cpu}");
        Console.WriteLine($"Resultados exportados: {arquivo.Resultados.Count}");

        if (arquivo.Resultados.Count == 0)
        {
            Console.WriteLine();
            Console.WriteLine("Nenhum benchmark foi rodado nesta máquina ainda: rode o TRANAL BENCH antes de exportar.");
        }

        return CodigoOk;
    }

    private static int Importar(string caminho)
    {
        BenchArquivo? arquivo = BenchIntercambio.Ler(caminho, out string? erro);
        if (arquivo == null)
        {
            Console.WriteLine(erro ?? "Não deu para ler o arquivo.");
            return CodigoErro;
        }

        AppSettings settings = AppSettings.Load();

        List<BenchResultado> locais = BenchIntercambio.Resultados(
            settings.BenchRuns ?? new List<BenchRun>(),
            settings.BenchRecords ?? new List<BenchRecord>());

        if (locais.Count == 0)
        {
            Console.WriteLine("Esta máquina ainda não rodou nenhum benchmark: rode o TRANAL BENCH para ter com o que comparar.");
            Console.WriteLine("Para comparar dois arquivos sem benchmark aqui, use: --comparar comparar <arquivoA> <arquivoB>");
            Console.WriteLine();
        }

        RelatorioComparacao relatorio = BenchComparacao.Comparar(BenchIntercambio.MaquinaAtual(), arquivo, locais);
        ImprimirRelatorio(relatorio, caminho);
        return CodigoOk;
    }

    private static int CompararDois(string caminhoA, string caminhoB)
    {
        BenchArquivo? arquivoA = BenchIntercambio.Ler(caminhoA, out string? erroA);
        if (arquivoA == null)
        {
            Console.WriteLine($"Arquivo A: {erroA ?? "não deu para ler."}");
            return CodigoErro;
        }

        BenchArquivo? arquivoB = BenchIntercambio.Ler(caminhoB, out string? erroB);
        if (arquivoB == null)
        {
            Console.WriteLine($"Arquivo B: {erroB ?? "não deu para ler."}");
            return CodigoErro;
        }

        RelatorioComparacao relatorio = BenchComparacao.Comparar(arquivoA.Maquina, arquivoB, arquivoA.Resultados);
        ImprimirRelatorio(relatorio, $"{caminhoA} × {caminhoB}");
        return CodigoOk;
    }

    /// <summary>Imprime o relatório da comparação.</summary>
    private static void ImprimirRelatorio(RelatorioComparacao relatorio, string origem)
    {
        Console.WriteLine("TRANAL SENTINEL · COMPARAÇÃO DE BENCHMARKS");
        Console.WriteLine($"Arquivo: {origem}");
        if (relatorio.AppOrigem.Length > 0)
        {
            Console.WriteLine($"Origem: {relatorio.AppOrigem} · exportado em {relatorio.ExportadoEm:dd/MM/yyyy HH:mm}");
        }

        Console.WriteLine();
        Console.WriteLine($"PC A: {relatorio.MaquinaLocal.Nome} · {relatorio.MaquinaLocal.Cpu} · {MemoriaTexto.Numero(relatorio.MaquinaLocal.MemoriaGb)} GB · {relatorio.MaquinaLocal.Sistema}");
        Console.WriteLine($"PC B: {relatorio.MaquinaImportada.Nome} · {relatorio.MaquinaImportada.Cpu} · {MemoriaTexto.Numero(relatorio.MaquinaImportada.MemoriaGb)} GB · {relatorio.MaquinaImportada.Sistema}");
        Console.WriteLine();

        if (relatorio.Diferencas.Count > 0)
        {
            Console.WriteLine("Atenção — as máquinas não são iguais:");
            foreach (string diferenca in relatorio.Diferencas) Console.WriteLine("  · " + diferenca);
            Console.WriteLine();
        }

        if (relatorio.Linhas.Count == 0)
        {
            Console.WriteLine("Nenhum teste existe nos dois PCs (rode os mesmos benchmarks nos dois).");
        }
        else
        {
            Console.WriteLine($"{"Teste",-26} {"PC A",12} {"PC B",12}  Diferença");
            Console.WriteLine(new string('-', 78));

            foreach (LinhaComparacao linha in relatorio.Linhas)
            {
                Console.WriteLine($"{linha.Teste,-26} {MemoriaTexto.Numero(linha.NotaLocal),12} {MemoriaTexto.Numero(linha.NotaImportada),12}  {linha.Veredito} ({MemoriaTexto.Numero(linha.DiferencaPercentual)}%)");
            }
        }

        if (relatorio.SoLocal.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("Só existem no PC A: " + string.Join(", ", relatorio.SoLocal));
        }

        if (relatorio.SoImportado.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("Só existem no PC B: " + string.Join(", ", relatorio.SoImportado));
        }
    }
}
