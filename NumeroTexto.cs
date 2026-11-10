using System.Globalization;

namespace TranalSentinel;

/// <summary>
/// A formatação dos números mostrados na tela, em um lugar só.
///
/// <para>
/// O TRANAL SENTINEL mostra os mesmos números em vários lugares (o cartão do
/// tráfego, a aba de memória, a comparação entre PCs, o resumo dos sensores)
/// e o computador do usuário pode estar em português ou em inglês. Se cada
/// lugar tiver o seu <c>ToString</c>, os mesmos números saem com formatos
/// diferentes — e o que é pior, saem diferentes <b>na máquina do dono</b> e na
/// máquina de integração, que roda em en-US.
///
/// <para>
/// Por isso a formatação é invariante de propósito: o número sai sempre com
/// vírgula decimal e ponto de milhar, aconteça o que acontecer com a cultura
/// do Windows.
///
/// <para>
/// Um número que não é finito (infinito, NaN) não vira texto de número: ele
/// vira "—". Número errado é pior que número ausente.
/// </para>
/// </summary>
public static class NumeroTexto
{
    /// <summary>
    /// O texto de um número que não deu para ler (o travessão).
    ///
    /// <para>
    /// É a única definição do glifo no app: o <see cref="SensorTexto.Ausente"/>
    /// aponta para esta mesma constante.
    /// </para>
    /// </summary>
    public const string Ausente = "—";

    /// <summary>
    /// Número no padrão brasileiro (vírgula decimal), sem casas demais.
    ///
    /// <para>
    /// A troca do ponto pela vírgula é feita à mão: a cultura invariante usa o
    /// ponto, e o padrão brasileiro usa a vírgula — mas a máquina de
    /// integração roda em en-US, onde a vírgula é o separador de milhar.
    /// </para>
    /// </summary>
    public static string Numero(double valor)
    {
        if (!double.IsFinite(valor)) return Ausente;

        string texto = valor.ToString("0.##", CultureInfo.InvariantCulture);

        // O formato "0.##" arredonda para duas casas. Com números muito
        // grandes, esse arredondamento muda o próprio número: a dupla não
        // guarda as duas casas E o algarismo dos trilhões ao mesmo tempo, e
        // o texto sai com um algarismo trocado. Quando os dígitos inteiros
        // mudaram, o texto sai pelo formato fixo, que é exato.
        if (!PreservaOsDigitosInteiros(valor, texto)) texto = TextoExato(valor);

        return SemSinalDeZero(texto.Replace('.', ','));
    }

    /// <summary>
    /// Número no padrão brasileiro (vírgula decimal) com um número fixo de
    /// casas decimais ("8,0", "1,234").
    ///
    /// <para>
    /// O <see cref="Numero(double)"/> corta os zeros à direita (formato
    /// "0.##"), e isso não serve para as unidades que pedem casas fixas
    /// (gigabytes, volts, gigahertz). Esta sobrecarga garante as casas, no
    /// mesmo padrão do app: vírgula decimal e independente da cultura do
    /// Windows.
    /// </para>
    ///
    /// <para>
    /// As casas são limitadas a 0..15: o formato "F" do <see cref="double"/>
    /// não passa de 15 casas decimais, e casa negativa não existe. Pedir fora
    /// da faixa não quebra nada — o pedido é ajustado.
    /// </para>
    /// </summary>
    public static string Numero(double valor, int casas)
    {
        if (!double.IsFinite(valor)) return Ausente;
        int fixas = Math.Clamp(casas, 0, 15);
        string texto = valor.ToString("F" + fixas, CultureInfo.InvariantCulture).Replace('.', ',');
        return SemSinalDeZero(texto);
    }

    /// <summary>Tira o sinal de um texto que é zero ("-0", "-0,000").</summary>
    ///
    /// <para>
    /// A regra do app é: o número formatado não leva sinal quando ele é zero.
    /// O sinal sai do texto já formatado, sem mexer nos dígitos — mudar o valor
    /// passado ao <c>ToString</c> mudaria o arredondamento das outras casas.
    /// </para>
    private static string SemSinalDeZero(string texto)
    {
        if (!texto.StartsWith("-", StringComparison.Ordinal)) return texto;
        string resto = texto.Substring(1);
        foreach (char c in resto)
        {
            if (c != '0' && c != ',') return texto;
        }
        return resto;
    }

    /// <summary>
    /// O texto ainda tem os mesmos dígitos inteiros do número?
    /// </summary>
    ///
    /// <para>
    /// O "0.##" corta as casas, mas não pode trocar o algarismo da parte
    /// inteira. Quando ele troca, o número grande saiu errado — e a casa
    /// prefere o número exato ao número arredondado.
    /// </para>
    /// </summary>
    private static bool PreservaOsDigitosInteiros(double valor, string texto)
    {
        // Fora do inteiro exato do double não dá para comparar: o número já
        // não cabe em long, e o texto exato sai de qualquer jeito.
        if (Math.Abs(valor) >= 9.007199254740991E15) return false;

        int ponto = texto.IndexOf('.');
        string digitos = ponto < 0 ? texto : texto.Substring(0, ponto);
        if (digitos.StartsWith("-", StringComparison.Ordinal)) digitos = digitos.Substring(1);

        // Se não deu para ler os dígitos do texto, não se mexe: não há o que
        // comparar, e o texto fica como está.
        if (!long.TryParse(digitos, NumberStyles.None, CultureInfo.InvariantCulture, out long inteiro))
        {
            return true;
        }

        return inteiro == (long)Math.Truncate(Math.Abs(valor));
    }

    /// <summary>
    /// O texto exato do número, em ponto fixo (sem notação científica).
    /// </summary>
    ///
    /// <para>
    /// A casa vai subindo até o texto devolver o mesmo número de volta: é
    /// o menor texto que não perde nenhum dígito.
    /// </para>
    /// </summary>
    private static string TextoExato(double valor)
    {
        for (int casas = 0; casas <= 15; casas++)
        {
            string texto = valor.ToString("F" + casas, CultureInfo.InvariantCulture);
            if (double.TryParse(texto, NumberStyles.Float, CultureInfo.InvariantCulture, out double volta)
                && volta == valor)
            {
                return texto;
            }
        }

        return valor.ToString("F15", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Um número inteiro, com o separador de milhar do padrão brasileiro.
    ///
    /// <para>
    /// "N0" com cultura invariante separa os milhares com vírgula; o padrão
    /// brasileiro usa ponto. A troca é feita à mão para que o texto saia igual
    /// no PC do dono e na máquina de integração.
    /// </para>
    /// </summary>
    public static string Inteiro(long valor) =>
        valor.ToString("N0", CultureInfo.InvariantCulture).Replace(",", ".");

    /// <summary>
    /// Uma data e hora no padrão brasileiro, sem depender da cultura do
    /// Windows (09/11/2026 14:32).
    /// </summary>
    public static string DataHora(DateTime momento) =>
        momento.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);

    /// <summary>Uma data no padrão brasileiro, sem depender da cultura do Windows (09/11/2026).</summary>
    public static string Data(DateTime dia) =>
        dia.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
}
