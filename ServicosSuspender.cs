using System.Diagnostics;
using System.IO;

namespace TranalSentinel;

/// <summary>
/// O modo de inicialização de um serviço (o número é o do sc.exe).
///
/// <para>
/// É a <b>única</b> definição de "modo que o app pode mudar": automático
/// (2) e manual (3). Desativado (4), boot (0) e system (1) ficam de fora —
/// tanto na hora de suspender quanto na hora de restaurar. Quem precisar
/// desse filtro consome <see cref="Restauravel"/>.
/// </para>
/// </summary>
public sealed record ModoServico
{
    /// <summary>Automático (o número do sc.exe).</summary>
    public const int Automatico = 2;

    /// <summary>Manual (o número do sc.exe).</summary>
    public const int Manual = 3;

    /// <summary>Modo de inicialização do serviço (o número é o do sc.exe).</summary>
    public int Modo { get; init; }

    /// <summary>Automático com atraso (o Windows segura o serviço no boot).</summary>
    public bool Atrasado { get; init; }

    /// <summary>
    /// Este modo pode ser mudado pelo app? Só automático e manual — e é
    /// este o filtro usado tanto para suspender quanto para restaurar.
    /// </summary>
    public bool Restauravel => Modo is Automatico or Manual;

    /// <summary>Texto que vai para o backup: "<modo>;<atrasado>;<caminho>".</summary>
    public string Texto(string caminho) => $"{Modo};{(Atrasado ? 1 : 0)};{caminho}";

    /// <summary>Nome do modo, para a interface.</summary>
    public static string TextoDoModo(int modo) => modo switch
    {
        0 => "Inicialização inicial",
        1 => "Sistema",
        Automatico => "Automático",
        Manual => "Manual",
        4 => "Desativado",
        _ => "Automático"
    };

    /// <summary>
    /// Lê do texto do backup. Devolve null quando o texto não é do formato
    /// que o app grava — e, principalmente, quando o modo não é um dos que o
    /// app mesmo produz. É essa recusa que impede um <c>settings.json</c>
    /// adulterado de desativar um serviço.
    /// </summary>
    public static ModoServico? DoTexto(string texto, out string caminho)
    {
        caminho = "";
        if (string.IsNullOrWhiteSpace(texto)) return null;

        var partes = texto.Split(';');
        if (partes.Length < 2) return null;
        if (!int.TryParse(partes[0], out int modo)) return null;
        if (!int.TryParse(partes[1], out int atrasado)) return null;

        // O app só grava e só restaura estes dois modos. Desativado (4),
        // boot (0) e system (1) nunca foram gravados por ele — então um
        // backup que os traga não é do app, e é descartado.
        if (modo is not (Automatico or Manual)) return null;

        caminho = partes.Length > 2 ? string.Join(";", partes[2..]) : "";
        return new ModoServico { Modo = modo, Atrasado = atrasado == 1 };
    }
}

/// <summary>
/// O que o Windows faz com um serviço.
///
/// <para>
/// É a fronteira: <see cref="ServicosSuspender"/> decide <b>quais</b>
/// serviços suspender e <b>como desfazer</b>; quem fala com o Windows é
/// esta interface — a implementação real (<see cref="ServicosWindows"/>) usa
/// o <c>sc.exe</c> do próprio Windows, e os testes usam uma de mentira.
/// </para>
/// </summary>
public interface IServicoWindows
{
    /// <summary>Modo de inicialização do serviço (null = não deu para ler).</summary>
    ModoServico? LerModo(string nome);

    /// <summary>Muda o modo de inicialização. Devolve false se o Windows recusou.</summary>
    bool Configurar(string nome, ModoServico modo);

    /// <summary>Para o serviço. Devolve false se o Windows recusou.</summary>
    bool Parar(string nome);

    /// <summary>Inicia o serviço. Devolve false se o Windows recusou.</summary>
    bool Iniciar(string nome);
}

/// <summary>
/// Suspende serviços de programas em segundo plano (estilo "Sleep Mode").
///
/// <para>
/// A regra é uma só: o app <b>não suspende nada por conta própria</b>. A
/// lista é montada por <see cref="ServicosSuspendiveis"/> (puro) e o
/// usuário escolhe. O que foi mudado fica guardado (em
/// <see cref="AppSettings.TweakBackup"/>) para desfazer.
/// </para>
///
/// <para>
/// Nada de segurança é tocado: antivírus, Defender e serviços do Windows
/// ficam de fora, sempre — na hora de suspender e na hora de restaurar.
/// </para>
///
/// <para>
/// A mudança de modo de inicialização é feita pelo <c>sc.exe</c> do próprio
/// Windows (o <c>ServiceController</c> do .NET exigiria uma referência nova,
/// e o app não pode ganhar dependência).
/// </para>
/// </summary>
public static class ServicosSuspender
{
    private const string PrefixoChave = "servico.";

    private static string Chave(string nome) => PrefixoChave + nome;

    /// <summary>
    /// Suspende os serviços escolhidos: o serviço para e o modo de
    /// inicialização vira Manual. O modo original fica guardado para
    /// desfazer — inclusive o "automático com atraso", que sem isso voltaria
    /// a subir no boot normal.
    ///
    /// <para>
    /// O backup é gravado <b>antes</b> da mudança (se o app cair no meio,
    /// o original já está no arquivo) e só é gravado de serviço que existe,
    /// que é automático ou manual, e que não é de segurança/suporte.
    /// </para>
    /// </summary>
    /// <param name="settings">Onde o modo original fica guardado (para desfazer).</param>
    /// <param name="servicos">Os serviços escolhidos pelo usuário.</param>
    /// <param name="alvo">Com quem falar com o Windows (padrão: o sc.exe).</param>
    /// <returns>Quantos serviços foram de fato suspensos.</returns>
    public static int Suspender(AppSettings settings, IEnumerable<ServicoSuspendivel> servicos, IServicoWindows? alvo = null)
    {
        var windows = alvo ?? ServicosWindows.Instancia;
        int suspensos = 0;

        foreach (var servico in servicos)
        {
            if (servico is null) continue;
            string nome = servico.Nome;
            if (string.IsNullOrWhiteSpace(nome)) continue;

            // Rede de segurança: mesmo chamando direto, serviço de
            // segurança, de suporte ou do próprio app não é tocado.
            if (!ServicosSuspendiveis.PodeOferecer(new ServicoInfo
            {
                Nome = nome,
                Caminho = servico.Caminho,
                ModoInicializacao = servico.ModoInicializacao,
            })) continue;

            try
            {
                var original = windows.LerModo(nome);
                if (original is null) continue;   // serviço não existe (ou não deu para ler)

                // Só automático e manual entram. Desativado, boot e system
                // ficam de fora — mexer neles não é desfazerível pelo app.
                if (!original.Restauravel) continue;

                // Primeiro para: se não conseguir parar, nada mudou e não
                // há nada para guardar.
                if (!windows.Parar(nome)) continue;

                // Guarda o modo original ANTES de mudar (e com o caminho,
                // para a blindagem da restauração).
                string chave = Chave(nome);
                if (!settings.TweakBackup.ContainsKey(chave))
                    settings.TweakBackup[chave] = original.Texto(servico.Caminho);

                if (original.Modo == ModoServico.Automatico &&
                    !windows.Configurar(nome, new ModoServico { Modo = ModoServico.Manual }))
                {
                    // O Windows recusou a mudança de modo. O backup fica: o
                    // serviço parou, e o desfazer ainda vale.
                    continue;
                }

                suspensos++;
            }
            catch
            {
                // Serviço protegido, já parado, sem permissão: seguir sem ele.
                // O que importa é que os outros sejam suspensos.
            }
        }
        return suspensos;
    }

    /// <summary>
    /// Devolve os serviços suspensos pelo app ao modo de inicialização que
    /// tinham. Quem não estiver no backup não é tocado.
    ///
    /// <remarks>
    /// Blindagem: o nome e o modo vêm do <c>settings.json</c>, que o usuário
    /// pode editar. Por isso o backup só é aceito se o formato for o que o
    /// app grava, o modo for um dos que o app produz (nunca "desativado") e o
    /// serviço não for de segurança nem de sistema. Backup fora do formato é
    /// descartado, não aplicado.
    ///
    /// <para>
    /// O backup só é apagado quando o serviço volta ao estado anterior.
    /// Se o Windows recusar, a entrada fica (para uma segunda tentativa);
    /// se o serviço tiver sido mexido por outra pessoa depois (o modo não
    /// está mais Manual), a entrada é descartada — a escolha de quem mexeu
    /// depois manda, e o app não sobrescreve.
    /// </para>
    /// </remarks>
    public static int Restaurar(AppSettings settings, IServicoWindows? alvo = null)
    {
        var windows = alvo ?? ServicosWindows.Instancia;
        var chaves = settings.TweakBackup.Keys
            .Where(k => k.StartsWith(PrefixoChave, StringComparison.Ordinal))
            .ToList();

        int restaurados = 0;
        foreach (var chave in chaves)
        {
            string nome = chave.Substring(PrefixoChave.Length);

            // Backup fora do formato (ou com modo que o app nunca grava):
            // descarta sem tocar em nada.
            var original = ModoServico.DoTexto(settings.TweakBackup[chave] ?? "", out string caminho);
            if (original is null)
            {
                settings.TweakBackup.Remove(chave);
                continue;
            }

            // Rede de segurança na restauração também: um backup forjado
            // não desliga o Defender nem mexe em serviço do Windows.
            if (!ServicosSuspendiveis.PodeOferecer(new ServicoInfo
            {
                Nome = nome,
                Caminho = caminho,
            }))
            {
                settings.TweakBackup.Remove(chave);
                continue;
            }

            try
            {
                // O serviço precisa continuar existindo: se sumiu, não há o
                // que restaurar. A entrada fica guardada (pode ser que a
                // leitura tenha falhado) — ela não atrapalha ninguém.
                var atual = windows.LerModo(nome);
                if (atual is null) continue;

                // O app só desfaz o que ele deixou em Manual. Se o modo está
                // diferente, alguém mexeu depois: o app não sobrescreve.
                if (atual.Modo != ModoServico.Manual)
                {
                    settings.TweakBackup.Remove(chave);
                    continue;
                }

                if (!windows.Configurar(nome, original))
                {
                    // O Windows recusou: o backup fica, para uma segunda
                    // tentativa.
                    continue;
                }

                if (original.Modo == ModoServico.Automatico)
                    windows.Iniciar(nome);

                settings.TweakBackup.Remove(chave);
                restaurados++;
            }
            catch
            {
                // Serviço sumiu entre a suspensão e agora: nada a fazer.
            }
        }
        return restaurados;
    }

    /// <summary>Os serviços que o app suspendeu (ainda guardados no backup).</summary>
    public static IReadOnlyList<string> Suspensos(AppSettings settings) =>
        settings.TweakBackup.Keys
            .Where(k => k.StartsWith(PrefixoChave, StringComparison.Ordinal))
            .Select(k => k.Substring(PrefixoChave.Length))
            .ToList();
}

/// <summary>
/// A implementação de verdade: fala com o Windows pelo <c>sc.exe</c>.
///
/// <para>
/// O <c>ServiceController</c> do .NET exigiria uma referência nova
/// (<c>System.ServiceProcess.ServiceController</c>) e o app não pode gain
/// dependência. O <c>sc.exe</c> já está no Windows e faz exatamente o que
/// precisa.
/// </para>
/// </summary>
public sealed class ServicosWindows : IServicoWindows
{
    /// <summary>A instância real (o sc.exe do Windows).</summary>
    public static ServicosWindows Instancia { get; } = new();

    public ModoServico? LerModo(string nome)
    {
        var (saida, codigo) = Sc("qc", nome);
        if (codigo != 0 || string.IsNullOrWhiteSpace(saida)) return null;

        int? modo = null;
        bool atrasado = false;
        foreach (var linha in saida.Split('\n'))
        {
            // "START_TYPE         : 2   AUTO_START"
            int pos = linha.IndexOf("START_TYPE", StringComparison.OrdinalIgnoreCase);
            if (pos >= 0 && modo is null)
            {
                int doisPontos = linha.IndexOf(':', pos);
                if (doisPontos >= 0)
                {
                    string resto = linha[(doisPontos + 1)..].Trim();
                    int fim = 0;
                    while (fim < resto.Length && char.IsDigit(resto[fim])) fim++;
                    if (fim > 0 && int.TryParse(resto[..fim], out int m)) modo = m;
                }
            }

            // "DELAYED_AUTO_START_INFO : YES"
            if (linha.Contains("DELAYED_AUTO_START_INFO", StringComparison.OrdinalIgnoreCase))
                atrasado = linha.Contains("YES", StringComparison.OrdinalIgnoreCase);
        }

        if (modo is null) return null;
        return new ModoServico { Modo = modo.Value, Atrasado = atrasado };
    }

    public bool Configurar(string nome, ModoServico modo)
    {
        string valor = modo.Modo switch
        {
            0 => "boot",
            1 => "system",
            4 => "disabled",
            ModoServico.Automatico => modo.Atrasado ? "delayed-auto" : "auto",
            ModoServico.Manual => "demand",
            _ => "demand"
        };

        var (_, codigo) = Sc("config", nome, "start=", valor);
        return codigo == 0;
    }

    public bool Parar(string nome)
    {
        var (_, codigo) = Sc("stop", nome);
        // 1062 = o serviço já estava parado (não é erro).
        return codigo == 0 || codigo == 1062;
    }

    public bool Iniciar(string nome)
    {
        var (_, codigo) = Sc("start", nome);
        // 1056 = o serviço já estava rodando.
        return codigo == 0 || codigo == 1056;
    }

    /// <summary>Roda o sc.exe e devolve a saída e o código de saída.</summary>
    private static (string Saida, int Codigo) Sc(params string[] argumentos)
    {
        try
        {
            var psi = new ProcessStartInfo("sc.exe")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            // ArgumentList escapa cada argumento: nome de serviço com espaço
            // ou aspas não vira outro comando.
            foreach (var a in argumentos) psi.ArgumentList.Add(a);

            using var p = Process.Start(psi);
            if (p is null) return ("", -1);
            string saida = p.StandardOutput.ReadToEnd();
            p.WaitForExit(15000);
            return (saida, p.ExitCode);
        }
        catch
        {
            return ("", -1);
        }
    }
}
