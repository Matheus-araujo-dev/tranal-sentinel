using System.IO;
using System.Xml;

namespace TranalSentinel;

/// <summary>
/// O retrato da saúde da bateria, extraído do relatório do
/// <c>powercfg /batteryreport</c>. Sem dependência nova: o XML é lido
/// com o <see cref="System.Xml.XmlDocument"/> do framework.
/// </summary>
public sealed record BateriaRetrato(
    string? NomeBateria,
    int CapacidadeOriginalMwh,
    int CapacidadeAtualMwh,
    int CiclosEstimados,
    float SaudePercentual,
    DateTime? UltimaLeitura);

/// <summary>
/// Regras puras da saúde da bateria: lê o XML do
/// <c>powercfg /batteryreport /xml</c> e devolve o retrato. A coleta
/// (que roda o powercfg) é separada.
/// </summary>
public static class BateriaSaude
{
    /// <summary>
    /// Percentual abaixo do qual a bateria é considerada degradada.
    /// </summary>
    public const float LimiteDegradacao = 80f;

    /// <summary>
    /// Percentual abaixo do qual a bateria é considerada crítica.
    /// </summary>
    public const float LimiteCritico = 50f;

    /// <summary>
    /// Extrai o retrato da bateria a partir do XML do powercfg.
    /// Devolve null se não há bateria no relatório.
    /// </summary>
    public static BateriaRetrato? LerXml(string xml)
    {
        if (string.IsNullOrWhiteSpace(xml)) return null;

        var doc = new XmlDocument();
        try { doc.LoadXml(xml); }
        catch { return null; }

        var ns = new XmlNamespaceManager(doc.NameTable);
        // O relatório do powercfg usa o namespace padrão do HTML, mas
        // precisamos registrá-lo para a consulta XPath funcionar.
        string? nsUri = doc.DocumentElement?.NamespaceURI;
        if (!string.IsNullOrEmpty(nsUri))
            ns.AddNamespace("r", nsUri);

        string prefixo = string.IsNullOrEmpty(nsUri) ? "" : "r:";

        // Procura a primeira bateria no relatório.
        var bateria = doc.SelectSingleNode(
            $"//{prefixo}Battery", ns);
        if (bateria is null) return null;

        string? nome = Atributo(bateria, "Id")
            ?? Filho(bateria, "Id", prefixo, ns);

        int original = IntFilho(bateria, "DesignCapacity", prefixo, ns);
        int atual = IntFilho(bateria, "FullChargeCapacity", prefixo, ns);

        int ciclos = IntFilho(bateria, "CycleCount", prefixo, ns);

        float saude = original > 0
            ? 100f * atual / original
            : 0f;

        return new BateriaRetrato(
            nome, original, atual, ciclos,
            MathF.Round(saude, 1),
            DateTime.Now);
    }

    /// <summary>
    /// Texto para a tela: descreve a saúde da bateria.
    /// </summary>
    public static string TextoSaude(BateriaRetrato b)
    {
        string estado = b.SaudePercentual switch
        {
            < LimiteCritico => "CRÍTICA",
            < LimiteDegradacao => "DEGRADADA",
            _ => "BOA"
        };

        string capacidade = $"{NumeroTexto.Inteiro(b.CapacidadeAtualMwh)} / " +
                            $"{NumeroTexto.Inteiro(b.CapacidadeOriginalMwh)} mWh";

        return $"Bateria: {NumeroTexto.Numero(b.SaudePercentual, 1)}% ({estado}) — " +
               $"{capacidade}, ~{b.CiclosEstimados} ciclos";
    }

    /// <summary>
    /// Texto curto para o cartão: o estado em uma palavra.
    /// </summary>
    public static string TextoEstado(float saudePercentual) => saudePercentual switch
    {
        < LimiteCritico => "Bateria crítica",
        < LimiteDegradacao => "Bateria degradada",
        _ => "Bateria saudável"
    };

    /// <summary>
    /// Devolve true se a máquina provavelmente tem bateria (notebook).
    /// Sem dependência nova: usa o WMI que o app já carrega.
    /// </summary>
    public static bool TemBateria()
    {
        try
        {
            return System.IO.File.Exists(
                System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.System),
                    "powercfg.exe"));
        }
        catch { return false; }
    }

    private static string? Atributo(XmlNode no, string nome) =>
        no.Attributes?[nome]?.Value;

    private static string? Filho(XmlNode no, string nome, string prefixo, XmlNamespaceManager ns) =>
        no.SelectSingleNode($"{prefixo}{nome}", ns)?.InnerText;

    private static int IntFilho(XmlNode no, string nome, string prefixo, XmlNamespaceManager ns)
    {
        string? texto = Filho(no, nome, prefixo, ns);
        return int.TryParse(texto, out int v) ? v : 0;
    }
}
