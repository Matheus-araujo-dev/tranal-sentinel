using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace TranalSentinel;

/// <summary>Grupos de mudança que a linha do tempo acompanha.</summary>
public enum CategoriaMudanca
{
    Sistema,
    Bios,
    Driver,
    Energia,
    Memoria,
    Programa,
    Inicializacao,
    Seguranca,
}

/// <summary>O que aconteceu com o item observado.</summary>
public enum TipoMudanca
{
    /// <summary>Apareceu (programa novo, item de inicialização novo).</summary>
    Novo,

    /// <summary>O valor mudou (versão de driver nova, outro plano de energia).</summary>
    Alterado,

    /// <summary>Desapareceu (programa desinstalado, item removido da inicialização).</summary>
    Removido,
}

/// <summary>
/// O estado do PC em um instante, reduzido ao que muda de verdade.
/// </summary>
/// <remarks>
/// Tudo é texto de propósito: a comparação entre dois instantâneos é uma
/// comparação de strings, o que torna a regra testável e o arquivo de
/// histórico legível.
/// </remarks>
public sealed record InstantaneoPC
{
    /// <summary>Quando este retrato foi tirado.</summary>
    public DateTime Quando { get; init; }

    /// <summary>Versão do Windows (ex.: "11 · 26100.1").</summary>
    public string Windows { get; init; } = "";

    /// <summary>Versão da BIOS/UEFI.</summary>
    public string Bios { get; init; } = "";

    /// <summary>Driver de vídeo (nome + versão).</summary>
    public string DriverVideo { get; init; } = "";

    /// <summary>Driver de rede (nome + versão).</summary>
    public string DriverRede { get; init; } = "";

    /// <summary>Plano de energia em uso.</summary>
    public string PlanoEnergia { get; init; } = "";

    /// <summary>Memória instalada (módulos, total e velocidade).</summary>
    public string Memoria { get; init; } = "";

    /// <summary>Configurações que afetam desempenho e estabilidade.</summary>
    public string Configuracoes { get; init; } = "";

    /// <summary>Programas instalados, no formato "Nome · versão".</summary>
    public List<string> Programas { get; init; } = new();

    /// <summary>Itens de inicialização, no formato "Nome · origem".</summary>
    public List<string> Inicializacao { get; init; } = new();
}

/// <summary>Uma mudança observada no PC entre dois retratos.</summary>
public sealed record Mudanca
{
    /// <summary>Quando a mudança foi vista.</summary>
    public DateTime Quando { get; init; }

    /// <summary>Grupo da mudança.</summary>
    public CategoriaMudanca Categoria { get; init; }

    /// <summary>O que mudou (ex.: "Driver de vídeo", "Steam").</summary>
    public string Item { get; init; } = "";

    /// <summary>Valor anterior (vazio se o item acabou de aparecer).</summary>
    public string Antes { get; init; } = "";

    /// <summary>Valor novo (vazio se o item sumiu).</summary>
    public string Depois { get; init; } = "";

    /// <summary>Natureza da mudança.</summary>
    public TipoMudanca Tipo { get; init; }

    /// <summary>Texto pronto para a interface.</summary>
    public string Resumo => Tipo switch
    {
        TipoMudanca.Novo => $"{Item} — novo ({Depois})",
        TipoMudanca.Removido => $"{Item} — removido (era {Antes})",
        _ => $"{Item} — {Antes} → {Depois}",
    };
}

/// <summary>
/// A linha do tempo do PC: o que mudou desde a última vez que olhei.
/// </summary>
/// <remarks>
/// <para>
/// Esta é a peça que transforma o app de "mostrador de números" em
/// "assistente diário": nada do que muda é óbvio olhando o Gerenciador de
/// Tarefas, mas olhando para trás fica óbvio — "o driver da GPU mudou na
/// terça", "esse programa foi instalado na sexta".
/// </para>
/// <para>
/// A comparação é uma função pura: recebe dois retratos e devolve a lista de
/// mudanças. Nenhuma leitura de disco, nenhuma chamada ao Windows — é o que os
/// testes cobrem.
/// </para>
/// </remarks>
public static class Mudancas
{
    /// <summary>
    /// Compara dois retratos e devolve o que mudou do primeiro para o segundo.
    /// </summary>
    /// <remarks>
    /// Se o retrato anterior for nulo, devolve lista vazia: não há "antes" para
    /// comparar, e inventar uma mudança para tudo poluiria a linha do tempo.
    /// </remarks>
    public static List<Mudanca> Comparar(InstantaneoPC? antes, InstantaneoPC depois)
    {
        var lista = new List<Mudanca>();
        if (antes == null || depois == null) return lista;

        DateTime quando = depois.Quando;

        CompararTexto(lista, quando, CategoriaMudanca.Sistema, "Windows", antes.Windows, depois.Windows);
        CompararTexto(lista, quando, CategoriaMudanca.Bios, "BIOS/UEFI", antes.Bios, depois.Bios);
        CompararTexto(lista, quando, CategoriaMudanca.Driver, "Driver de vídeo", antes.DriverVideo, depois.DriverVideo);
        CompararTexto(lista, quando, CategoriaMudanca.Driver, "Driver de rede", antes.DriverRede, depois.DriverRede);
        CompararTexto(lista, quando, CategoriaMudanca.Energia, "Plano de energia", antes.PlanoEnergia, depois.PlanoEnergia);
        CompararTexto(lista, quando, CategoriaMudanca.Memoria, "Memória", antes.Memoria, depois.Memoria);
        CompararTexto(lista, quando, CategoriaMudanca.Seguranca, "Configurações do sistema", antes.Configuracoes, depois.Configuracoes);

        CompararLista(lista, quando, CategoriaMudanca.Programa, "Programa", antes.Programas, depois.Programas);
        CompararLista(lista, quando, CategoriaMudanca.Inicializacao, "Inicialização", antes.Inicializacao, depois.Inicializacao);

        return lista;
    }

    private static void CompararTexto(List<Mudanca> lista, DateTime quando, CategoriaMudanca categoria,
        string item, string antes, string depois)
    {
        if (antes == depois) return;
        // Valor vazio nos dois lados = não deu para ler; não é mudança.
        if (antes.Length == 0 && depois.Length == 0) return;
        lista.Add(new Mudanca
        {
            Quando = quando,
            Categoria = categoria,
            Item = item,
            Antes = antes,
            Depois = depois,
            Tipo = TipoMudanca.Alterado,
        });
    }

    private static void CompararLista(List<Mudanca> lista, DateTime quando, CategoriaMudanca categoria,
        string rotulo, List<string> antes, List<string> depois)
    {
        var antesPorNome = PorNome(antes);
        var depoisPorNome = PorNome(depois);

        foreach (var par in depoisPorNome)
        {
            string texto = string.Join(" / ", par.Value);
            if (!antesPorNome.TryGetValue(par.Key, out var versoesAntigas))
            {
                lista.Add(new Mudanca
                {
                    Quando = quando,
                    Categoria = categoria,
                    Item = $"{rotulo}: {par.Key}",
                    Antes = "",
                    Depois = texto,
                    Tipo = TipoMudanca.Novo,
                });
                continue;
            }

            string antesTexto = string.Join(" / ", versoesAntigas);
            if (antesTexto == texto) continue;
            // Mesmo item, texto diferente: quase sempre é a versão que mudou
            // (um driver atualizado, um programa que se atualizou). É uma
            // alteração, não um item novo com outro removido.
            lista.Add(new Mudanca
            {
                Quando = quando,
                Categoria = categoria,
                Item = $"{rotulo}: {par.Key}",
                Antes = antesTexto,
                Depois = texto,
                Tipo = TipoMudanca.Alterado,
            });
        }

        foreach (var par in antesPorNome)
        {
            if (depoisPorNome.ContainsKey(par.Key)) continue;
            lista.Add(new Mudanca
            {
                Quando = quando,
                Categoria = categoria,
                Item = $"{rotulo}: {par.Key}",
                Antes = string.Join(" / ", par.Value),
                Depois = "",
                Tipo = TipoMudanca.Removido,
            });
        }
    }

    /// <summary>
    /// Agrupa os itens pelo nome, guardando as versões de cada um.
    /// </summary>
    /// <remarks>
    /// (32 e 64 bits, ou duas versões instaladas). Agrupar pelo nome evita que a linha do tempo
/// trate isso como aparecimento e sumiço de itens.
    /// </remarks>
    private static Dictionary<string, List<string>> PorNome(IEnumerable<string> itens)
    {
        var mapa = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (string item in itens)
        {
            string nome = SemVersao(item);
            if (!mapa.TryGetValue(nome, out var versoes))
            {
                versoes = new List<string>();
                mapa[nome] = versoes;
            }
            if (!versoes.Contains(item)) versoes.Add(item);
        }
        return mapa;
    }

    /// <summary>Separa o nome do item da sua versão ("Steam · 2.0" → "Steam").</summary>
    public static string SemVersao(string item)
    {
        int corte = item.LastIndexOf(" · ", StringComparison.Ordinal);
        return corte > 0 ? item[..corte] : item;
    }

    /// <summary>
    /// Separa a versão do nome ("Steam · 2.0" → "2.0"); string vazia se não houver.
    /// </summary>
    public static string Versao(string item)
    {
        int corte = item.LastIndexOf(" · ", StringComparison.Ordinal);
        return corte > 0 ? item[(corte + 3)..] : "";
    }
}

/// <summary>
/// Coleta o retrato do PC e guarda o histórico dos retratos.
/// </summary>
public static class HistoricoMudancas
{
    /// <summary>Arquivo com os retratos anteriores.</summary>
    public static string Arquivo => Path.Combine(AppSettings.Folder, "historico", "instantaneos.json");

    /// <summary>Lê os retratos guardados, do mais antigo para o mais novo.</summary>
    public static List<InstantaneoPC> Ler()
    {
        try
        {
            if (!File.Exists(Arquivo)) return new List<InstantaneoPC>();
            var lista = JsonSerializer.Deserialize<List<InstantaneoPC>>(File.ReadAllText(Arquivo));
            return lista ?? new List<InstantaneoPC>();
        }
        catch
        {
            return new List<InstantaneoPC>();
        }
    }

    /// <summary>Grava os retratos (guarda só os últimos N, conforme o perfil de recursos).</summary>
    public static void Gravar(List<InstantaneoPC> instantaneos, int maximo)
    {
        try
        {
            var lista = instantaneos.Skip(Math.Max(0, instantaneos.Count - maximo)).ToList();
            Directory.CreateDirectory(Path.GetDirectoryName(Arquivo)!);
            File.WriteAllText(Arquivo, JsonSerializer.Serialize(lista, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }
}

/// <summary>
/// Tira o retrato do PC.
/// </summary>
/// <remarks>
/// Cada bloco é isolado: se o WMI falhar, o retrato fica com aquele campo vazio
/// em vez de a coleta inteira quebrar. Campo vazio nos dois lados não vira
/// mudança — é assim que a linha do tempo evita barulho.
/// </remarks>
public static class ColetaInstantaneo
{
    /// <summary>Tira o retrato de agora.</summary>
    public static InstantaneoPC Criar() => new()
    {
        Quando = DateTime.Now,
        Windows = LerWindows(),
        Bios = LerBios(),
        DriverVideo = LerDriver("DISPLAY"),
        DriverRede = LerDriver("NET"),
        PlanoEnergia = LerPlanoEnergia(),
        Memoria = LerMemoria(),
        Configuracoes = LerConfiguracoes(),
        Programas = LerProgramas(),
        Inicializacao = LerInicializacao(),
    };

    private static string LerWindows()
    {
        try
        {
            using var chave = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            if (chave == null) return "";
            string nome = chave.GetValue("ProductName")?.ToString() ?? "";
            string build = chave.GetValue("CurrentBuild")?.ToString() ?? "";
            string rev = chave.GetValue("UBR")?.ToString() ?? "";
            return string.IsNullOrEmpty(rev) ? $"{nome} {build}".Trim() : $"{nome} {build}.{rev}";
        }
        catch { return ""; }
    }

    private static string LerBios()
    {
        try
        {
            foreach (var linha in Wmi.Consultar("SELECT SMBIOSBIOSVersion, Manufacturer, ReleaseDate FROM Win32_BIOS"))
            {
                string versao = linha["SMBIOSBIOSVersion"];
                string data = LinhaWmiData(linha["ReleaseDate"]);
                return string.IsNullOrEmpty(data) ? versao : $"{versao} ({data})";
            }
        }
        catch { }
        return "";
    }

    private static string LinhaWmiData(string valorBruto)
    {
        // A data do WMI vem em formato CIM: 20260829120000.000000-000.
        try
        {
            if (valorBruto.Length < 8) return "";
            string ano = valorBruto[..4], mes = valorBruto.Substring(4, 2), dia = valorBruto.Substring(6, 2);
            return $"{dia}/{mes}/{ano}";
        }
        catch { return ""; }
    }

    private static string LerDriver(string classe)
    {
        try
        {
            foreach (var linha in Wmi.Consultar(
                $"SELECT DeviceName, DriverVersion, DriverDate FROM Win32_PnPSignedDriver WHERE DeviceClass='{classe}'"))
            {
                string nome = linha["DeviceName"];
                if (nome.Length == 0) continue;
                string versao = linha["DriverVersion"];
                return string.IsNullOrEmpty(versao) ? nome : $"{nome} · {versao}";
            }
        }
        catch { }
        return "";
    }

    private static string LerPlanoEnergia()
    {
        try
        {
            var psi = new ProcessStartInfo("powercfg", "/getactivescheme")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true,
            };
            using var proc = Process.Start(psi);
            if (proc == null) return "";
            string saida = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit(5000);

            // A linha contém o GUID do esquema; o nome vem do powercfg /list.
            int ini = saida.IndexOf(':');
            int fim = saida.IndexOf(')', ini + 1);
            if (ini < 0 || fim < 0) return "";
            string guid = saida.Substring(ini + 1, fim - ini - 1).Trim();
            return PlanosEnergia.NomeDoPlano(guid);
        }
        catch { return ""; }
    }

    private static string LerMemoria()
    {
        try
        {
            var modulos = new List<string>();
            long total = 0;
            int velocidade = 0;
            foreach (var linha in Wmi.Consultar(
                "SELECT DeviceLocator, Capacity, Speed, ConfiguredClockSpeed, Manufacturer FROM Win32_PhysicalMemory"))
            {
                long capacidade = linha.ValorLong("Capacity");
                total += capacidade;
                int vel = (int)linha.ValorLong("ConfiguredClockSpeed");
                if (vel == 0) vel = (int)linha.ValorLong("Speed");
                if (vel > velocidade) velocidade = vel;
                string local = linha["DeviceLocator"];
                string fabricante = linha["Manufacturer"];
                modulos.Add($"{local} {fabricante} {capacidade / (1024 * 1024)} GB".Trim());
            }
            if (modulos.Count == 0) return "";
            return $"{total / (1024 * 1024)} GB em {modulos.Count} módulo(s) · {velocidade} MHz · {string.Join(" / ", modulos)}";
        }
        catch { return ""; }
    }

    private static string LerConfiguracoes()
    {
        var partes = new List<string>();
        partes.Add(Chave("HKCU", @"Software\Microsoft\GameBar", "AutoGameModeEnabled", "Modo de jogo"));
        partes.Add(Chave("HKCU", @"System\GameConfigStore", "GameDVR_Enabled", "Game DVR"));
        partes.Add(Chave("HKLM", @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "HwSchMode", "HAGS"));
        partes.Add(Chave("HKLM", @"SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity", "Enabled", "Integridade de memória"));
        partes.Add(Chave("HKLM", @"SYSTEM\CurrentControlSet\Control\DeviceGuard", "EnableVirtualizationBasedSecurity", "VBS"));
        partes.Add(Chave("HKLM", @"SYSTEM\CurrentControlSet\Control\Session Manager\Power", "HiberbootEnabled", "Inicialização rápida"));
        // Só entra no retrato o que foi possível ler de fato: valor ausente nos
        // dois lados não é mudança, e poluir a linha do tempo com "—" seria ruído.
        return string.Join(" · ", partes.Where(p => !p.EndsWith(": —", StringComparison.Ordinal)));
    }

    private static string Chave(string raiz, string caminho, string nome, string rotulo)
    {
        string valor = "";
        try
        {
            var hive = raiz == "HKCU" ? Microsoft.Win32.Registry.CurrentUser : Microsoft.Win32.Registry.LocalMachine;
            using var chave = hive.OpenSubKey(caminho);
            if (chave != null)
            {
                var v = chave.GetValue(nome);
                if (v != null)
                {
                    int n = v is int i ? i : (int.TryParse(v.ToString(), out int p) ? p : -1);
                    valor = n switch
                    {
                        0 => "desligado",
                        1 => "ligado",
                        2 => "ligado",
                        _ => v.ToString() ?? "",
                    };
                }
            }
        }
        catch { }
        return valor.Length == 0 ? $"{rotulo}: —" : $"{rotulo}: {valor}";
    }

    private static List<string> LerProgramas()
    {
        var lista = new List<string>();
        var vistas = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string[] caminhos =
        {
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
            @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall",
        };
        foreach (string caminho in caminhos)
        {
            try
            {
                using var hive = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(caminho);
                if (hive == null) continue;
                foreach (string sub in hive.GetSubKeyNames())
                {
                    using var item = hive.OpenSubKey(sub);
                    string? nome = item?.GetValue("DisplayName") as string;
                    if (string.IsNullOrWhiteSpace(nome)) continue;
                    string versao = (item?.GetValue("DisplayVersion") as string ?? "").Trim();
                    string texto = versao.Length > 0 ? $"{nome.Trim()} · {versao}" : nome.Trim();
                    if (vistas.Add(texto)) lista.Add(texto);
                }
            }
            catch { }
        }
        try
        {
            using var hive = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
            if (hive != null)
            {
                foreach (string sub in hive.GetSubKeyNames())
                {
                    using var item = hive.OpenSubKey(sub);
                    string? nome = item?.GetValue("DisplayName") as string;
                    if (string.IsNullOrWhiteSpace(nome)) continue;
                    string versao = (item?.GetValue("DisplayVersion") as string ?? "").Trim();
                    string texto = versao.Length > 0 ? $"{nome.Trim()} · {versao}" : nome.Trim();
                    if (vistas.Add(texto)) lista.Add(texto);
                }
            }
        }
        catch { }
        lista.Sort(StringComparer.OrdinalIgnoreCase);
        return lista;
    }

    private static List<string> LerInicializacao()
    {
        var lista = new List<string>();
        try
        {
            foreach (var item in StartupManager.List())
            {
                string texto = $"{item.Name} · {item.Source}";
                if (!lista.Contains(texto, StringComparer.OrdinalIgnoreCase)) lista.Add(texto);
            }
        }
        catch { }
        lista.Sort(StringComparer.OrdinalIgnoreCase);
        return lista;
    }
}

/// <summary>Nomes amigáveis dos planos de energia do Windows (por GUID).</summary>
public static class PlanosEnergia
{
    private static readonly Dictionary<string, string> Nomes = new(StringComparer.OrdinalIgnoreCase)
    {
        { "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c", "Alto desempenho" },
        { "381b4222-f694-41f0-9685-ff5bb260df2e", "Equilibrado" },
        { "a1841308-3541-4fab-bc81-f71556f20b4a", "Economia de energia" },
        { "00000000-0000-0000-0000-000000000000", "Desligado" },
    };

    /// <summary>Nome do plano a partir do GUID; devolve o GUID se não for conhecido.</summary>
    public static string NomeDoPlano(string guid) =>
        Nomes.TryGetValue(guid, out var nome) ? nome : guid;
}
