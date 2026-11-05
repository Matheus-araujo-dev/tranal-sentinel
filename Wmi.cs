using System.Collections.Generic;
using System.Management;

namespace TranalSentinel;

/// <summary>
/// Uma linha de consulta WMI convertida para texto.
/// </summary>
/// <remarks>
/// Tudo vira string porque o objetivo é comparar estados ("o que mudou"), não
/// fazer conta. Cada acesso é tolerante: chave ausente devolve string vazia.
/// </remarks>
public sealed class LinhaWmi
{
    private readonly Dictionary<string, string> _valores = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Valor da propriedade (string vazia se a propriedade não existir na linha).</summary>
    public string this[string chave] => _valores.TryGetValue(chave, out var v) ? v : "";

    /// <summary>Guarda o valor de uma propriedade.</summary>
    public void Definir(string chave, string valor) => _valores[chave] = valor;

    /// <summary>Valor da propriedade como número (0 se não existir ou não for número).</summary>
    public long ValorLong(string chave) =>
        _valores.TryGetValue(chave, out var v) && long.TryParse(v, out var n) ? n : 0;

    /// <summary>Valor da propriedade como booleano (false se não existir ou não for booleano).</summary>
    public bool ValorBool(string chave) =>
        _valores.TryGetValue(chave, out var v) && bool.TryParse(v, out var b) && b;
}

/// <summary>
/// Consultas WMI compartilhadas.
/// </summary>
/// <remarks>
/// Existe como classe própria (e não como método privado dentro da coleta de
/// saúde) porque mais de um módulo precisa ler o WMI: a aba de saúde e a
/// linha do tempo do PC. Uma única implementação evita as duas divergirem.
/// </remarks>
public static class Wmi
{
    /// <summary>Consulta WMI no namespace padrão (root\cimv2) e devolve as linhas já convertidas para texto.</summary>
    public static List<LinhaWmi> Consultar(string consulta) => ConsultarEm(@"root\cimv2", consulta);

    /// <summary>Consulta WMI em um namespace específico e devolve as linhas já convertidas para texto.</summary>
    /// <remarks>
    /// Falha de consulta devolve lista vazia em vez de estourar: uma consulta WMI
    /// quebrada não pode derrubar a coleta inteira.
    /// </remarks>
    public static List<LinhaWmi> ConsultarEm(string espacoDeNomes, string consulta)
    {
        var lista = new List<LinhaWmi>();
        try
        {
            var escopo = new ManagementScope(espacoDeNomes);
            escopo.Connect();

            using var busca = new ManagementObjectSearcher(escopo, new ObjectQuery(consulta));
            using var objetos = busca.Get();
            foreach (ManagementObject objeto in objetos)
            {
                var linha = new LinhaWmi();
                foreach (var propriedade in objeto.Properties)
                {
                    try { linha.Definir(propriedade.Name, propriedade.Value?.ToString() ?? ""); }
                    catch { }
                }
                lista.Add(linha);
            }
        }
        catch { }
        return lista;
    }
}
