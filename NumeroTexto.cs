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
        if (!double.IsFinite(valor)) return "—";
        return valor.ToString("0.##", CultureInfo.InvariantCulture).Replace('.', ',');
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
    /// </summary>
    public static string Numero(double valor, int casas)
    {
        if (!double.IsFinite(valor)) return "—";
        int fixas = Math.Clamp(casas, 0, 15);
        return valor.ToString("F" + fixas, CultureInfo.InvariantCulture).Replace('.', ',');
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

    /// <summary>Um número em porcentagem (47%).</summary>
    public static string Percentual(double valor) => Numero(valor) + "%";

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
