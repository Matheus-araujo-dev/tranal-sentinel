using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace TranalSentinel;

/// <summary>
/// A nota de saúde do PC: um número de 0 a 100 e o conceito de escola (A a
/// E) que vai junto com ele.
/// </summary>
public sealed record NotaSaude
{
    /// <summary>A nota em si (de 0 a 100).</summary>
    public int Nota { get; init; }

    /// <summary>
    /// O conceito (A a E). Sai da nota pela regra única
    /// (<see cref="SaudeNota.Conceito"/>): guardar o conceito junto do número
    /// criaria duas verdades para a mesma coisa.
    /// </summary>
    public string Conceito => SaudeNota.Conceito(Nota);

    /// <summary>Uma frase curta para a interface (ex.: "nota 78 (C)").</summary>
    public string Resumo { get; init; } = "";
}

/// <summary>
/// A nota de saúde do PC: transforma os achados do diagnóstico em um número
/// de 0 a 100 e no conceito de escola (A a E).
///
/// <para>
/// É função pura: recebe os achados e devolve a nota. Nenhuma leitura de
/// disco, nenhuma chamada ao Windows — é o que os testes cobrem. A parte que
/// guarda a nota na linha do tempo é outra peça
/// (<see cref="SaudeNotaHistorico"/>).
/// </para>
///
/// <para>
/// A contagem é uma só: começa em 100 e cada achado desconta um peso. O peso
/// de cada severidade mora em um lugar só (<see cref="Desconto"/>), e o
/// conceito mora em um lugar só (<see cref="Conceito"/>).
/// </para>
/// </summary>
public static class SaudeNota
{
    /// <summary>
    /// Quanto cada severidade desconta da nota: o que pesa mais dói mais.
    /// </summary>
    public static int Desconto(Severidade severidade) => severidade switch
    {
        Severidade.Critico => 20,
        Severidade.Alto => 10,
        Severidade.Medio => 5,
        Severidade.Baixo => 2,
        _ => 0,
    };

    /// <summary>
    /// Conceito a partir da nota: 90 ou mais é A, 75 é B, 60 é C, 40 é D,
    /// abaixo de 40 é E.
    /// </summary>
    public static string Conceito(int nota) => nota switch
    {
        >= 90 => "A",
        >= 75 => "B",
        >= 60 => "C",
        >= 40 => "D",
        _ => "E",
    };

    /// <summary>
    /// Nota a partir dos achados, já limitada a 0..100 (nunca negativa,
    /// nunca acima de 100).
    /// </summary>
    public static int CalcularNota(IReadOnlyList<Achado>? achados)
    {
        int nota = 100;
        if (achados is null) return nota;
        foreach (var achado in achados)
            nota -= Desconto(achado.Severidade);
        return Math.Clamp(nota, 0, 100);
    }

    /// <summary>
    /// Nota, conceito e resumo de uma frase, a partir dos achados.
    /// </summary>
    public static NotaSaude Calcular(IReadOnlyList<Achado>? achados)
    {
        var lista = achados ?? Array.Empty<Achado>();
        int nota = CalcularNota(lista);
        return new NotaSaude
        {
            Nota = nota,
            Resumo = Resumo(lista, nota),
        };
    }

    /// <summary>
    /// Uma frase para a interface, com a contagem dos achados e a nota.
    /// </summary>
    public static string Resumo(IReadOnlyList<Achado>? achados, int nota)
    {
        var lista = achados ?? Array.Empty<Achado>();
        // O texto com as contagens ("N achado(s): X crítico(s)...") mora em
        // RelatorioSaude.Resumo: aqui entra só a nota. A mesma frase escrita
        // duas vezes diverge na primeira alteração.
        var relatorio = new RelatorioSaude();
        relatorio.Achados.AddRange(lista);
        string baseTexto = relatorio.Resumo.TrimEnd('.');
        return $"{baseTexto} — nota {nota} ({Conceito(nota)})";
    }

    /// <summary>
    /// Direção da nota em relação à nota anterior.
    /// </summary>
    /// <remarks>
    /// Formato: "sem histórico" (não há nota anterior), "estável",
    /// "subiu 5 ponto(s)" ou "caiu 3 ponto(s)".
    /// </remarks>
    public static string Tendencia(int notaAtual, int? notaAnterior)
    {
        if (notaAnterior is null) return "sem histórico";
        int delta = notaAtual - notaAnterior.Value;
        if (delta == 0) return "estável";
        if (delta > 0) return $"subiu {delta} ponto{(delta == 1 ? "" : "s")}";
        return $"caiu {-delta} ponto{(delta == -1 ? "" : "s")}";
    }
}

/// <summary>Uma nota guardada em um dia do histórico.</summary>
public sealed record EntradaNotaSaude(DateTime Dia, int Nota);

/// <summary>
/// O histórico da nota: uma linha por dia em um arquivo de texto, para
/// mostrar a tendência ao longo do tempo.
///
/// <para>
/// O arquivo fica na mesma pasta de log do diário do app
/// (<c>%AppData%\TranalSentinel\log\</c>) e segue o mesmo estilo: texto
/// simples, uma linha por registro, UTF-8 e escrita silenciosa. O formato da
/// linha é <c>AAAA-MM-DD;nota</c> (ex.: <c>2026-11-05;78</c>). O conceito
/// não é gravado: ele sai da nota pela mesma regra de sempre.
/// </para>
///
/// <para>
/// Gravar duas vezes no mesmo dia atualiza a linha do dia — não duplica.
/// </para>
///
/// <para>
/// Nada aqui lança: o histórico é um arquivo de apoio. Se ele não puder ser
/// lido ou gravado, o app mostra a nota de hoje e mais nada — derrubar o
/// app por causa de um arquivo de apoio seria um preço alto demais.
/// </para>
/// </summary>
public static class SaudeNotaHistorico
{
    /// <summary>
    /// Redireciona a pasta do histórico (só os testes usam). Vazio = a pasta
    /// de log de verdade.
    /// </summary>
    internal static string? PastaOverride { get; set; }

    /// <summary>Pasta onde fica o arquivo do histórico.</summary>
    public static string Pasta => string.IsNullOrWhiteSpace(PastaOverride)
        ? Path.Combine(AppSettings.Folder, "log")
        : PastaOverride!;

    /// <summary>Arquivo com uma linha por dia.</summary>
    public static string Arquivo => Path.Combine(Pasta, "tranal-saude-nota.log");

    /// <summary>Quantos dias de histórico são mantidos. O mais antigo é apagado.</summary>
    public const int DiasMantidos = 30;

    /// <summary>
    /// Grava a nota do dia. Se já houver entrada do mesmo dia, ela é
    /// atualizada (não duplicada).
    /// </summary>
    public static void Gravar(DateTime dia, int nota)
    {
        try
        {
            Directory.CreateDirectory(Pasta);

            var diaLimpo = dia.Date;
            var entradas = LerArquivo();
            entradas.RemoveAll(e => e.Dia == diaLimpo);
            entradas.Add(new EntradaNotaSaude(diaLimpo, Math.Clamp(nota, 0, 100)));
            entradas.Sort((a, b) => a.Dia.CompareTo(b.Dia));
            if (entradas.Count > DiasMantidos)
                entradas = entradas.GetRange(entradas.Count - DiasMantidos, DiasMantidos);

            var linhas = entradas.Select(e => $"{e.Dia:yyyy-MM-dd};{e.Nota}");
            File.WriteAllText(Arquivo, string.Join(Environment.NewLine, linhas) + Environment.NewLine, Encoding.UTF8);
        }
        catch
        {
            // Disco cheio, arquivo preso pelo antivírus, pasta sem permissão:
            // o histórico é acessório. Perder a gravação não pode derrubar o app.
        }
    }

    /// <summary>Grava a nota de hoje.</summary>
    public static void GravarHoje(int nota) => Gravar(DateTime.Today, nota);

    /// <summary>
    /// Lê as últimas entradas do histórico, da mais antiga para a mais
    /// recente. Devolve lista vazia se não houver arquivo (ou se ele não
    /// puder ser lido).
    /// </summary>
    public static List<EntradaNotaSaude> Ler(int ultimas = DiasMantidos)
    {
        var entradas = LerArquivo();
        if (ultimas <= 0) return new List<EntradaNotaSaude>();
        if (entradas.Count > ultimas)
            entradas = entradas.GetRange(entradas.Count - ultimas, ultimas);
        return entradas;
    }

    /// <summary>
    /// Direção da nota de hoje em relação à nota anterior mais recente (a de
    /// ontem, ou a mais recente que existir antes de hoje).
    /// </summary>
    public static string TendenciaDe(int notaAtual)
    {
        var anterior = Ler().LastOrDefault(e => e.Dia < DateTime.Today);
        return SaudeNota.Tendencia(notaAtual, anterior?.Nota);
    }

    /// <summary>
    /// Lê o arquivo do histórico. Linhas fora do formato são ignoradas: um
    /// arquivo meio escrito não pode estragar a leitura toda.
    /// </summary>
    private static List<EntradaNotaSaude> LerArquivo()
    {
        var lista = new List<EntradaNotaSaude>();
        try
        {
            if (!File.Exists(Arquivo)) return lista;
            foreach (string linha in File.ReadAllLines(Arquivo, Encoding.UTF8))
            {
                var partes = linha.Split(';');
                if (partes.Length < 2) continue;
                if (!DateTime.TryParseExact(partes[0].Trim(), "yyyy-MM-dd",
                        CultureInfo.InvariantCulture, DateTimeStyles.None, out var dia)) continue;
                if (!int.TryParse(partes[1].Trim(), NumberStyles.Integer,
                        CultureInfo.InvariantCulture, out int nota)) continue;
                lista.Add(new EntradaNotaSaude(dia, Math.Clamp(nota, 0, 100)));
            }
        }
        catch
        {
            // Arquivo ilegível, bloqueado ou corrompido: histórico vazio. A
            // nota de hoje continua valendo; o passado é acessório.
            return new List<EntradaNotaSaude>();
        }
        return lista;
    }
}
