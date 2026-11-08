using System.Globalization;
using System.IO;
using System.Text;

namespace TranalSentinel;

/// <summary>
/// TRANAL BANCO na linha de comando: mostra o estado do banco de dados do
/// histórico e repara o banco quando ele está estragado.
///
/// <para>
/// Uso: <c>TranalSentinel.exe --banco</c> ou
/// <c>TranalSentinel.exe --banco --reparar</c>
///
/// <para>
/// É o mesmo caminho do app (o mesmo <see cref="Banco"/>), sem abrir janela.
/// Serve para conferir o banco por script e para diagnosticar uma máquina
/// sem passar pela interface.
///
/// <para>
/// O reparo pela linha de comando reconstrói o banco com o que der para ler
/// e <b>guarda o banco velho como cópia datada</b> — o arquivo estragado não
/// é apagado. Ele só acontece quando o usuário pede.
/// </para>
/// </summary>
public static class BancoCli
{
    /// <summary>Deu certo.</summary>
    public const int CodigoOk = 0;

    /// <summary>Erro de execução (o banco não abre, o reparo falhou).</summary>
    public const int CodigoErro = 1;

    /// <summary>Texto de uso.</summary>
    public static string TextoUso => """
        TRANAL SENTINEL · BANCO DE DADOS DO HISTÓRICO (linha de comando)

        Uso:
          TranalSentinel.exe --banco
          TranalSentinel.exe --banco --reparar
          TranalSentinel.exe --banco --pasta <pasta>

        Sem argumento: mostra o estado do banco (integridade, tamanho,
        quantas amostras de sensor e quantos registros de uso ele tem).

        Com --reparar: reconstrói o banco quando ele está estragado. O banco
        velho é guardado como cópia datada na mesma pasta — ele não é
        apagado.

        Com --pasta: olha o banco de outra pasta (uma cópia, por exemplo),
        em vez do banco de verdade do usuário.
        """;

    /// <summary>Executa a CLI do banco.</summary>
    public static int Executar(string[] args)
    {
        try
        {
            Console.OutputEncoding = Encoding.UTF8;

            // Apontar para outra pasta serve para conferir uma cópia do
            // banco (e reparar a cópia, sem mexer no banco de verdade do
            // usuário).
            string? pasta = ValorDoArgumento(args, "--pasta");
            if (!string.IsNullOrWhiteSpace(pasta))
            {
                Banco.PastaOverride = pasta;

                // O CSV antigo também é isolado: a pasta indicada não pode
                // receber o histórico do usuário de verdade (o que seria
                // copiar o histórico para fora da máquina).
                string legado = Path.Combine(pasta, "legado");
                LegadoCsv.PastaSensoresOverride = legado;
                LegadoCsv.PastaUsoOverride = legado;
            }

            bool reparar = Array.Exists(args, a => string.Equals(a, "--reparar", StringComparison.OrdinalIgnoreCase));

            if (reparar)
            {
                var resultado = Banco.Reparar();
                Console.WriteLine(resultado.Relato);

                return resultado.Sucesso ? CodigoOk : CodigoErro;
            }

            var dados = Banco.Ver();
            Console.WriteLine(MontarRelatorio(dados));

            return CodigoOk;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Erro inesperado: " + ex.Message);
            return CodigoErro;
        }
    }

    /// <summary>
    /// O valor de um argumento ("--pasta &lt;valor&gt;"), ou null quando ele
    /// não está na linha de comando.
    /// </summary>
    private static string? ValorDoArgumento(string[] args, string nome)
    {
        int posicao = Array.FindIndex(args, a => string.Equals(a, nome, StringComparison.OrdinalIgnoreCase));
        if (posicao < 0 || posicao + 1 >= args.Length) return null;

        return args[posicao + 1];
    }

    /// <summary>
    /// O relatório do banco, em texto. É o mesmo retrato que a aba
    /// TRANAL SAÚDE mostra — a tela e a linha de comando contam a mesma
    /// história.
    /// </summary>
    public static string MontarRelatorio(Banco.Diagnostico dados)
    {
        var texto = new StringBuilder();

        texto.AppendLine("TRANAL SENTINEL · BANCO DE DADOS DO HISTÓRICO");
        texto.AppendLine();
        texto.AppendLine("Estado: " + (dados.Estado == "ok" ? "íntegro" : dados.Estado));
        texto.AppendLine("Arquivo: " + dados.Caminho);
        texto.AppendLine("Tamanho: " + TamanhoLegivel(dados.TamanhoBytes));
        texto.AppendLine("Amostras de sensor: " + dados.Amostras.ToString(CultureInfo.InvariantCulture));
        texto.AppendLine("Registros de uso: " + dados.Uso.ToString(CultureInfo.InvariantCulture));

        var dias = Banco.DiasComUso();
        texto.AppendLine("Dias com histórico de uso: " + dias.Count.ToString(CultureInfo.InvariantCulture));

        if (dados.CopiaAvariada != null)
            texto.AppendLine("Cópia avariada guardada: " + dados.CopiaAvariada);

        return texto.ToString();
    }

    /// <summary>
    /// O tamanho em bytes, KB ou MB (sem locale: o número não pode depender
    /// do Windows).
    /// </summary>
    private static string TamanhoLegivel(long bytes)
    {
        if (bytes < 1024) return bytes.ToString(CultureInfo.InvariantCulture) + " B";
        if (bytes < 1024 * 1024) return (bytes / 1024.0).ToString("0.#", CultureInfo.InvariantCulture) + " KB";
        return (bytes / (1024.0 * 1024.0)).ToString("0.#", CultureInfo.InvariantCulture) + " MB";
    }
}
