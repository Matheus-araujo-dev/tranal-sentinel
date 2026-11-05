namespace TranalSetup.Dominio;

/// <summary>O que o instalador deve fazer, conforme os argumentos de linha de comando.</summary>
public enum ModoExecucao
{
    /// <summary>Abre a janela de instalação.</summary>
    Interface,

    /// <summary>Instala em modo silencioso (linha de comando).</summary>
    Instalar,

    /// <summary>Desinstala em modo silencioso (linha de comando).</summary>
    Desinstalar,
}

/// <summary>
/// Argumentos de linha de comando do instalador já interpretados.
///
/// O contrato é:
///   (sem argumento)          → abre a janela de instalação
///   --instalar &lt;pasta&gt;   → instala em modo silencioso
///   --desinstalar            → desinstala
///   --simular                → mostra o plano e não altera nada
/// O --simular pode vir sozinho (mostra o plano das três operações) ou junto de
/// --instalar/--desinstalar (mostra o plano só daquela operação).
/// </summary>
public sealed record ArgumentosLinhaComando
{
    /// <summary>Operação pedida.</summary>
    public ModoExecucao Modo { get; init; } = ModoExecucao.Interface;

    /// <summary>Se é uma simulação (não altera nada no sistema).</summary>
    public bool Simular { get; init; }

    /// <summary>Pasta do payload informada em --instalar.</summary>
    public string? PastaPayload { get; init; }

    /// <summary>Se os argumentos são válidos.</summary>
    public bool Valido { get; init; } = true;

    /// <summary>Motivo da invalidade (quando <see cref="Valido"/> é falso).</summary>
    public string? Erro { get; init; }

    /// <summary>
    /// Interpreta a linha de comando. Devolve um resultado inválido (com o motivo)
    /// em vez de lançar exceção: quem chama decide como avisar o usuário.
    /// </summary>
    public static ArgumentosLinhaComando Analisar(IReadOnlyList<string> argumentos)
    {
        var modo = ModoExecucao.Interface;
        bool simular = false;
        string? payload = null;

        for (int i = 0; i < argumentos.Count; i++)
        {
            string arg = argumentos[i] ?? "";

            if (arg.Equals("--simular", StringComparison.OrdinalIgnoreCase))
            {
                simular = true;
                continue;
            }

            if (arg.Equals("--desinstalar", StringComparison.OrdinalIgnoreCase))
            {
                if (modo == ModoExecucao.Instalar)
                    return Invalido("Escolha apenas uma operação: --instalar ou --desinstalar.");
                modo = ModoExecucao.Desinstalar;
                continue;
            }

            if (arg.StartsWith("--instalar=", StringComparison.OrdinalIgnoreCase))
            {
                if (modo == ModoExecucao.Desinstalar)
                    return Invalido("Escolha apenas uma operação: --instalar ou --desinstalar.");
                modo = ModoExecucao.Instalar;
                payload = arg["--instalar=".Length..];
                continue;
            }

            if (arg.Equals("--instalar", StringComparison.OrdinalIgnoreCase))
            {
                if (modo == ModoExecucao.Desinstalar)
                    return Invalido("Escolha apenas uma operação: --instalar ou --desinstalar.");
                modo = ModoExecucao.Instalar;
                if (i + 1 >= argumentos.Count || argumentos[i + 1].StartsWith("--", StringComparison.Ordinal))
                    return Invalido("A opção --instalar precisa da pasta do payload. Exemplo: --instalar \"C:\\Tranal\"");
                payload = argumentos[++i];
                continue;
            }

            return Invalido($"Opção desconhecida: {arg}");
        }

        if (modo == ModoExecucao.Instalar && string.IsNullOrWhiteSpace(payload))
            return Invalido("A opção --instalar precisa da pasta do payload.");

        return new ArgumentosLinhaComando { Modo = modo, Simular = simular, PastaPayload = payload };
    }

    private static ArgumentosLinhaComando Invalido(string erro)
        => new() { Valido = false, Erro = erro };
}
