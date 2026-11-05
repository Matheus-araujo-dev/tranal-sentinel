using System.IO;

namespace TranalSentinel;

/// <summary>
/// Um serviço de terceiros que sobe junto com o Windows.
/// </summary>
public sealed class ServicoSuspendivel
{
    /// <summary>Nome do serviço no Windows (o que o services.msc mostra).</summary>
    public string Nome { get; init; } = "";

    /// <summary>Programa dono do serviço (deduzido do caminho do executável).</summary>
    public string Programa { get; init; } = "";

    /// <summary>Caminho do executável do serviço.</summary>
    public string Caminho { get; init; } = "";

    /// <summary>Como ele inicia: Automático, Manual, Desativado.</summary>
    public string ModoInicializacao { get; init; } = "";

    /// <summary>O app sabe o que esse serviço é (e não é só um nome).</summary>
    public bool Conhecido { get; init; }

    /// <summary>
    /// É puro ruído (atualizador, telemetria)? Só o que é ruído vem marcado
    /// por seleção — o resto aparece, mas a escolha é do usuário.
    /// </summary>
    public bool Ruido { get; init; }

    /// <summary>Por que ele é ruído (preenchido só quando <see cref="Conhecido"/>).</summary>
    public string Motivo { get; init; } = "";

    /// <summary>Já vem marcado? Só o que o app reconhece como ruído.</summary>
    public bool Selecionado { get; set; }

    /// <summary>Texto curto para a lista da interface.</summary>
    public string Resumo =>
        $"{Programa} · {ModoInicializacao}";
}

/// <summary>
/// A lista de serviços que o app oferece para suspender.
///
/// <para>
/// <b>Só lógica pura</b>: filtra e classifica. Quem executa é o
/// <see cref="ServicosSuspender"/> — assim a regra do diagnóstico (que é
/// pura) não depende de nada que execute comando.
/// </para>
///
/// <para>
/// A lista é conservadora de propósito: serviço de segurança, serviço de
/// sistema, serviço de áudio/rede/impressora e a integração de RGB que o
/// próprio app usa <b>não entram</b>. Do que sobra, o app reconhece os
/// atualizadores e telemetrias (esses vêm marcados) e o resto aparece sem
/// veredito — a escolha é do usuário.
/// </para>
/// </summary>
public static class ServicosSuspendiveis
{
    /// <summary>
    /// Tokens que indicam serviço de segurança. A comparação é por "contém",
    /// porque o nome real costuma vir sufixado (ex.: "MpsSvc", "WDNisSvc").
    /// </summary>
    private static readonly string[] Seguranca =
    {
        "windefend", "wdnis", "wdfilter", "sense", "mpssvc", "wscsvc", "securityhealth",
        "mcafee", "masvc", "mcpxsvc", "mcafeewebadvisor", "symantec", "avp", "eset", "sophos",
        "kaspersky", "avast", "avg", "norton", "trendmicro", "trend", "comodo", "malwarebytes",
        "bullguard", "forticlient", "cylance", "trellix", "fsecure", "f-secure", "gdata",
        "webroot", "totalav", "avira", "antivirus", "antimalware", "antispam", "endpoint",
        "protection", "hips", "firewall", "sentinel", "edr", "xdr", "webadvisor", "msmpeng",
        "corsair",  // o iCUE tem driver de proteção própria
    };

    /// <summary>
    /// Tokens de serviço de que o usuário pode depender: áudio, vídeo,
    /// impressora, rede, virtualização, banco de dados, Office. O app não
    /// oferece nada disso — nem desmarcado.
    /// </summary>
    private static readonly string[] Suporte =
    {
        "audio", "sound", "nahimic", "realtek", "bluetooth", "bthserv",
        "printer", "spooler", "print", "fax",
        "display", "nvdisplay", "nvidia", "amdkmdag", "amdwddmg", "touch", "tablet",
        "keyboard", "mouse",
        "wsl", "vmcompute", "hns", "docker", "hyper", "vmmem", "sandbox", "vmms",
        "tailscale", "vpn", "openvpn", "zerotier", "wireguard",
        "sql", "mssql", "oracle", "postgres", "mongo", "mysql",
        "clicktorun", "office", "outlook", "exchange", "lync", "msteams",
        "iis", "w3svc", "iisadmin",
    };

    /// <summary>
    /// A integração de RGB que o próprio TRANAL SENTINEL usa. Suspender
    /// quebraria o controle de luz que o app oferece — e o app consertaria o
    /// que quebrou.
    /// </summary>
    private static readonly string[] IntegracaoDoApp =
    {
        "openrgb", "icue", "razer", "chroma", "logitech", "g-hub", "ghub", "lghub",
        "wallpaper", "coolercontrol",
    };

    /// <summary>
    /// Serviços que o app reconhece: quase sempre são só atualizador,
    /// telemetria ou sincronização de segundo plano.
    /// </summary>
    private static readonly Dictionary<string, (string Programa, string Motivo, bool Ruido)> Conhecidos =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["AdobeARCSService"] = ("Adobe Acrobat/Reader", "Atualizador do Acrobat — só roda para ver se há versão nova", true),
            ["AdobeGCInvoker"] = ("Adobe Genuine", "Verificação de licença da Adobe — roda sozinho de tempos em tempos", true),
            ["AGMService"] = ("Adobe Genuine", "Verificação de licença da Adobe — roda sozinho de tempos em tempos", true),
            ["AdobeARM"] = ("Adobe", "Gerenciador de pacotes da Adobe", true),
            ["CoreSync"] = ("Adobe Creative Cloud", "Sincronização da Adobe — consome banda e disco", true),
            ["CCXProcess"] = ("Adobe Creative Cloud", "Processo do Creative Cloud — costuma ficar ocioso em segundo plano", true),
            ["gupdate"] = ("Google Update", "Atualizador do Google — reinicia o Chrome sozinho", true),
            ["gupdatem"] = ("Google Update", "Atualizador do Google — reinicia o Chrome sozinho", true),
            ["GUpdater"] = ("Google Update", "Atualizador do Google", true),
            ["GUpdaterService"] = ("Google Update", "Atualizador do Google", true),
            ["brave"] = ("Brave", "Atualizador do navegador Brave", true),
            ["edgeupdate"] = ("Microsoft Edge", "Atualizador do Edge — roda sozinho para ver se há versão nova", true),
            ["BraveSoftwareBraveBrowserUpdater"] = ("Brave", "Atualizador do navegador", true),
            ["BraveSoftwareBraveBrowserMaintenance"] = ("Brave", "Manutenção do navegador", true),
            ["MozillaMaintenance"] = ("Mozilla", "Manutenção do Firefox", true),
            ["MozillaQuickInstaller"] = ("Mozilla", "Instalador rápido do Firefox", true),
            ["EpicOnlineServices"] = ("Epic Games", "Serviço do launcher da Epic", true),
            ["EpicGamesLauncherUpdater"] = ("Epic Games", "Atualizador do launcher da Epic", true),
            ["EpicWebHelper"] = ("Epic Games", "Ajuda do launcher da Epic", true),
            ["YandexUpdater"] = ("Yandex", "Atualizador do Yandex", true),
            ["YandexBrowserUpdater"] = ("Yandex", "Atualizador do navegador", true),
            ["AUEPLauncher"] = ("AMD", "Perfil de desempenho da AMD — mede o uso em segundo plano", false),
            ["AUEPDU"] = ("AMD", "Perfil de desempenho da AMD — mede o uso em segundo plano", false),
            ["MouseWithoutBordersSvc"] = ("Microsoft Garage", "Mouse sem Fronteiras — só se você usa", false),
        };

    /// <summary>
    /// Serviços conhecidos cujo nome <b>carrega a versão</b> (o do Google
    /// Update é "GoogleUpdaterService157.0.8089.0"). Sem o prefixo, o app
    /// não reconheceria nem o atualizador mais comum do Windows.
    /// </summary>
    private static readonly (string Prefixo, string Programa, string Motivo)[] ConhecidosPorPrefixo =
    {
        ("GoogleUpdaterService", "Google Update", "Atualizador do Google — reinicia o Chrome sozinho"),
        ("GoogleUpdaterInternalService", "Google Update", "Atualizador interno do Google — só checa atualização"),
        ("MicrosoftEdgeUpdate", "Microsoft Edge", "Atualizador do Edge"),
        ("BraveSoftwareBraveBrowser", "Brave", "Atualizador do navegador"),
    };

    /// <summary>
    /// Monta a lista de serviços que o app oferece.
    /// </summary>
    public static List<ServicoSuspendivel> Listar(IEnumerable<ServicoInfo> servicos)
    {
        var lista = new List<ServicoSuspendivel>();

        foreach (var s in servicos)
        {
            if (string.IsNullOrWhiteSpace(s.Nome)) continue;
            if (s.EhDoSistema) continue;          // serviço do Windows
            if (EhSeguranca(s.Nome) || EhSeguranca(s.Caminho)) continue;
            if (EhSuporte(s.Nome) || EhSuporte(s.Caminho)) continue;
            if (EhIntegracaoDoApp(s.Nome) || EhIntegracaoDoApp(s.Caminho)) continue;
            if (s.ModoInicializacao.Equals("Disabled", StringComparison.OrdinalIgnoreCase)) continue;

            var reconhecido = Reconhecer(s.Nome);
            bool conhecido = reconhecido is not null;
            bool ruido = conhecido && reconhecido!.Value.Ruido;

            lista.Add(new ServicoSuspendivel
            {
                Nome = s.Nome,
                Programa = conhecido ? reconhecido!.Value.Programa : ProgramaDoCaminho(s.Caminho),
                Caminho = s.Caminho,
                ModoInicializacao = s.ModoInicializacao,
                Conhecido = conhecido,
                Ruido = ruido,
                Motivo = conhecido ? reconhecido!.Value.Motivo : "",
                // Só o que é puro ruído vem marcado: o resto aparece, mas a
                // escolha é do usuário.
                Selecionado = ruido,
            });
        }

        // Os de ruído primeiro (são os que o app tem autoridade para julgar),
        // e dentro de cada grupo por nome.
        return lista
            .OrderByDescending(s => s.Ruido)
            .ThenByDescending(s => s.Conhecido)
            .ThenBy(s => s.Nome, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>É serviço de segurança?</summary>
    public static bool EhSeguranca(string texto) => ContemAlgum(texto, Seguranca);

    /// <summary>É serviço de que o usuário pode depender (áudio, rede, Office...)?</summary>
    public static bool EhSuporte(string texto) => ContemAlgum(texto, Suporte);

    /// <summary>É a integração de RGB que o próprio app usa?</summary>
    public static bool EhIntegracaoDoApp(string texto) => ContemAlgum(texto, IntegracaoDoApp);

    /// <summary>
    /// O app pode oferecer este serviço? Reúne todas as guardas — é o único
    /// lugar que decide o que entra na lista.
    /// </summary>
    public static bool PodeOferecer(ServicoInfo s)
    {
        if (string.IsNullOrWhiteSpace(s.Nome)) return false;
        if (s.EhDoSistema) return false;
        if (EhSeguranca(s.Nome) || EhSeguranca(s.Caminho)) return false;
        if (EhSuporte(s.Nome) || EhSuporte(s.Caminho)) return false;
        if (EhIntegracaoDoApp(s.Nome) || EhIntegracaoDoApp(s.Caminho)) return false;
        return true;
    }

    /// <summary>Reconhece o serviço pelo nome exato ou pelo prefixo.</summary>
    private static (string Programa, string Motivo, bool Ruido)? Reconhecer(string nome)
    {
        if (Conhecidos.TryGetValue(nome, out var exato)) return exato;
        foreach (var (prefixo, programa, motivo) in ConhecidosPorPrefixo)
            if (nome.StartsWith(prefixo, StringComparison.OrdinalIgnoreCase))
                return (programa, motivo, true);
        return null;
    }

    /// <summary>Nome do programa dono de um serviço, pelo caminho do executável.</summary>
    public static string ProgramaDoCaminho(string caminho)
    {
        if (string.IsNullOrWhiteSpace(caminho)) return "Programa desconhecido";
        try
        {
            string pasta = Path.GetDirectoryName(caminho) ?? "";
            string pastaAcima = Path.GetDirectoryName(pasta) ?? "";
            string nome = Path.GetFileName(pastaAcima);
            return string.IsNullOrWhiteSpace(nome) ? Path.GetFileName(pasta) : nome;
        }
        catch
        {
            return "Programa desconhecido";
        }
    }

    private static bool ContemAlgum(string texto, string[] tokens)
    {
        if (string.IsNullOrWhiteSpace(texto)) return false;
        foreach (var t in tokens)
            if (texto.Contains(t, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }
}
