using System.Globalization;

namespace TranalSentinel;

/// <summary>
/// O rótulo de um dia: "hoje", "ontem" ou a data.
///
/// <para>
/// A regra mora aqui porque dois seletores precisam dela (o do TRANAL USO
/// e o do TRANAL REDE) — e duas versões da mesma regra divergem: uma
/// escrevia "HOJE" e a outra "hoje", e a página mostrava um dia que não
/// existia. A diferença de caixa (maiúscula no título do cartão, minúscula
/// na lista) é a única coisa que separa as duas formas.
/// </para>
/// </summary>
public static class DiaTexto
{
    /// <summary>
    /// O rótulo do dia, como ele aparece na lista: "hoje", "ontem" ou a
    /// data.
    /// </summary>
    /// <param name="dia">O dia do rótulo.</param>
    /// <param name="hoje">O dia de hoje.</param>
    public static string Rotulo(DateTime dia, DateTime hoje)
    {
        DateTime alvo = dia.Date;
        DateTime hj = hoje.Date;

        if (alvo == hj) return "hoje";
        if (alvo == hj.AddDays(-1)) return "ontem";

        return alvo.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// O rótulo do dia em caixa alta: "HOJE", "ONTEM" ou a data (a data
    /// não tem letra, então a caixa não muda nada nela).
    /// </summary>
    public static string RotuloMaiusculo(DateTime dia, DateTime hoje) =>
        Rotulo(dia, hoje).ToUpperInvariant();
}

/// <summary>
/// Um dia que os seletores de dia oferecem.
///
/// <para>
/// O <c>ToString</c> é o rótulo, e não o registro inteiro: o ComboBox da
/// casa (o estilo <c>DarkCombo</c>) escreve o item selecionado como texto
/// puro, e sem esta sobrescrita a caixa fechada mostrava
/// "OpcaoDia { Data = ... }" em vez do dia.
/// </para>
/// </summary>
/// <param name="Data">O dia.</param>
/// <param name="Rotulo">O texto que a pessoa lê.</param>
public sealed record OpcaoDia(DateTime Data, string Rotulo)
{
    /// <summary>O texto que o seletor mostra.</summary>
    public override string ToString() => Rotulo;
}
