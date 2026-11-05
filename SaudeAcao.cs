namespace TranalSentinel;

/// <summary>
/// Uma ação que o app pode oferecer a partir de um achado do diagnóstico.
///
/// <para>
/// A regra do diagnóstico só <b>descreve</b> a ação (ela é pura e testável
/// sem Windows). Quem executa é o <see cref="AcaoExecutor"/>, que pergunta
/// ao <see cref="IAcaoAlvo"/> — o único lugar que mexe no sistema. Assim
/// "o que o app pode fazer" e "o que o app faz" não se misturam.
/// </para>
/// </summary>
public sealed class AcaoAplicavel
{
    /// <summary>Identificador estável da ação (quem executa conhece por ele).</summary>
    public string Id { get; init; } = "";

    /// <summary>Texto do botão.</summary>
    public string Rotulo { get; init; } = "";

    /// <summary>O que a ação faz — e o que pode dar errado.</summary>
    public string Explicacao { get; init; } = "";

    /// <summary>
    /// A ação mexe no sistema (serviço, registro, plano de energia). Então
    /// precisa de confirmação e, em geral, de administrador.
    /// </summary>
    public bool MudaSistema { get; init; }

    /// <summary>A ação tem como desfazer (aparece o botão de restaurar).</summary>
    public bool Reversivel { get; init; }

    /// <summary>Dados que a ação precisa (ex.: o nome do serviço).</summary>
    public IReadOnlyDictionary<string, string> Dados { get; init; } =
        new Dictionary<string, string>();

    /// <summary>Um valor dos <see cref="Dados"/>, ou null se não existir.</summary>
    public string? Dado(string chave)
        => Dados.TryGetValue(chave, out var v) ? v : null;
}

/// <summary>
/// O catálogo de ações do TRANAL SAÚDE.
///
/// <para>
/// Uma só fonte: a regra do diagnóstico oferece <b>o que</b> dá para fazer
/// (descrevendo o id), o executor sabe <b>como</b> fazer. Se uma ação
/// aparecer na interface sem estar aqui, é bug.
/// </para>
/// </summary>
public static class AcoesSaude
{
    /// <summary>Id da ação de desligar o ASPM.</summary>
    public const string IdAspmDesligar = "aspm-off";

    /// <summary>Id da ação de restaurar o ASPM.</summary>
    public const string IdAspmRestaurar = "aspm-restaurar";

    /// <summary>Id da ação de suspender serviços.</summary>
    public const string IdSuspenderServicos = "suspender-servicos";

    /// <summary>Id da ação que leva para a aba Inicialização.</summary>
    public const string IdAbrirInicializacao = "abrir-inicializacao";

    /// <summary>Id da ação que leva para a aba Drivers.</summary>
    public const string IdAbrirDrivers = "abrir-drivers";

    /// <summary>Id da ação que leva para a aba Otimização.</summary>
    public const string IdAbrirOtimizacao = "abrir-otimizacao";

    /// <summary>
    /// A ação de desligar o ASPM, com a explicação do custo (consome mais
    /// energia e pode aquecer a placa).
    /// </summary>
    public static AcaoAplicavel DesligarAspm() => new()
    {
        Id = IdAspmDesligar,
        Rotulo = "Desligar ASPM",
        Explicacao = "Desliga o gerenciamento de energia do link PCIe (economia de energia do barramento). "
                   + "Ajuda quando o NVMe ou a placa PCIe travam. Gasto um pouco mais de energia. "
                   + "Dá para restaurar depois.",
        MudaSistema = true,
        Reversivel = true,
    };

    /// <summary>A ação de restaurar o ASPM.</summary>
    public static AcaoAplicavel RestaurarAspm() => new()
    {
        Id = IdAspmRestaurar,
        Rotulo = "Restaurar ASPM",
        Explicacao = "Devolve o gerenciamento de energia do link PCIe ao valor que estava antes "
                   + "de o TRANAL SENTINEL mexer nele.",
        MudaSistema = true,
        Reversivel = false,
    };

    /// <summary>
    /// A ação de suspender os serviços de programas em segundo plano. Os
    /// serviços são escolhidos pelo usuário — o app não suspende nada por
    /// conta própria.
    /// </summary>
    public static AcaoAplicavel SuspenderServicos(int quantidade) => new()
    {
        Id = IdSuspenderServicos,
        Rotulo = "Suspender serviços",
        Explicacao = $"Suspende {quantidade} serviço(s) de programas em segundo plano (atualizadores e "
                   + "telemetria). Eles param de rodar e voltam a iniciar quando você quiser. "
                   + "Serviços do Windows e de segurança nunca entram na lista.",
        MudaSistema = true,
        Reversivel = true,
    };

    /// <summary>A ação que leva para a aba Inicialização.</summary>
    public static AcaoAplicavel AbrirInicializacao() => new()
    {
        Id = IdAbrirInicializacao,
        Rotulo = "Ver inicialização",
        Explicacao = "Abre a aba Inicialização, onde você desativa o que não usa no boot.",
        MudaSistema = false,
        Reversivel = false,
    };

    /// <summary>A ação que leva para a aba Drivers.</summary>
    public static AcaoAplicavel AbrirDrivers() => new()
    {
        Id = IdAbrirDrivers,
        Rotulo = "Ver drivers",
        Explicacao = "Abre a aba Drivers, onde dá para atualizar o driver da GPU.",
        MudaSistema = false,
        Reversivel = false,
    };

    /// <summary>A ação que leva para a aba Otimização.</summary>
    public static AcaoAplicavel AbrirOtimizacao() => new()
    {
        Id = IdAbrirOtimizacao,
        Rotulo = "Ver otimizações",
        Explicacao = "Abre a aba Otimização (com o botão de restaurar os valores originais).",
        MudaSistema = false,
        Reversivel = false,
    };
}
