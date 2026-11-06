namespace TranalSentinel;

/// <summary>Uma regra de alerta que o usuário define (vigiar um valor, avisar se passar do limite).</summary>
public sealed class RegraAlerta
{
    /// <summary>Identificador estável (gerado quando a regra é criada).</summary>
    public string Id { get; set; } = "";

    /// <summary>A chave do catálogo do sensor vigiado (ex.: "gpu-temp").</summary>
    public string Sensor { get; set; } = "";

    /// <summary>A comparação usada: "&gt;=" ou "&lt;=".</summary>
    public string Operacao { get; set; } = ">=";

    /// <summary>O valor que dispara o aviso.</summary>
    public double Limite { get; set; }

    /// <summary>Quanto tempo a condição precisa valer antes de avisar (0 = na hora).</summary>
    public int SegundosMinimos { get; set; }

    /// <summary>Regra desligada não vigia nada.</summary>
    public bool Ativa { get; set; } = true;

    /// <summary>Quão grave é o aviso quando a regra dispara.</summary>
    public Severidade Severidade { get; set; } = Severidade.Medio;

    /// <summary>Nome que o usuário deu à regra (null = usa o do catálogo).</summary>
    public string? Rotulo { get; set; }
}

/// <summary>
/// As três regras que o app já usava com limite fixo no código, agora
/// configuráveis pelo usuário.
/// </summary>
public static class RegrasAlertaPadrao
{
    /// <summary>As regras padrão de fábrica (CPU 85 °C, disco 60 °C, bateria 20 %).</summary>
    public static List<RegraAlerta> Criar() => new()
    {
        new() { Id = "padrao-cpu", Sensor = "cpu-temp", Operacao = ">=", Limite = 85, Severidade = Severidade.Alto, Rotulo = "CPU muito quente" },
        new() { Id = "padrao-disco", Sensor = "disco-temp", Operacao = ">=", Limite = 60, Severidade = Severidade.Medio, Rotulo = "Disco quente" },
        new() { Id = "padrao-bateria", Sensor = "bateria", Operacao = "<=", Limite = 20, Severidade = Severidade.Medio, Rotulo = "Bateria acabando" },
    };
}
