namespace TranalSentinel;

/// <summary>Os valores que o usuário pode mandar vigiar.</summary>
public sealed class SensorCatalogoItem
{
    /// <summary>A chave da regra que aponta para este sensor (ex.: "gpu-temp").</summary>
    public string Chave { get; init; } = "";

    /// <summary>O nome que aparece na tela.</summary>
    public string Rotulo { get; init; } = "";

    /// <summary>A unidade do valor (°C, %, MHz).</summary>
    public string Unidade { get; init; } = "";

    /// <summary>A comparação usada por padrão (">=" ou "&lt;=").</summary>
    public string OperacaoPadrao { get; init; } = ">=";

    /// <summary>O limite sugerido quando o usuário cria a regra.</summary>
    public double LimitePadrao { get; init; }

    /// <summary>A severidade sugerida quando o usuário cria a regra.</summary>
    public Severidade SeveridadePadrao { get; init; } = Severidade.Medio;

    /// <summary>O que o usuário pode fazer a respeito (o texto de dica).</summary>
    public string Dica { get; init; } = "";

    /// <summary>Lê o valor atual (null quando o sensor não existe nesta máquina).</summary>
    public Func<ContextoSensores, float?> Ler { get; init; } = _ => null;

    /// <summary>
    /// O texto que o seletor mostra.
    ///
    /// <para>
    /// O ComboBox da casa (o estilo <c>DarkCombo</c>) escreve o item
    /// selecionado como texto puro: sem esta sobrescrita, a caixa fechada
    /// da página de alertas mostra o item inteiro ("SensorCatalogoItem {
    /// Chave = ... }") em vez do nome do sensor.
    /// </para>
    /// </summary>
    public override string ToString() => Rotulo;
}

/// <summary>Os valores que o app consegue ler para vigiar.</summary>
public sealed class ContextoSensores
{
    public float? TemperaturaCpu { get; init; }
    public float? TemperaturaGpu { get; init; }
    public float? TemperaturaDisco { get; init; }
    public float? CargaCpu { get; init; }
    public float? CargaGpu { get; init; }
    public float? ClockCpu { get; init; }
    public float? ClockGpu { get; init; }
    public float? UsoRam { get; init; }
    public int? Bateria { get; init; }
    public bool Carregando { get; init; }

    /// <summary>
    /// As ventoinhas que o app pode vigiar agora.
    ///
    /// <para>
    /// A lista é montada a cada ciclo e muda conforme o hardware: uma
    /// ventoinha que não está dando leitura não entra — e a regra que a
    /// vigia fica sem valor naquele momento (o mesmo que acontece com um
    /// sensor que sumiu).
    /// </para>
    /// </summary>
    public IReadOnlyList<ContextoVentoinha> Ventoinhas { get; init; } = Array.Empty<ContextoVentoinha>();
}

/// <summary>
/// Uma ventoinha que o app pode vigiar (o que a regra do usuário acompanha).
/// </summary>
/// <param name="Id">A identidade estável da ventoinha.</param>
/// <param name="Rotulo">O nome que o usuário lê ("Fan #1 (ITE IT8655E)").</param>
/// <param name="Rpm">As rotações por minuto de agora.</param>
public sealed record ContextoVentoinha(string Id, string Rotulo, float Rpm);

/// <summary>
/// O catálogo dos sensores vigiáveis: a fonte única do que dá para
/// acompanhar.
///
/// <para>
/// A bateria não entra aqui de propósito: o valor dela só vale quando o PC
/// está fora da tomada, e isso é decidido pelo motor de alertas (e não pelo
/// catálogo).
/// </para>
/// </summary>
public static class SensorCatalogo
{
    /// <summary>Todos os sensores que podem virar regra de alerta.</summary>
    public static IReadOnlyList<SensorCatalogoItem> Itens { get; } = new[]
    {
        new SensorCatalogoItem
        {
            Chave = "cpu-temp",
            Rotulo = "Temperatura da CPU",
            Unidade = "°C",
            OperacaoPadrao = ">=",
            LimitePadrao = 85,
            SeveridadePadrao = Severidade.Alto,
            Dica = "Dê uma olhada na ventilação e no cooler.",
            Ler = ctx => ctx.TemperaturaCpu,
        },
        new SensorCatalogoItem
        {
            Chave = "gpu-temp",
            Rotulo = "Temperatura da GPU",
            Unidade = "°C",
            OperacaoPadrao = ">=",
            LimitePadrao = 85,
            SeveridadePadrao = Severidade.Alto,
            Dica = "Melhore a ventilação da placa de vídeo.",
            Ler = ctx => ctx.TemperaturaGpu,
        },
        new SensorCatalogoItem
        {
            Chave = "disco-temp",
            Rotulo = "Temperatura do disco",
            Unidade = "°C",
            OperacaoPadrao = ">=",
            LimitePadrao = 60,
            SeveridadePadrao = Severidade.Medio,
            Dica = "Melhore a ventilação da caixa.",
            Ler = ctx => ctx.TemperaturaDisco,
        },
        new SensorCatalogoItem
        {
            Chave = "ram-uso",
            Rotulo = "Uso da memória RAM",
            Unidade = "%",
            OperacaoPadrao = ">=",
            LimitePadrao = 90,
            SeveridadePadrao = Severidade.Medio,
            Dica = "Feche abas e apps pesados; reduza o autostart.",
            Ler = ctx => ctx.UsoRam,
        },
        new SensorCatalogoItem
        {
            Chave = "cpu-carga",
            Rotulo = "Carga da CPU",
            Unidade = "%",
            OperacaoPadrao = ">=",
            LimitePadrao = 95,
            SeveridadePadrao = Severidade.Baixo,
            Dica = "A CPU está no limite — normal em benchmark.",
            Ler = ctx => ctx.CargaCpu,
        },
        new SensorCatalogoItem
        {
            Chave = "gpu-carga",
            Rotulo = "Carga da GPU",
            Unidade = "%",
            OperacaoPadrao = ">=",
            LimitePadrao = 95,
            SeveridadePadrao = Severidade.Baixo,
            Dica = "A GPU está no limite.",
            Ler = ctx => ctx.CargaGpu,
        },
        new SensorCatalogoItem
        {
            Chave = "cpu-clock",
            Rotulo = "Clock da CPU",
            Unidade = "MHz",
            OperacaoPadrao = "<=",
            LimitePadrao = 2000,
            SeveridadePadrao = Severidade.Medio,
            Dica = "O clock da CPU caiu — pode ser limite térmico ou de energia.",
            Ler = ctx => ctx.ClockCpu,
        },
        new SensorCatalogoItem
        {
            Chave = "gpu-clock",
            Rotulo = "Clock da GPU",
            Unidade = "MHz",
            OperacaoPadrao = "<=",
            LimitePadrao = 200,
            SeveridadePadrao = Severidade.Medio,
            Dica = "O clock da GPU caiu — pode ser limite térmico.",
            Ler = ctx => ctx.ClockGpu,
        },
    };

    /// <summary>Procura um sensor pela chave (null quando não existe).</summary>
    public static SensorCatalogoItem? PorChave(string chave) =>
        Itens.FirstOrDefault(i => i.Chave == chave);
}
