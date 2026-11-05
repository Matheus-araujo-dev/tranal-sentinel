using System.Diagnostics;
using System.Globalization;
using System.Security.Principal;

namespace TranalSentinel;

/// <summary>Resultado de uma ação: deu certo ou não, e o que aconteceu.</summary>
public sealed class ResultadoAcao
{
    public bool Sucesso { get; init; }
    public string Mensagem { get; init; } = "";

    public static ResultadoAcao Ok(string mensagem) => new() { Sucesso = true, Mensagem = mensagem };
    public static ResultadoAcao Falhou(string mensagem) => new() { Sucesso = false, Mensagem = mensagem };
}

/// <summary>
/// O que a ação mexe no sistema.
///
/// <para>
/// É a fronteira: o <see cref="AcaoExecutor"/> só sabe o que fazer com cada
/// ação; <b>como</b> fazer (e o que dá errado) é deste lado. É o que permite
/// testar o executor inteiro sem Windows.
/// </para>
/// </summary>
public interface IAcaoAlvo
{
    /// <summary>O app está rodando como administrador?</summary>
    bool Elevado { get; }

    /// <summary>Identificador do plano de energia ativo (GUID).</summary>
    string? PlanoAtivo();

    /// <summary>Valor atual do ASPM no plano (null = não deu para ler).</summary>
    int? LerAspm(string plano);

    /// <summary>
    /// Escreve o valor do ASPM no plano e aplica. Devolve false se o Windows
    /// recusou — quem chama precisa saber, senão jura que restaurou.
    /// </summary>
    bool AplicarAspm(string plano, int valor);

    /// <summary>Suspende os serviços escolhidos. Devolve quantos pararam.</summary>
    int SuspenderServicos(AppSettings settings, IEnumerable<ServicoSuspendivel> servicos);

    /// <summary>Devolve os serviços suspensos ao modo original.</summary>
    int RestaurarServicos(AppSettings settings);
}

/// <summary>
/// Executa as ações do TRANAL SAÚDE.
///
/// <para>
/// Concentra as regras que valem para toda ação: só mexe no sistema com
/// administrador, só mexe no ASPM com o valor original guardado (para
/// desfazer), e nunca toca em serviço de segurança.
/// </para>
/// </summary>
public sealed class AcaoExecutor
{
    private readonly IAcaoAlvo _alvo;
    private readonly AppSettings _settings;
    private readonly Action<string> _navegar;

    public AcaoExecutor(IAcaoAlvo alvo, AppSettings settings, Action<string>? navegar = null)
    {
        _alvo = alvo;
        _settings = settings;
        _navegar = navegar ?? (_ => { });
    }

    /// <summary>Chave de backup do ASPM (o valor é "plano|aspm").</summary>
    public const string ChaveAspm = "aspm";

    /// <summary>
    /// Executa a ação. Devolve o que aconteceu — nunca lança: uma ação que
    /// falhou é um aviso na tela, não um crash.
    /// </summary>
    public ResultadoAcao Executar(AcaoAplicavel acao)
    {
        if (acao is null) return ResultadoAcao.Falhou("Ação inválida.");

        try
        {
            switch (acao.Id)
            {
                case AcoesSaude.IdAspmDesligar:
                    return DesligarAspm();

                case AcoesSaude.IdAspmRestaurar:
                    return RestaurarAspm();

                case AcoesSaude.IdSuspenderServicos:
                    return ResultadoAcao.Falhou(
                        "Abra a janela SUSPENDER SERVIÇOS para escolher quais serviços suspender.");

                case AcoesSaude.IdAbrirInicializacao:
                    _navegar("PageStartup");
                    return ResultadoAcao.Ok("Abrindo Inicialização.");

                case AcoesSaude.IdAbrirDrivers:
                    _navegar("PageDriver");
                    return ResultadoAcao.Ok("Abrindo Drivers.");

                case AcoesSaude.IdAbrirOtimizacao:
                    _navegar("PageOptimize");
                    return ResultadoAcao.Ok("Abrindo Otimização.");

                default:
                    return ResultadoAcao.Falhou($"Ação desconhecida: {acao.Id}");
            }
        }
        catch (Exception ex)
        {
            return ResultadoAcao.Falhou($"Não deu para aplicar: {ex.Message}");
        }
    }

    /// <summary>Desliga o ASPM, guardando o valor original para desfazer.</summary>
    public ResultadoAcao DesligarAspm()
    {
        if (!_alvo.Elevado)
            return ResultadoAcao.Falhou("Esta ação precisa de administrador. Abra o TRANAL SENTINEL como administrador.");

        string? plano = _alvo.PlanoAtivo();
        if (string.IsNullOrWhiteSpace(plano))
            return ResultadoAcao.Falhou("Não deu para descobrir o plano de energia ativo.");

        int? original = _alvo.LerAspm(plano);
        if (original is null)
            return ResultadoAcao.Falhou("Não deu para ler o ASPM deste plano de energia.");

        // O backup vem ANTES da mudança: se o app cair no meio, o valor
        // original já está no arquivo e dá para restaurar depois.
        if (!_settings.TweakBackup.ContainsKey(ChaveAspm))
        {
            _settings.TweakBackup[ChaveAspm] = plano + "|" + original.Value.ToString(CultureInfo.InvariantCulture);
            _settings.Save();
        }

        if (original.Value == 0)
            return ResultadoAcao.Ok("O ASPM já estava desligado.");

        if (!_alvo.AplicarAspm(plano, 0))
            return ResultadoAcao.Falhou("O Windows recusou a mudança do ASPM. Nada foi alterado.");

        return ResultadoAcao.Ok("ASPM desligado. Dá para restaurar depois.");
    }

    /// <summary>Devolve o ASPM ao valor original (se o app tiver mudado).</summary>
    public ResultadoAcao RestaurarAspm()
    {
        if (!_alvo.Elevado)
            return ResultadoAcao.Falhou("Esta ação precisa de administrador. Abra o TRANAL SENTINEL como administrador.");

        if (!_settings.TweakBackup.TryGetValue(ChaveAspm, out var texto) || string.IsNullOrWhiteSpace(texto))
            return ResultadoAcao.Falhou("O TRANAL SENTINEL não mudou o ASPM neste PC — nada a restaurar.");

        // O backup guarda o plano em que a mudança foi feita: restaurar em
        // outro plano não desfaz nada e estraga o plano errado.
        var partes = texto.Split('|');
        if (partes.Length != 2 ||
            string.IsNullOrWhiteSpace(partes[0]) ||
            !int.TryParse(partes[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int original))
            return ResultadoAcao.Falhou("O registro do ASPM ficou inconsistente — o valor original não foi encontrado.");

        if (!_alvo.AplicarAspm(partes[0], original))
        {
            // Falhou: o backup fica, para uma segunda tentativa.
            return ResultadoAcao.Falhou("O Windows recusou a mudança do ASPM. O valor original continua guardado.");
        }

        _settings.TweakBackup.Remove(ChaveAspm);
        _settings.Save();

        return ResultadoAcao.Ok($"ASPM restaurado para o valor original ({original}).");
    }
}

/// <summary>
/// A ação no Windows de verdade: <c>powercfg</c> para o ASPM e o gerenciador
/// de serviços para o resto.
/// </summary>
public sealed class AcaoWindows : IAcaoAlvo
{
    public bool Elevado
    {
        get
        {
            try
            {
                using var id = WindowsIdentity.GetCurrent();
                return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }
    }

    public string? PlanoAtivo()
    {
        // "Power Scheme GUID: 381b4222-f694-41f0-9685-ff5bb260df2e (Balanced)"
        string saida = PowerCfg("/getactivescheme");
        if (string.IsNullOrWhiteSpace(saida)) return null;

        int pos = saida.IndexOf("GUID:", StringComparison.OrdinalIgnoreCase);
        if (pos < 0) return null;

        string resto = saida[(pos + 5)..].Trim();
        int fim = resto.IndexOf(' ');
        string guid = (fim > 0 ? resto[..fim] : resto).Trim();
        return guid.Length >= 32 ? guid : null;
    }

    public int? LerAspm(string plano)
    {
        string saida = PowerCfg($"/getacvalueindex {plano} SUB_PCIEXPRESS ASPM");
        if (string.IsNullOrWhiteSpace(saida)) return null;

        // A última linha tem "Current AC Power Setting Index: 0x00000000".
        foreach (var linha in saida.Split('\n'))
        {
            int pos = linha.IndexOf("0x", StringComparison.OrdinalIgnoreCase);
            if (pos < 0) continue;
            string hex = linha[(pos + 2)..].Trim();
            if (int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int valor))
                return valor;
        }
        return null;
    }

    public bool AplicarAspm(string plano, int valor)
    {
        if (!PowerCfgOk($"/setacvalueindex {plano} SUB_PCIEXPRESS ASPM {valor}")) return false;
        // Sem isto o valor fica guardado mas o plano continua com o valor antigo.
        return PowerCfgOk("/setactive " + plano);
    }

    public int SuspenderServicos(AppSettings settings, IEnumerable<ServicoSuspendivel> servicos)
    {
        int total = ServicosSuspender.Suspender(settings, servicos);
        settings.Save();
        return total;
    }

    public int RestaurarServicos(AppSettings settings)
    {
        int total = ServicosSuspender.Restaurar(settings);
        settings.Save();
        return total;
    }

    /// <summary>Roda o powercfg e devolve a saída (vazio se falhar).</summary>
    private static string PowerCfg(string argumentos)
    {
        try
        {
            var psi = new ProcessStartInfo("powercfg.exe")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            // ArgumentList (e não a linha de comando crua): o plano e o valor
            // não viram parte de outro comando.
            foreach (var parte in argumentos.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                psi.ArgumentList.Add(parte);

            using var p = Process.Start(psi);
            if (p is null) return "";
            string saida = p.StandardOutput.ReadToEnd();
            p.WaitForExit(15000);
            return saida;
        }
        catch
        {
            return "";
        }
    }

    /// <summary>Roda o powercfg e diz se deu certo.</summary>
    private static bool PowerCfgOk(string argumentos)
    {
        try
        {
            var psi = new ProcessStartInfo("powercfg.exe")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            foreach (var parte in argumentos.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                psi.ArgumentList.Add(parte);

            using var p = Process.Start(psi);
            if (p is null) return false;
            p.StandardOutput.ReadToEnd();
            p.WaitForExit(15000);
            return p.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}
