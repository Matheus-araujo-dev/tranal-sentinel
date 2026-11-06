using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace TranalSentinel;

/// <summary>
/// TRANAL S.M.A.R.T. na linha de comando: lê a tabela de atributos de cada
/// disco e mostra o resultado em texto.
///
/// <para>
/// Uso: <c>TranalSentinel.exe --smart</c>
///
/// É o mesmo caminho do app (mesmo leitor, mesmo decodificador, mesma
/// regra), sem abrir janela. Serve para conferir a leitura por script e
/// para diagnosticar uma máquina sem passar pela interface.
/// </para>
/// </summary>
public static class SmartCli
{
    /// <summary>Deu certo.</summary>
    public const int CodigoOk = 0;

    /// <summary>Erro de execução (sem permissão, disco protegido...).</summary>
    public const int CodigoErro = 1;

    /// <summary>Texto de uso.</summary>
    public static string TextoUso => """
        TRANAL SENTINEL · TRANAL S.M.A.R.T. (linha de comando)

        Uso:
          TranalSentinel.exe --smart

        Lê o S.M.A.R.T. de cada disco e mostra a tabela de atributos
        (id, nome, flags, valor, pior, limiar e valor cru).

        Somente leitura: nada é gravado no disco.
        Precisa de execução como administrador.
        """;

    /// <summary>Executa a CLI do S.M.A.R.T.</summary>
    public static int Executar(string[] args)
    {
        try
        {
            Console.OutputEncoding = Encoding.UTF8;

            Console.WriteLine(MontarRelatorio(SmartLeitor.LerTodos()));

            return CodigoOk;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Erro inesperado: " + ex.Message);
            return CodigoErro;
        }
    }

    /// <summary>Monta o relatório em texto.</summary>
    public static string MontarRelatorio(IReadOnlyList<SmartDisco> discos)
    {
        var sb = new StringBuilder();

        sb.AppendLine("TRANAL SENTINEL · TRANAL S.M.A.R.T.");
        sb.AppendLine($"Leitura do S.M.A.R.T. dos discos · {DateTime.Now:dd/MM/yyyy HH:mm:ss}");
        sb.AppendLine(new string('=', 78));
        sb.AppendLine();

        if (discos.Count == 0)
        {
            sb.AppendLine("Nenhum disco encontrado.");
            return sb.ToString();
        }

        foreach (SmartDisco disco in discos)
        {
            sb.AppendLine($"DISCO {disco.Numero} · {disco.Modelo}");
            sb.AppendLine($"  {disco.Meta}");

            if (disco.FalhaPrevista) sb.AppendLine("  O próprio disco avisa que vai falhar (SMART PredictFailure).");
            if (!string.IsNullOrWhiteSpace(disco.Resumo)) sb.AppendLine($"  Situação: {disco.Resumo}");
            if (disco.VidaRestantePercent is int vida) sb.AppendLine($"  Vida útil restante: {vida.ToString(CultureInfo.CurrentCulture)} %");

            if (!disco.TemDados)
            {
                sb.AppendLine("  Sem SMART: " + (string.IsNullOrWhiteSpace(disco.Observacao) ? "este disco não expõe o S.M.A.R.T." : disco.Observacao));
                sb.AppendLine();
                continue;
            }

            if (disco.TemAtributos)
            {
                sb.AppendLine();
                sb.AppendLine($"  {"ID",-6} {"Nome",-26} {"Flags",-8} {"Valor",5} {"Pior",5} {"Limiar",7} {"Tipo",-10} Valor cru");
                sb.AppendLine("  " + new string('-', 90));

                foreach (SmartAtributo a in disco.Atributos)
                {
                    sb.AppendLine($"  {a.IdTexto,-6} {a.Nome,-26} {a.FlagsTexto,-8} "
                        + $"{a.Valor.ToString(CultureInfo.CurrentCulture),5} {a.Pior.ToString(CultureInfo.CurrentCulture),5} "
                        + $"{a.LimiarTexto,7} {a.TipoTexto,-10} {a.CruTexto}");
                }
            }

            if (disco.TemContadores)
            {
                sb.AppendLine();
                foreach (SmartContador c in disco.Contadores)
                    sb.AppendLine($"  {c.Nome}: {c.Valor}");
            }

            sb.AppendLine();
        }

        sb.AppendLine("Somente leitura: nada foi gravado nos discos.");

        return sb.ToString();
    }
}
