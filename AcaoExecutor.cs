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
    /// Escreve o valor do ASPM no plano. Devolve false se o Windows recusou
    /// — quem chama precisa saber, senão jura que restaurou.
    ///
    /// <para>
    /// O <paramref name="reativar"/> diz se o plano deve virar o plano em
    /// uso depois da mudança. Só é true quando o plano já era o em uso: ativar
    /// outro plano trocaria a escolha de energia do usuário sem ele pedir.
    /// </para>
    /// </summary>
    bool AplicarAspm(string plano, int valor, bool reativar);

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

    /// <summary>Prefixo da chave de backup do ASPM (o plano vem no fim da chave).</summary>
    public const string PrefixoAspm = "aspm.";

    /// <summary>
    /// Chave de backup do ASPM de um plano. Cada plano tem a sua: desligar o
    /// ASPM em um plano e em outro são duas mudanças, com dois valores
    /// originais.
    /// </summary>
    public static string ChaveAspm(string plano) => PrefixoAspm + plano;

    /// <summary>O app alterou o ASPM de algum plano neste PC?</summary>
    public static bool TemAspmAlterado(AppSettings settings) =>
        settings.TweakBackup.Keys.Any(k => k.StartsWith(PrefixoAspm, StringComparison.OrdinalIgnoreCase));

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

    /// <summary>
    /// Aplica o ASPM em um plano. Só reativa o plano quando ele já é o plano
    /// em uso — ativar outro trocaria a escolha de energia do usuário sem ele
    /// pedir.
    /// </summary>
    private bool AplicarAspm(string plano, int valor)
    {
        bool reativar = string.Equals(plano, _alvo.PlanoAtivo(), StringComparison.OrdinalIgnoreCase);
        return _alvo.AplicarAspm(plano, valor, reativar);
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

        // Já estava desligado: o app não alterou nada, e não vai registrar
        // uma "alteração" que não fez — senão o desfazer sobrescreveria uma
        // escolha que o usuário fez depois.
        if (original.Value == 0)
            return ResultadoAcao.Ok("O ASPM já estava desligado neste plano — nada foi alterado.");

        // O backup vem ANTES da mudança: se o app cair no meio, o valor
        // original já está no arquivo e dá para restaurar depois.
        string chave = ChaveAspm(plano);
        if (!_settings.TweakBackup.ContainsKey(chave))
        {
            _settings.TweakBackup[chave] = original.Value.ToString(CultureInfo.InvariantCulture);
            _settings.Save();
        }

        if (!AplicarAspm(plano, 0))
            return ResultadoAcao.Falhou("O Windows recusou a mudança do ASPM. Nada foi alterado.");

        return ResultadoAcao.Ok("ASPM desligado. Dá para restaurar depois.");
    }

    /// <summary>Devolve o ASPM ao valor original (nos planos em que o app mexeu).</summary>
    public ResultadoAcao RestaurarAspm()
    {
        if (!_alvo.Elevado)
            return ResultadoAcao.Falhou("Esta ação precisa de administrador. Abra o TRANAL SENTINEL como administrador.");

        // Registro de uma versão antiga: o valor não pode ser aplicado com
        // segurança (não dá para saber em que plano foi feito). Some.
        _settings.TweakBackup.Remove("aspm");

        var chaves = _settings.TweakBackup.Keys
            .Where(k => k.StartsWith(PrefixoAspm, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (chaves.Count == 0)
            return ResultadoAcao.Falhou("O TRANAL SENTINEL não mudou o ASPM neste PC — nada a restaurar.");

        int restaurados = 0;
        int ultimoValor = 0;

        foreach (var chave in chaves)
        {
            string plano = chave.Substring(PrefixoAspm.Length);
            var texto = _settings.TweakBackup[chave];
            if (!int.TryParse(texto, NumberStyles.Integer, CultureInfo.InvariantCulture, out int valor))
            {
                _settings.TweakBackup.Remove(chave);
                continue;
            }

            // O plano restaurado é o plano em que a mudança foi feita. O
            // plano de energia em uso não é trocado (é o usuário que escolhe
            // o plano dele) — só é reativado se já for o plano em uso, para
            // que a mudança valha na hora.
            if (!AplicarAspm(plano, valor))
            {
                // Falhou: o backup fica, para uma segunda tentativa.
                continue;
            }

            _settings.TweakBackup.Remove(chave);
            restaurados++;
            ultimoValor = valor;
        }

        if (restaurados == 0)
            return ResultadoAcao.Falhou("O Windows recusou a mudança do ASPM. O valor original continua guardado.");

        _settings.Save();

        return ResultadoAcao.Ok(restaurados == 1
            ? $"ASPM restaurado para o valor original ({ultimoValor})."
            : $"ASPM restaurado em {restaurados} planos de energia (valor original {ultimoValor}).");
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

    public bool AplicarAspm(string plano, int valor, bool reativar)
    {
        if (!PowerCfgOk($"/setacvalueindex {plano} SUB_PCIEXPRESS ASPM {valor}")) return false;

        // Sem isto o valor fica guardado mas o plano continua com o valor
        // antigo. E só quando o plano já é o plano em uso: ativar outro
        // trocaria a escolha de energia do usuário sem ele pedir.
        if (!reativar) return true;

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
