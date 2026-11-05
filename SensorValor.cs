using System.Text.Json;
using System.Text.Json.Serialization;

namespace TranalSentinel;

/// <summary>
/// Regras de valor das leituras de sensores.
///
/// <para>
/// O LibreHardwareMonitor devolve <c>infinito</c> (e <c>NaN</c>) para sensores
/// que não sabem o valor — o clock de um núcleo ocioso, por exemplo. O JSON
/// padrão não aceita infinito: serializar a leitura estourava, o processo de
/// coleta engolia o erro e nunca gravava o arquivo. O app ficava sem sensor
/// nenhum e achava que o driver do PawnIO tinha falhado.
/// </para>
///
/// <para>
/// A regra é uma só, para os dois lados: o valor é normalizado na origem
/// (vira null) e a serialização aceita literal nomeado, para um arquivo
/// antigo não derrubar o app.
/// </para>
/// </summary>
public static class SensorValor
{
    /// <summary>As opções de JSON usadas para ler e escrever as leituras.</summary>
    public static readonly JsonSerializerOptions OpcoesJson = new()
    {
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
    };

    /// <summary>
    /// O valor, se for um número finito; null caso contrário (não há leitura).
    /// </summary>
    public static float? Finito(float? valor)
        => valor is float v && !float.IsFinite(v) ? null : valor;
}
