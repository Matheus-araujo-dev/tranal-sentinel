namespace TranalSentinel;

/// <summary>Quanto o app se permite perder de informação para economizar CPU e memória.</summary>
public enum PerfilRecursos
{
    /// <summary>Tudo ligado: sensores a cada segundo, overlay e histórico sempre ativos.</summary>
    Completo = 0,

    /// <summary>Meio-termo: sensores a cada 2 s, overlay só quando pede, histórico a cada 6 h.</summary>
    Equilibrado = 1,

    /// <summary>Modo leve: sensores a cada 5 s, sem overlay automático, histórico a cada 24 h.</summary>
    Economia = 2,
}

/// <summary>
/// Regras do perfil de recursos: quem decide o intervalo de cada tarefa.
/// </summary>
/// <remarks>
/// <para>
/// É uma classe de decisão pura (sem I/O, sem WPF) de propósito: os intervalos
/// mudam conforme o que o app está fazendo — benchmark de GPU, overlay na tela,
/// app ocioso — e a regra precisa ser testável sem abrir janela.
/// </para>
/// <para>
/// O objetivo do dono é "gastar o mínimo possível do processamento". A coleta de
/// sensores é o que pesa (o LibreHardwareMonitor interroga a placa a cada
/// leitura), então o perfil muda o relógio dela e não a quantidade de sensores:
/// perderia informação à toa.
/// </para>
/// </remarks>
public static class PoliticaRecursos
{
    /// <summary>Intervalo da leitura de sensores, em milissegundos.</summary>
    public static int IntervaloSensoresMs(PerfilRecursos perfil) => perfil switch
    {
        PerfilRecursos.Completo => 1000,
        PerfilRecursos.Equilibrado => 2000,
        PerfilRecursos.Economia => 5000,
        _ => 1000,
    };

    /// <summary>De quanto em quanto tempo o app grava um instantâneo do PC (linha do tempo).</summary>
    public static int IntervaloInstantaneoMinutos(PerfilRecursos perfil) => perfil switch
    {
        PerfilRecursos.Completo => 180,    // 3 h
        PerfilRecursos.Equilibrado => 360, // 6 h
        PerfilRecursos.Economia => 1440,   // 24 h
        _ => 180,
    };

    /// <summary>Se o overlay de FPS deve se atualizar sozinho enquanto o app está aberto.</summary>
    public static bool OverlayAutomatico(PerfilRecursos perfil) => perfil != PerfilRecursos.Economia;

    /// <summary>Se a leitura de saúde do PC roda sozinha quando o app abre.</summary>
    /// <remarks>
    /// A análise completa lê o log de eventos do Windows inteiro: leva segundos.
    /// Em economia isso só acontece quando o usuário pede.
    /// </remarks>
    public static bool AnaliseSaudeAoAbrir(PerfilRecursos perfil) => perfil != PerfilRecursos.Economia;

    /// <summary>Se o app grava o histórico de temperatura e uso de memória continuamente.</summary>
    public static bool GravarHistorico(PerfilRecursos perfil) => perfil != PerfilRecursos.Economia;

    /// <summary>Quantos instantâneos do PC a linha do tempo guarda.</summary>
    public static int HistoricoInstantaneos(PerfilRecursos perfil) => perfil switch
    {
        PerfilRecursos.Completo => 60,     // ~2 semanas a cada 3 h
        PerfilRecursos.Equilibrado => 60,  // ~15 dias a cada 6 h
        PerfilRecursos.Economia => 30,     // ~30 dias a cada 24 h
        _ => 60,
    };

    /// <summary>Texto curto que explica o perfil na interface.</summary>
    public static string Descricao(PerfilRecursos perfil) => perfil switch
    {
        PerfilRecursos.Completo => "Tudo ligado. Sensores a cada 1 s, histórico a cada 3 h. Use se o PC aguenta bem.",
        PerfilRecursos.Equilibrado => "Equilíbrio. Sensores a cada 2 s, histórico a cada 6 h. Padrão recomendado.",
        PerfilRecursos.Economia => "Modo leve. Sensores a cada 5 s, sem overlay automático, histórico a cada 24 h.",
        _ => "",
    };
}
