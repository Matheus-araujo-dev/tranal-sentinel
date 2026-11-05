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

    /// <summary>
    /// Texto que vai para o backup: "<modo>;<atrasado>;<rodando>;<caminho>".
    ///
    /// <para>
    /// O "rodando" é o estado do serviço antes da mudança. Sem ele, o
    /// desfazer devolvia o modo mas deixava o serviço parado — e o app
    /// jurava que tinha restaurado.
    /// </para>
    /// </summary>
    public string Texto(string caminho, bool rodando) =>
        $"{Modo};{(Atrasado ? 1 : 0)};{(rodando ? 1 : 0)};{caminho}";

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
    public static ModoServico? DoTexto(string texto, out string caminho, out bool rodando)
    {
        caminho = "";
        rodando = false;
        if (string.IsNullOrWhiteSpace(texto)) return null;

        var partes = texto.Split(';');
        // O formato do app tem quatro campos. Um backup com menos (o de uma
        // versão antiga, ou forjado à mão) é descartado sem tocar em nada.
        if (partes.Length < 4) return null;
        if (!int.TryParse(partes[0], out int modo)) return null;
        if (!int.TryParse(partes[1], out int atrasado)) return null;
        if (!int.TryParse(partes[2], out int rodandoFlag)) return null;

        // O app só grava e só restaura estes dois modos. Desativado (4),
        // boot (0) e system (1) nunca foram gravados por ele — então um
        // backup que os traga não é do app, e é descartado.
        if (modo is not (Automatico or Manual)) return null;

        caminho = string.Join(";", partes[3..]);
        rodando = rodandoFlag == 1;
        return new ModoServico { Modo = modo, Atrasado = atrasado == 1 };
    }
}

/// <summary>
/// O que o Windows sabe sobre um serviço.
///
/// <para>
/// Tudo aqui vem do Windows (o <c>sc.exe</c>), nunca do
/// <c>settings.json</c>: o caminho do executável e o estado do serviço são
/// o que o próprio Windows registra. Um backup adulterado pelo usuário não
/// pode mentir sobre isso.
/// </para>
/// </summary>
public sealed record InfoServico(ModoServico Modo, string Caminho, bool Rodando);

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
    /// <summary>
    /// O que o Windows sabe sobre o serviço (null = não existe ou não deu
    /// para ler).
    /// </summary>
    InfoServico? Ler(string nome);

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
    /// inicialização vira Manual. O estado original fica guardado para
    /// desfazer — modo, atraso, se estava rodando e o caminho real do
    /// executável.
    ///
    /// <para>
    /// O backup é gravado <b>antes</b> da mudança (se o app cair no meio,
    /// o original já está no arquivo) e só de serviço que existe, que é
    /// automático ou manual, e que não é de segurança/suporte.
    /// </para>
    /// </summary>
    /// <param name="settings">Onde o estado original fica guardado (para desfazer).</param>
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
                var info = windows.Ler(nome);
                if (info is null) continue;   // serviço não existe (ou não deu para ler)

                // Só automático e manual entram. Desativado, boot e system
                // ficam de fora — mexer neles não é desfazerível pelo app.
                if (!info.Modo.Restauravel) continue;

                // A decisão final é sobre o que o WINDOWS registra, não
                // sobre o que a lista ou o backup dizem. Sem caminho
                // conhecido, não dá para saber se é serviço do Windows:
                // nesse caso, não é tocado.
                if (string.IsNullOrWhiteSpace(info.Caminho)) continue;
                if (!ServicosSuspendiveis.PodeOferecer(new ServicoInfo
                {
                    Nome = nome,
                    Caminho = info.Caminho,
                })) continue;

                // Guarda o estado original ANTES de mexer: modo, atraso,
                // se estava rodando e o caminho que o Windows registra.
                string chave = Chave(nome);
                bool gravouBackup = false;
                if (!settings.TweakBackup.ContainsKey(chave))
                {
                    settings.TweakBackup[chave] = info.Modo.Texto(info.Caminho, info.Rodando);
                    gravouBackup = true;
                }

                if (!windows.Parar(nome))
                {
                    // Não parou: nada mudou, e o backup que acabamos de
                    // gravar não serve para nada.
                    if (gravouBackup) settings.TweakBackup.Remove(chave);
                    continue;
                }

                if (info.Modo.Modo == ModoServico.Automatico)
                {
                    // O Windows pode recusar (serviço protegido, política de
                    // grupo). O backup fica: o serviço parou, e o desfazer
                    // ainda vale.
                    windows.Configurar(nome, new ModoServico { Modo = ModoServico.Manual });
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
    /// Devolve os serviços suspensos pelo app ao estado que tinham. Quem não
    /// estiver no backup não é tocado.
    ///
    /// <remarks>
    /// Blindagem: o nome vem do <c>settings.json</c>, que o usuário pode
    /// editar. Por isso o backup só é aceito se o formato for o que o app
    /// grava, o modo for um dos que o app produz (nunca "desativado") e o
    /// serviço não for de segurança nem de sistema. E a checagem de
    /// segurança usa o caminho <b>que o Windows registra</b> — não o texto do
    /// arquivo, que o usuário controla.
    ///
    /// <para>
    /// O backup só é apagado quando o serviço volta ao estado anterior. Se
    /// o Windows recusar, a entrada fica (para uma segunda tentativa); se o
    /// serviço tiver sido mexido por outra pessoa depois (o modo está fora
    /// do que o app mexe), a entrada é descartada — a escolha de quem mexeu
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
            var original = ModoServico.DoTexto(settings.TweakBackup[chave] ?? "", out _, out bool estavaRodando);
            if (original is null)
            {
                settings.TweakBackup.Remove(chave);
                continue;
            }

            try
            {
                // O que vale é o que o Windows sabe agora.
                var atual = windows.Ler(nome);
                if (atual is null) continue;   // serviço sumiu: a entrada fica (pode ser erro de leitura)

                // Rede de segurança na restauração também: um backup forjado
                // não desliga o Defender nem mexe em serviço do Windows. E
                // a checagem olha o caminho real, não o do arquivo.
                if (string.IsNullOrWhiteSpace(atual.Caminho) ||
                    !ServicosSuspendiveis.PodeOferecer(new ServicoInfo
                    {
                        Nome = nome,
                        Caminho = atual.Caminho,
                    }))
                {
                    settings.TweakBackup.Remove(chave);
                    continue;
                }

                bool mexeu = false;

                if (atual.Modo.Modo == original.Modo)
                {
                    // O modo é o mesmo que o app gravou: o app só parou o
                    // serviço (ou o Windows recusou a mudança de modo).
                    if (estavaRodando && !atual.Rodando)
                    {
                        if (!windows.Iniciar(nome)) continue;   // recusou: o backup fica
                        mexeu = true;
                    }
                }
                else if (atual.Modo.Modo == ModoServico.Manual && original.Modo == ModoServico.Automatico)
                {
                    // O app mudou o modo: volta ao original.
                    if (!windows.Configurar(nome, original)) continue;   // recusou: o backup fica
                    mexeu = true;
                    if (estavaRodando && !atual.Rodando)
                        windows.Iniciar(nome);
                }
                else
                {
                    // Alguém mexeu depois (o modo está fora do que o app
                    // mexe): a escolha de quem mexeu depois manda.
                    settings.TweakBackup.Remove(chave);
                    continue;
                }

                settings.TweakBackup.Remove(chave);
                if (mexeu) restaurados++;
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

    public InfoServico? Ler(string nome)
    {
        var (saida, codigo) = Sc("qc", nome);
        if (codigo != 0 || string.IsNullOrWhiteSpace(saida)) return null;

        int? modo = null;
        bool atrasado = false;
        string caminho = "";

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

            // "BINARY_PATH_NAME    : C:\Program Files\App\app.exe -s"
            if (caminho.Length == 0 &&
                linha.Contains("BINARY_PATH_NAME", StringComparison.OrdinalIgnoreCase))
            {
                int doisPontos = linha.IndexOf(':');
                if (doisPontos >= 0)
                    caminho = ExtrairCaminho(linha[(doisPontos + 1)..].Trim());
            }
        }

        if (modo is null) return null;

        var modoServico = new ModoServico { Modo = modo.Value, Atrasado = atrasado };
        return new InfoServico(modoServico, caminho, EstaRodando(nome));
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

    /// <summary>
    /// O serviço está rodando agora? (o "STATE : 4 RUNNING" do sc query)
    /// </summary>
    private static bool EstaRodando(string nome)
    {
        var (saida, codigo) = Sc("query", nome);
        if (codigo != 0 || string.IsNullOrWhiteSpace(saida)) return false;

        foreach (var linha in saida.Split('\n'))
        {
            string limpa = linha.TrimStart();
            if (!limpa.StartsWith("STATE", StringComparison.OrdinalIgnoreCase)) continue;

            int doisPontos = limpa.IndexOf(':');
            if (doisPontos < 0) continue;

            string resto = limpa[(doisPontos + 1)..].Trim();
            int fim = 0;
            while (fim < resto.Length && char.IsDigit(resto[fim])) fim++;
            if (fim > 0 && int.TryParse(resto[..fim], out int estado))
                return estado == 4;   // 4 = RUNNING
        }
        return false;
    }

    /// <summary>
    /// Tira o caminho do executável do "BINARY_PATH_NAME" do sc.exe. O
    /// caminho é o que o Windows registra — e é nele que se reconhece um
    /// serviço do Windows (system32) ou de segurança.
    /// </summary>
    private static string ExtrairCaminho(string texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return "";
        texto = texto.Trim();

        if (texto.StartsWith("\""))
        {
            int fim = texto.IndexOf('"', 1);
            return fim > 1 ? texto[1..fim] : texto.Trim('"');
        }

        // Sem aspas: o caminho vai até o executável; o resto são argumentos
        // do serviço ("-s", "-k netsvcs").
        foreach (var marcador in new[] { ".exe", ".dll", ".sys" })
        {
            int pos = texto.IndexOf(marcador, StringComparison.OrdinalIgnoreCase);
            if (pos > 0) return texto[..(pos + marcador.Length)];
        }

        int espaco = texto.IndexOf(' ');
        return espaco > 0 ? texto[..espaco] : texto;
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
