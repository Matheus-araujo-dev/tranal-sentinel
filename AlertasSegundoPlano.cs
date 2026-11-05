namespace TranalSentinel;

/// <summary>Um aviso que o app dá em segundo plano.</summary>
public sealed class Alerta
{
    /// <summary>Identificador estável (o mesmo alerta não repete).</summary>
    public string Id { get; init; } = "";

    public string Titulo { get; init; } = "";
    public string Texto { get; init; } = "";
    public Severidade Severidade { get; init; } = Severidade.Medio;
}

/// <summary>
/// Os avisos em segundo plano: temperatura alta, bateria acabando, disco
/// quente.
///
/// <para>
/// A regra é uma só e é pura: o que muda é o <b>estado</b> (o que já foi
/// avisado). O mesmo alerta não repete a cada ciclo, e some da lista quando
/// a condição passa — então o usuário não é martelado.
/// </para>
/// </summary>
public sealed class MonitorAlertas
{
    /// <summary>Temperatura da CPU que vira alerta (°C).</summary>
    public const double TemperaturaCpu = 85;

    /// <summary>Temperatura de disco que vira alerta (°C).</summary>
    public const double TemperaturaDisco = 60;

    /// <summary>Carga da bateria que vira alerta (%).</summary>
    public const int Bateria = 20;

    /// <summary>O estado do PC avaliado de tempos em tempos.</summary>
    public sealed record Estado(
        float? TemperaturaCpu,
        float? TemperaturaDisco,
        int? Bateria,
        bool Carregando);

    /// <summary>
    /// A maior temperatura entre os discos (null se nenhum disco reler
    /// temperatura). Só o que é <see cref="LibreHardwareMonitor.Hardware.HardwareType.Storage"/>
    /// conta — sensor de GPU ou placa-mãe não é disco.
    /// </summary>
    public static float? TemperaturaMaximaDisco(IEnumerable<SensorRow> linhas)
    {
        float? maxima = null;
        foreach (var linha in linhas)
        {
            if (linha.HardwareType != LibreHardwareMonitor.Hardware.HardwareType.Storage) continue;
            if (linha.Type != LibreHardwareMonitor.Hardware.SensorType.Temperature) continue;
            if (linha.RawValue is float v && (maxima is null || v > maxima)) maxima = v;
        }
        return maxima;
    }

    private readonly Dictionary<string, Alerta> _ativos = new();

    /// <summary>Disparado quando um alerta começa (uma vez por episódio).</summary>
    public event Action<Alerta>? Alertar;

    /// <summary>Os alertas que estão valendo agora.</summary>
    public IReadOnlyList<Alerta> Ativos => _ativos.Values.ToList();

    /// <summary>
    /// Avalia o estado e avisa o que é novo. Devolve os alertas que
    /// acabaram de aparecer (o que já estava valendo não repete).
    /// </summary>
    public IReadOnlyList<Alerta> Verificar(Estado estado)
    {
        var candidatos = new List<Alerta>();

        if (estado.TemperaturaCpu is float cpu && cpu >= TemperaturaCpu)
            candidatos.Add(new Alerta
            {
                Id = "temperatura-cpu",
                Titulo = "CPU muito quente",
                Texto = $"A CPU chegou a {cpu:0} °C. Dê uma olhada na ventilação e no cooler.",
                Severidade = Severidade.Alto
            });

        if (estado.TemperaturaDisco is float disco && disco >= TemperaturaDisco)
            candidatos.Add(new Alerta
            {
                Id = "temperatura-disco",
                Titulo = "Disco quente",
                Texto = $"Um disco chegou a {disco:0} °C.Melhore a ventilação da caixa.",
                Severidade = Severidade.Medio
            });

        if (estado.Bateria is int bateria && bateria <= Bateria && !estado.Carregando)
            candidatos.Add(new Alerta
            {
                Id = "bateria-baixa",
                Titulo = "Bateria acabando",
                Texto = $"A bateria está em {bateria}% e o PC está fora da tomada.",
                Severidade = Severidade.Medio
            });

        var novos = new List<Alerta>();
        foreach (var alerta in candidatos)
        {
            if (_ativos.ContainsKey(alerta.Id)) continue;   // já estava avisado
            _ativos[alerta.Id] = alerta;
            novos.Add(alerta);
            try { Alertar?.Invoke(alerta); }
            catch { /* um aviso que falha não pode derrubar a vigilância */ }
        }

        // O que não está mais valendo sai da lista (pode avisar de novo depois).
        foreach (var id in _ativos.Keys.ToList())
            if (candidatos.All(a => a.Id != id))
                _ativos.Remove(id);

        return novos;
    }
}
