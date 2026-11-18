using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace TranalSentinel;

/// <summary>
/// Ajuste do Windows para jogos: lê o estado atual, aplica e guarda o valor original (em AppSettings.TweakBackup)
/// na primeira vez que é alterado, para "Restaurar originais" desfazer tudo.
/// Só ajustes documentados e reversíveis; nada que reduza a segurança (Defender, VBS/HVCI, UAC, Windows Update).
/// </summary>
public sealed class Tweak : INotifyPropertyChanged
{
    public string Id { get; init; } = "";
    public string Title { get; init; } = "";
    public string Description { get; init; } = "";
    public string Effect { get; init; } = "";
    public bool NeedsRestart { get; init; }
    /// <summary>
    /// O que o dono precisa fazer para o ajuste valer. O padrão é reiniciar o
    /// PC; alguns ajustes só precisam de uma coisa menos forte (reiniciar o
    /// Explorer, por exemplo).
    /// </summary>
    public string RestartHint { get; init; } = "Reinicie o PC para valer.";
    internal Func<bool?> Read { get; init; } = () => null;
    internal Action<bool, AppSettings> Write { get; init; } = (_, _) => { };
    internal Action<AppSettings> Restore { get; init; } = _ => { };

    private bool _isOn;
    public bool IsOn { get => _isOn; set { _isOn = value; Changed(nameof(IsOn)); } }
    private string _status = "";
    public string Status { get => _status; set { _status = value; Changed(nameof(Status)); } }
    public string Badge => NeedsRestart ? "REQUER REINICIAR" : "";

    public void Refresh() => IsOn = Read() ?? false;

    private void Changed(string p) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));
    public event PropertyChangedEventHandler? PropertyChanged;
}

public static class WindowsTweaks
{
    private const string HighPerformance = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";

    public static List<Tweak> All() => new()
    {
        new Tweak
        {
            Id = "power", Title = "Plano de energia: Alto desempenho",
            Description = "Mantém a CPU pronta para subir o clock na hora, sem economia agressiva de energia.",
            Effect = "Menos engasgos e 1% low melhor em alguns jogos. Em notebooks gasta mais bateria.",
            Read = () => ActiveScheme()?.Equals(HighPerformance, StringComparison.OrdinalIgnoreCase),
            Write = (on, s) =>
            {
                if (!s.TweakBackup.ContainsKey("power")) s.TweakBackup["power"] = ActiveScheme();
                if (on)
                {
                    // Alguns PCs vêm sem o plano listado: duplicar o modelo padrão o recria com o mesmo GUID.
                    if (!PowerCfg("/list").Contains(HighPerformance, StringComparison.OrdinalIgnoreCase))
                        PowerCfg($"-duplicatescheme {HighPerformance} {HighPerformance}");
                    PowerCfg($"/setactive {HighPerformance}");
                }
                else if (s.TweakBackup["power"] is string prev && !prev.Equals(HighPerformance, StringComparison.OrdinalIgnoreCase))
                    PowerCfg($"/setactive {prev}");
                else PowerCfg("/setactive 381b4222-f694-41f0-9685-ff5bb260df2e"); // Equilibrado
            },
            Restore = s => { if (s.TweakBackup.GetValueOrDefault("power") is string prev) PowerCfg($"/setactive {prev}"); },
        },
        RegTweak("gamemode", "Modo de Jogo",
            "Dá prioridade ao jogo e evita que o Windows Update instale ou reinicie durante a partida.",
            "Recomendado ligado (é o padrão do Windows 11).",
            Registry.CurrentUser, @"Software\Microsoft\GameBar", "AutoGameModeEnabled", on: 1, off: 0, absentMeansOn: true),
        new Tweak
        {
            Id = "gamedvr", Title = "Desativar gravação em segundo plano (Game DVR)",
            Description = "Desliga a captura contínua do Xbox Game Bar, que grava os últimos minutos de jogo o tempo todo.",
            Effect = "Recupera alguns FPS e reduz uso de GPU/disco. Você perde o \"gravar o que aconteceu\" do Game Bar.",
            Read = () => (ReadDword(Registry.CurrentUser, @"System\GameConfigStore", "GameDVR_Enabled") ?? 1) == 0 &&
                         (ReadDword(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\GameDVR", "AppCaptureEnabled") ?? 1) == 0,
            Write = (on, s) =>
            {
                SetDword(s, "gamedvr.a", Registry.CurrentUser, @"System\GameConfigStore", "GameDVR_Enabled", on ? 0 : 1);
                SetDword(s, "gamedvr.b", Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\GameDVR", "AppCaptureEnabled", on ? 0 : 1);
            },
            Restore = s =>
            {
                RestoreDword(s, "gamedvr.a", Registry.CurrentUser, @"System\GameConfigStore", "GameDVR_Enabled");
                RestoreDword(s, "gamedvr.b", Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\GameDVR", "AppCaptureEnabled");
            },
        },
        RegTweak("hags", "Agendamento de GPU acelerado por hardware",
            "A própria placa de vídeo gerencia sua fila de trabalho, em vez da CPU.",
            "Necessário para o Frame Generation do DLSS 3/4. Pode reduzir latência. Requer GPU e driver compatíveis.",
            Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "HwSchMode", on: 2, off: 1,
            absentMeansOn: false, needsRestart: true),
        new Tweak
        {
            Id = "windowed", Title = "Otimizações para jogos em janela",
            Description = "Faz jogos DirectX 10/11 em janela ou \"sem bordas\" usarem o modelo de apresentação moderno (flip).",
            Effect = "Menor latência e suporte a VRR/G-Sync em janela. Recurso do Windows 11.",
            Read = () => DxSettings().GetValueOrDefault("SwapEffectUpgradeEnable") == "1",
            Write = (on, s) =>
            {
                if (!s.TweakBackup.ContainsKey("windowed"))
                    s.TweakBackup["windowed"] = Registry.CurrentUser.OpenSubKey(DxPath)?.GetValue("DirectXUserGlobalSettings") as string ?? "<ausente>";
                var d = DxSettings();
                d["SwapEffectUpgradeEnable"] = on ? "1" : "0";
                using var k = Registry.CurrentUser.CreateSubKey(DxPath);
                k.SetValue("DirectXUserGlobalSettings", string.Concat(d.Select(kv => $"{kv.Key}={kv.Value};")));
            },
            Restore = s =>
            {
                if (s.TweakBackup.GetValueOrDefault("windowed") is not string prev) return;
                using var k = Registry.CurrentUser.CreateSubKey(DxPath);
                if (prev == "<ausente>") k.DeleteValue("DirectXUserGlobalSettings", false);
                else k.SetValue("DirectXUserGlobalSettings", prev);
            },
        },
        new Tweak
        {
            Id = "mouse", Title = "Desligar aceleração do mouse",
            Description = "Desmarca \"Aprimorar precisão do ponteiro\": o cursor anda sempre a mesma distância para o mesmo movimento.",
            Effect = "Não muda FPS, mas deixa a mira consistente em jogos de tiro.",
            Read = () => ReadString(Registry.CurrentUser, @"Control Panel\Mouse", "MouseSpeed") == "0",
            Write = (on, s) =>
            {
                foreach (var n in new[] { "MouseSpeed", "MouseThreshold1", "MouseThreshold2" })
                    if (!s.TweakBackup.ContainsKey($"mouse.{n}")) s.TweakBackup[$"mouse.{n}"] = ReadString(Registry.CurrentUser, @"Control Panel\Mouse", n);
                var v = on ? new[] { "0", "0", "0" } : new[] { "1", "6", "10" }; // padrão do Windows
                ApplyMouse(v);
            },
            Restore = s =>
            {
                if (!s.TweakBackup.ContainsKey("mouse.MouseSpeed")) return;
                ApplyMouse(new[] { "MouseSpeed", "MouseThreshold1", "MouseThreshold2" }
                    .Select(n => s.TweakBackup.GetValueOrDefault($"mouse.{n}") ?? "0").ToArray());
            },
        },
        new Tweak
        {
            Id = "roda", Title = "Rolagem do mouse nos navegadores",
            Description = "Diz ao Windows quantas linhas a roda rola por entalhe.",
            Effect = "Com zero linhas, Opera, Chrome e os aplicativos Electron não rolam a página: a roda gira e nada acontece.",
            Read = () => !WindowsTweakRegras.RolagemDoNavegadorQuebrada(LinhasPorEntalhe()),
            Write = (on, s) =>
            {
                GuardarRoda(s);
                if (on)
                {
                    GravarLinhasPorEntalhe(WindowsTweakRegras.LinhasPorEntalhePadrao);
                    GravarCaracteresPorEntalhe(WindowsTweakRegras.CaracteresPorEntalhePadrao);
                }
                else
                {
                    // Desligar devolve o valor que estava antes: o app não
                    // inventa um valor novo para "desfazer".
                    if (s.TweakBackup.TryGetValue("roda.linhas", out var l) &&
                        uint.TryParse(l, NumberStyles.Integer, CultureInfo.InvariantCulture, out var lv))
                        GravarLinhasPorEntalhe(lv);
                    if (s.TweakBackup.TryGetValue("roda.chars", out var c) &&
                        uint.TryParse(c, NumberStyles.Integer, CultureInfo.InvariantCulture, out var cv))
                        GravarCaracteresPorEntalhe(cv);
                }
            },
            Restore = s =>
            {
                if (s.TweakBackup.TryGetValue("roda.linhas", out var l) &&
                    uint.TryParse(l, NumberStyles.Integer, CultureInfo.InvariantCulture, out var lv))
                    GravarLinhasPorEntalhe(lv);
                if (s.TweakBackup.TryGetValue("roda.chars", out var c) &&
                    uint.TryParse(c, NumberStyles.Integer, CultureInfo.InvariantCulture, out var cv))
                    GravarCaracteresPorEntalhe(cv);
            },
        },
        new Tweak
        {
            Id = "alttab", Title = "Alt+Tab: quadro de miniaturas do Windows 11",
            Description = "Tira o Alt+Tab do modo clássico, que o Windows 11 24H2 em diante já não desenha.",
            Effect = "No modo clássico o Alt+Tab troca de janela e não mostra nada: parece que o PC trocou de janela sozinho.",
            NeedsRestart = WindowsTweakRegras.AltTabPrecisaReiniciarOExplorer,
            RestartHint = "Reinicie o Explorer (ou saia e entre na conta) para o quadro de miniaturas aparecer.",
            Read = () => !WindowsTweakRegras.AltTabLegadoQuebrado(AltTabSettings()),
            Write = (on, s) =>
            {
                if (!s.TweakBackup.ContainsKey("alttab"))
                    s.TweakBackup["alttab"] = AltTabSettings()?.ToString(CultureInfo.InvariantCulture) ?? "<ausente>";
                if (on) GravarAltTabSettings(WindowsTweakRegras.AltTabModerno);
                else if (s.TweakBackup.TryGetValue("alttab", out var v) &&
                         int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var antigo))
                    GravarAltTabSettings(antigo);
            },
            Restore = s =>
            {
                if (s.TweakBackup.TryGetValue("alttab", out var v) &&
                    int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var antigo))
                    GravarAltTabSettings(antigo);
            },
        },
    };

    /// <summary>Desfaz tudo que foi alterado pelo app (valores guardados antes da primeira alteração).</summary>
    public static void RestoreAll(IEnumerable<Tweak> tweaks, AppSettings s)
    {
        foreach (var t in tweaks)
            try { t.Restore(s); } catch { /* Um ajuste que não voltou é motivo para o app seguir: os outros também precisam ser desfeitos. */ }
        s.TweakBackup.Clear();
        s.Save();
    }

    // ------------------------------------------------------------------ registro

    private static Tweak RegTweak(string id, string title, string desc, string effect, RegistryKey hive, string path, string name,
                                  int on, int off, bool absentMeansOn, bool needsRestart = false) => new()
    {
        Id = id, Title = title, Description = desc, Effect = effect, NeedsRestart = needsRestart,
        Read = () => (ReadDword(hive, path, name) is int v) ? v == on : absentMeansOn,
        Write = (enable, s) => SetDword(s, id, hive, path, name, enable ? on : off),
        Restore = s => RestoreDword(s, id, hive, path, name),
    };

    private static int? ReadDword(RegistryKey hive, string path, string name)
    {
        using var k = hive.OpenSubKey(path);
        return k?.GetValue(name) is int v ? v : null;
    }

    private static string? ReadString(RegistryKey hive, string path, string name)
    {
        using var k = hive.OpenSubKey(path);
        return k?.GetValue(name)?.ToString();
    }

    private static void SetDword(AppSettings s, string backupKey, RegistryKey hive, string path, string name, int value)
    {
        if (!s.TweakBackup.ContainsKey(backupKey))
            s.TweakBackup[backupKey] = ReadDword(hive, path, name)?.ToString() ?? "<ausente>";
        using var k = hive.CreateSubKey(path);
        k.SetValue(name, value, RegistryValueKind.DWord);
    }

    private static void RestoreDword(AppSettings s, string backupKey, RegistryKey hive, string path, string name)
    {
        if (s.TweakBackup.GetValueOrDefault(backupKey) is not string prev) return;
        using var k = hive.CreateSubKey(path);
        if (prev == "<ausente>") k.DeleteValue(name, false);
        else k.SetValue(name, int.Parse(prev), RegistryValueKind.DWord);
    }

    private const string DxPath = @"Software\Microsoft\DirectX\UserGpuPreferences";

    /// <summary>"Chave=Valor;Chave=Valor;" → dicionário (o valor pode ter outras opções do usuário, que preservamos).</summary>
    private static Dictionary<string, string> DxSettings()
    {
        var d = new Dictionary<string, string>();
        string raw = Registry.CurrentUser.OpenSubKey(DxPath)?.GetValue("DirectXUserGlobalSettings") as string ?? "";
        foreach (var part in raw.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = part.Split('=', 2);
            if (kv.Length == 2) d[kv[0].Trim()] = kv[1].Trim();
        }
        return d;
    }

    private static void ApplyMouse(string[] v)
    {
        using (var k = Registry.CurrentUser.CreateSubKey(@"Control Panel\Mouse"))
        {
            k.SetValue("MouseSpeed", v[0]);
            k.SetValue("MouseThreshold1", v[1]);
            k.SetValue("MouseThreshold2", v[2]);
        }
        // Aplica na hora, sem precisar sair da conta.
        var p = v.Select(int.Parse).ToArray();
        SystemParametersInfo(0x0004 /* SPI_SETMOUSE */, 0, p, 0x01 | 0x02);
    }

    // ------------------------------------------------------------------ rolagem da roda

    private const uint SpiGetWheelScrollLines = 0x0068;
    private const uint SpiSetWheelScrollLines = 0x0069;
    private const uint SpiGetWheelScrollChars = 0x006A;
    private const uint SpiSetWheelScrollChars = 0x006B;
    private const uint SpiUpdate = 0x0001;

    /// <summary>
    /// Quantas linhas a roda rola por entalhe, segundo o Windows.
    /// </summary>
    /// <remarks>
    /// <c>null</c> quando não deu para ler: um número adivinhado seria pior
    /// que número ausente, e o app não pode afirmar que a rolagem está
    /// quebrada sem saber.
    /// </remarks>
    private static uint? LinhasPorEntalhe() => LerValorDoWindows(SpiGetWheelScrollLines);

    /// <summary>Quantos caracteres a roda rola por entalhe na horizontal.</summary>
    private static uint? CaracteresPorEntalhe() => LerValorDoWindows(SpiGetWheelScrollChars);

    private static uint? LerValorDoWindows(uint acao)
    {
        var ptr = Marshal.AllocHGlobal(sizeof(uint));
        try
        {
            for (int i = 0; i < sizeof(uint); i++) Marshal.WriteByte(ptr, i, 0);
            return SystemParametersInfo(acao, 0, ptr, 0)
                ? unchecked((uint)Marshal.ReadInt32(ptr))
                : (uint?)null;
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }

    /// <summary>
    /// Grava quantas linhas a roda rola por entalhe.
    /// </summary>
    /// <remarks>
    /// Medido no PC do dono em 17/11/2026: o Windows pega o valor em
    /// <c>uiParam</c>. Passar o ponteiro em <c>pvParam</c> grava zero e não
    /// muda nada — foi o que aconteceu na primeira tentativa.
    /// </remarks>
    private static bool GravarLinhasPorEntalhe(uint valor) =>
        SystemParametersInfo(SpiSetWheelScrollLines, valor, IntPtr.Zero, SpiUpdate);

    /// <summary>Grava quantos caracteres a roda rola por entalhe na horizontal.</summary>
    private static bool GravarCaracteresPorEntalhe(uint valor) =>
        SystemParametersInfo(SpiSetWheelScrollChars, valor, IntPtr.Zero, SpiUpdate);

    /// <summary>Guarda os valores da roda antes da primeira alteração.</summary>
    private static void GuardarRoda(AppSettings s)
    {
        if (!s.TweakBackup.ContainsKey("roda.linhas"))
            s.TweakBackup["roda.linhas"] = LinhasPorEntalhe()?.ToString(CultureInfo.InvariantCulture) ?? "<ausente>";
        if (!s.TweakBackup.ContainsKey("roda.chars"))
            s.TweakBackup["roda.chars"] = CaracteresPorEntalhe()?.ToString(CultureInfo.InvariantCulture) ?? "<ausente>";
    }

    // ------------------------------------------------------------------ Alt+Tab

    private const string ChaveDoExplorer = @"Software\Microsoft\Windows\CurrentVersion\Explorer";

    /// <summary>
    /// O valor do Alt+Tab no registro do Windows, ou <c>null</c> quando a
    /// chave não tem o valor (ou não deu para ler).
    /// </summary>
    private static int? AltTabSettings()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(ChaveDoExplorer);
            return k?.GetValue("AltTabSettings") is int v ? v : null;
        }
        catch
        {
            // Sem o valor, o app não pode afirmar que o Alt+Tab está quebrado.
            return null;
        }
    }

    private static void GravarAltTabSettings(int valor)
    {
        using var k = Registry.CurrentUser.CreateSubKey(ChaveDoExplorer, writable: true);
        k?.SetValue("AltTabSettings", valor, RegistryValueKind.DWord);
    }

    // ------------------------------------------------------------------ plano de energia

    private static string? ActiveScheme()
    {
        var m = System.Text.RegularExpressions.Regex.Match(PowerCfg("/getactivescheme"), @"[0-9a-fA-F\-]{36}");
        return m.Success ? m.Value.ToLowerInvariant() : null;
    }

    private static string PowerCfg(string args)
    {
        var psi = new ProcessStartInfo("powercfg.exe", args)
        {
            RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true
        };
        using var p = Process.Start(psi)!;
        string output = p.StandardOutput.ReadToEnd();
        p.WaitForExit(5000);
        return output;
    }

    [DllImport("user32.dll")] private static extern bool SystemParametersInfo(uint action, uint param, int[] data, uint winIni);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SystemParametersInfo(uint action, uint param, IntPtr data, uint winIni);
}
