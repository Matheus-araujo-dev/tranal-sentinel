using System.IO;
using System.Runtime.CompilerServices;

namespace TranalSentinel.Tests;

/// <summary>
/// O banco de dados do histórico nos testes.
///
/// <para>
/// O banco mora no <c>%LOCALAPPDATA%</c> do usuário — e nenhum teste pode
/// mexer no banco de verdade: a suíte abriria o arquivo do dono, criaria
/// o banco dele e importaria o histórico antigo dele. Por isso, desde o
/// começo da execução, o banco e as pastas do CSV antigo apontam para
/// uma pasta temporária.
///
/// <para>
/// Os testes que precisam do seu próprio banco isolam ainda mais (uma
/// pasta por teste), e no fim devolvem o caminho padrão — assim um teste
/// não herda o banco do teste anterior.
/// </para>
/// </summary>
internal static class BancoDeTeste
{
    /// <summary>A pasta raiz dos bancos de teste (da execução inteira).</summary>
    internal static string PastaRaiz { get; } = Path.Combine(
        Path.GetTempPath(), "tranal-banco-testes-" + Guid.NewGuid().ToString("N"));

    /// <summary>A pasta do CSV antigo apontada por padrão.</summary>
    internal static string PastaLegada => Path.Combine(PastaRaiz, "legado");

    /// <summary>
    /// Redireciona o banco e o CSV antigo para a pasta temporária, antes de
    /// qualquer teste rodar.
    /// </summary>
    [ModuleInitializer]
    internal static void Redirecionar()
    {
        Banco.PastaOverride = PastaRaiz;
        LegadoCsv.PastaSensoresOverride = PastaLegada;
        LegadoCsv.PastaUsoOverride = PastaLegada;
    }

    /// <summary>
    /// Dá ao teste um banco só dele (e um CSV antigo só dele), sem tocar
    /// nos outros.
    /// </summary>
    internal static (string Banco, string Legada) Isolar()
    {
        string id = Guid.NewGuid().ToString("N");

        string pastaBanco = Path.Combine(PastaRaiz, "banco-" + id);
        string pastaLegada = Path.Combine(PastaRaiz, "legado-" + id);

        Banco.Fechar();
        Banco.PastaOverride = pastaBanco;
        LegadoCsv.PastaSensoresOverride = pastaLegada;
        LegadoCsv.PastaUsoOverride = pastaLegada;

        return (pastaBanco, pastaLegada);
    }

    /// <summary>
    /// Devolve o caminho padrão (o da pasta temporária da execução) e
    /// fecha o banco do teste.
    /// </summary>
    internal static void Restaurar()
    {
        Banco.Fechar();
        Banco.PastaOverride = PastaRaiz;
        LegadoCsv.PastaSensoresOverride = PastaLegada;
        LegadoCsv.PastaUsoOverride = PastaLegada;
    }
}
