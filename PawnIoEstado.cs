namespace TranalSentinel;

/// <summary>
/// O que o código de erro do Windows diz sobre o driver PawnIO. Regra pura:
/// entra se o driver está registrado como instalado e o erro da abertura;
/// sai o motivo para a tela.
///
/// <para>
/// Até a v0.37.2 toda falha ao abrir o driver virava "o driver PawnIO não está
/// instalado" (e o aviso da CPU dizia "o driver não respondeu"). Medido no PC do
/// dono em 22/11/2026: o driver estava instalado e o serviço rodando, mas o
/// dispositivo <c>\Device\PawnIO</c> não existia (erro 2). Foi uma
/// desinstalação que o OpenRGB travou no meio. Reiniciar o dispositivo
/// resolveu. "Não está instalado" e "está instalado, mas travado" pedem
/// correções diferentes, e o app não pode trocar uma pela outra.
/// </para>
/// </summary>
public static class PawnIoEstado
{
    /// <summary>ERROR_FILE_NOT_FOUND: o dispositivo do driver não existe.</summary>
    public const int ErroSemDispositivo = 2;

    /// <summary>ERROR_ACCESS_DENIED: o processo não tem permissão.</summary>
    public const int ErroAcessoNegado = 5;

    /// <summary>
    /// O driver está instalado, mas sem o dispositivo que os programas abrem?
    /// É o estado que o botão CORRIGIR resolve reiniciando o dispositivo.
    /// </summary>
    public static bool Travado(bool instalado, int erro) => instalado && erro == ErroSemDispositivo;

    /// <summary>Intervalo mínimo entre duas reaberturas automáticas da coleta.</summary>
    public static readonly TimeSpan IntervaloDeReabertura = TimeSpan.FromMinutes(5);

    /// <summary>
    /// A coleta deve reabrir agora? O coletor que abriu o driver travado lê 0
    /// para sempre (medido em 22/11/2026): quando o driver volta, só reabrir a
    /// coleta traz a CPU de volta.
    ///
    /// <para>
    /// <paramref name="pendente"/> diz que o driver foi visto travado desde a
    /// última reabertura. Durante um reparo pelo CORRIGIR a decisão espera: o
    /// próprio reparo reabre a coleta, e duas reaberturas seguidas podiam
    /// deixar um coletor órfão.
    /// </para>
    /// </summary>
    /// <returns>Se reabre agora, e o novo valor de <paramref name="pendente"/>.</returns>
    public static (bool Reabrir, bool Pendente) DecidirReabertura(
        bool pendente, bool travadoAgora, bool respondendoAgora, bool reparando, DateTime agora, DateTime ultimaReabertura)
    {
        if (reparando) return (false, pendente);
        if (travadoAgora) return (false, true);
        if (!pendente || !respondendoAgora) return (false, pendente);
        if (agora - ultimaReabertura < IntervaloDeReabertura) return (false, true);
        return (true, false);
    }

    /// <summary>O motivo de o driver não ter aberto, para a tela.</summary>
    public static string MotivoDeNaoAbrir(bool instalado, int erro)
    {
        if (!instalado) return "O driver PawnIO não está instalado neste computador.";
        return erro switch
        {
            ErroSemDispositivo =>
                "O driver PawnIO está instalado, mas travado: o dispositivo dele não existe (isso acontece quando " +
                "uma desinstalação é interrompida por outro programa que usava o driver). O botão CORRIGIR reinicia o driver.",
            ErroAcessoNegado => "Sem permissão para abrir o driver PawnIO: o app precisa rodar como administrador.",
            _ => $"O driver PawnIO não abriu (erro {erro} do Windows).",
        };
    }
}
