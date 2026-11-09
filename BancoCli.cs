using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;

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
          TranalSentinel.exe --banco --reparar --confirmar
          TranalSentinel.exe --banco --pasta <pasta>

        Sem argumento: mostra o estado do banco (integridade, tamanho,
        quantas amostras de sensor e quantos registros de uso ele tem).

        Com --reparar: reconstrói o banco quando ele está estragado. O banco
        velho é guardado como cópia datada na mesma pasta — ele não é
        apagado.

        Com --confirmar: a confirmação do reparo. Na tela ela é um botão;
        na linha de comando, é esta flag. Sem ela, o reparo não acontece:
        trocar o arquivo do histórico é decisão do usuário, e uma linha de
        comando digitada sem querer não pode tomar essa decisão.

        O reparo também exige o TRANAL SENTINEL fechado: com o app
        aberto, o Windows segura o arquivo do banco de dados e a troca
        do arquivo não dá certo.

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
            bool confirmar = Array.Exists(args, a => string.Equals(a, "--confirmar", StringComparison.OrdinalIgnoreCase));

            if (reparar)
            {
                // O reparo reconstrói o banco e troca o arquivo no lugar.
                // Duas coisas precisam estar resolvidas antes: a
                // confirmação do usuário e o app estar fechado.
                //
                // O app aberto é o que mais aparece: o TRANAL SENTINEL
                // entra sozinho com o Windows e fica na bandeja, e o
                // Windows segura o arquivo do banco enquanto ele roda. A
                // troca falharia só no último passo — depois de todo o
                // trabalho de reconstruir o banco, e com um relatório que
                // não dizia a verdade. A recusa vem antes: o usuário
                // fecha o app e repete o comando.
                var pendencias = new List<string>();

                if (!confirmar)
                {
                    pendencias.Add("Falta a confirmação: repita o comando com --confirmar.");
                }

                if (InstanciaUnica.EstaRodando())
                {
                    pendencias.Add("O TRANAL SENTINEL está aberto: feche o app (o ícone na bandeja) antes de reparar.");
                }

                if (pendencias.Count > 0)
                {
                    Console.WriteLine("O reparo reconstrói o banco de dados e guarda o banco velho como cópia datada.");
                    foreach (string pendencia in pendencias)
                        Console.WriteLine(pendencia);
                    Console.Out.Flush();

                    return CodigoErro;
                }

                // A trava é segurada durante o reparo: se o app subir no
                // meio da troca, ele abriria o banco velho e as gravações
                // dele iriam para o arquivo renomeado — o app continuaria
                // escrevendo num banco que não é mais o do caminho.
                Mutex? trava = InstanciaUnica.Segurar();

                if (trava == null)
                {
                    // O app subiu entre a conferência e o reparo.
                    Console.WriteLine("O TRANAL SENTINEL abriu enquanto o reparo começava. Feche o app e repita o comando.");
                    Console.Out.Flush();

                    return CodigoErro;
                }

                try
                {
                    var resultado = Banco.Reparar();
                    Console.WriteLine(resultado.Relato);

                    // Fica no diário do app: é uma ação que mexeu no arquivo do
                    // histórico, e o diário é onde o usuário vê o que o app fez.
                    try
                    {
                        RegistroDiario.Registrar($"banco: reparo pela linha de comando — {(resultado.Sucesso ? "ok" : "falhou")}: {resultado.Relato}");
                    }
                    catch
                    {
                        // O diário é um extra: se falhar, o reparo já foi
                        // aplicado e isso não pode virar mais um erro.
                    }

                    Console.Out.Flush();

                    return resultado.Sucesso ? CodigoOk : CodigoErro;
                }
                finally
                {
                    // A trava é devolvida: o app pode subir depois do
                    // reparo, e ele precisa conseguir.
                    try { trava.ReleaseMutex(); } catch { /* já foi devolvida */ }
                    trava.Dispose();
                }
            }

            var dados = Banco.Ver();
            Console.WriteLine(MontarRelatorio(dados));

            // O processo termina logo depois: sem o flush, a saída pode
            // ficar no buffer quando a saída for um arquivo (redirecionada).
            Console.Out.Flush();

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
        texto.AppendLine("Amostras de sensor: " + ContagemLegivel(dados.Amostras));
        texto.AppendLine("Registros de uso: " + ContagemLegivel(dados.Uso));
        texto.AppendLine("Registros de tráfego: " + ContagemLegivel(dados.Trafego));

        // A contagem de dias vem do MESMO retrato das linhas acima: uma
        // segunda leitura do banco podia ver outro banco (o reparo troca
        // o arquivo no meio) — e o relatório mostraria dois números de
        // mundos diferentes.
        texto.AppendLine("Dias com histórico de uso: " + dados.DiasComUso.ToString(CultureInfo.InvariantCulture));

        if (dados.CopiaAvariada != null)
            texto.AppendLine("Cópia avariada guardada: " + dados.CopiaAvariada);

        // O aviso do banco (a migração do histórico antigo ter ficado
        // pela metade, por exemplo) é o que o usuário precisa saber: sem
        // ele, o relatório parecia completo quando não está.
        if (!string.IsNullOrWhiteSpace(dados.Aviso))
            texto.AppendLine("Aviso: " + dados.Aviso);

        return texto.ToString();
    }

    /// <summary>
    /// A contagem de linhas, ou "não deu para contar" quando a consulta
    /// falhou (o banco estragado). Número errado é pior que número
    /// ausente.
    /// </summary>
    private static string ContagemLegivel(long linhas) =>
        linhas < 0 ? "não deu para contar" : linhas.ToString(CultureInfo.InvariantCulture);

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
