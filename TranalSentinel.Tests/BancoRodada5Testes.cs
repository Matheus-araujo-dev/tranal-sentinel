using System;
using System.IO;
using System.Text;
using Xunit;

namespace TranalSentinel.Tests;

/// <summary>
/// Testes da quinta rodada: a verificação de integridade do banco passa a
/// ser uma só — a completa.
///
/// <para>
/// O achado: a verificação rápida (<c>quick_check</c>) não confere o
/// conteúdo dos índices contra o das tabelas, e o esquema do TRANAL SENTINEL
/// tem dois índices na tabela de uso — um deles é a chave que impede a mesma
/// troca de ser gravada duas vezes. Um banco com o índice fora de sintonia
/// passava por "ok" no retrato automático. Medido em 09/11/2026: o
/// <c>quick_check</c> respondeu "ok" num banco de 40 linhas cujo índice
/// <c>sqlite_autoindex_uso_1</c> apontava para uma linha que não existia
/// mais, enquanto o <c>integrity_check</c> respondia "row 40 missing from
/// index sqlite_autoindex_uso_1".
///
/// <para>
/// Como as duas verificações custam o mesmo tempo (cerca de 60 ms num banco
/// com 258 mil amostras e 18,6 MB, medido em 09/11/2026), o caminho rápido
/// foi removido: uma só verificação, sem alternativa. O estado do banco na
/// tela, o relatório da linha de comando e o reparo passam a dizer a mesma
/// coisa sobre o mesmo arquivo.
///
/// <para>
/// A classe fica na coleção "Banco": o banco, as pastas do CSV antigo e a
/// trava de instância única são estado global, e o xUnit roda as classes da
/// mesma coleção em série.
/// </para>
/// </summary>
[Collection("Banco")]
public class BancoRodada5Testes : IDisposable
{
    private const string Processo = "chrome.exe";
    private const string ProcessoFora = "chromy.exe";

    private string? _pastaBanco;
    private string? _pastaLegada;

    public void Dispose()
    {
        BancoDeTeste.Restaurar();

        foreach (string? pasta in new[] { _pastaBanco, _pastaLegada })
        {
            if (pasta == null) continue;
            try
            {
                if (Directory.Exists(pasta)) Directory.Delete(pasta, true);
            }
            catch
            {
                // A pasta é temporária: se o Windows ainda estiver segurando o
                // arquivo, ela some sozinha depois.
            }
        }
    }

    private void UsarPastasTemporarias()
    {
        (_pastaBanco, _pastaLegada) = BancoDeTeste.Isolar();
    }

    // ------------------------------------------------------------------ o defeito

    [Fact]
    public void RetratoNaoDizQueOTudoEstaBemComOIndiceForaDeSintonia()
    {
        UsarPastasTemporarias();

        GravarTrocasDeUso(5);
        DesalinharIndice(Banco.Caminho);

        var retrato = Banco.Ver();

        // O retrato automático é o que a tela do TRANAL SAÚDE mostra. Com o
        // índice fora de sintonia, ele não pode dizer que o banco está
        // íntegro: o "ok" mentiroso é o pior jeito de errar.
        Assert.NotEqual("ok", retrato.Estado);
        Assert.Contains("precisa de reparo", retrato.Estado);
    }

    [Fact]
    public void VerificacaoDeIntegridadeEncontraOIndiceForaDeSintonia()
    {
        UsarPastasTemporarias();

        GravarTrocasDeUso(5);
        DesalinharIndice(Banco.Caminho);

        using var banco = Sqlite.Abrir(Banco.Caminho);
        Assert.NotNull(banco);

        Assert.NotEqual("ok", banco!.VerificarIntegridade());
    }

    [Fact]
    public void ReparoReconstroiOBancoComOIndiceForaDeSintonia()
    {
        UsarPastasTemporarias();

        GravarTrocasDeUso(5);
        DesalinharIndice(Banco.Caminho);

        var resultado = Banco.Reparar();

        Assert.True(resultado.Sucesso, resultado.Relato);

        // O banco novo está inteiro — e as trocas de uso voltaram: o
        // estrago estava no índice, não nos dados.
        var retrato = Banco.Ver();
        Assert.Equal("ok", retrato.Estado);
        Assert.Equal(5, retrato.Uso);
    }

    [Fact]
    public void BancoSaudavelContinuaDizendoQueEstaIntegro()
    {
        UsarPastasTemporarias();

        GravarTrocasDeUso(3);

        var retrato = Banco.Ver();

        // O outro lado da regra: a verificação não pode acusar estrago onde
        // não há. Um aviso falso faz o usuário desconfiar do aviso verdadeiro.
        Assert.Equal("ok", retrato.Estado);
        Assert.Equal(3, retrato.Uso);
    }

    // ------------------------------------------------------------------ relato com contagem que não deu

    [Fact]
    public void RelatoDoSalvoNaoDizQueSalvouMenosUm()
    {
        // -1 é "não deu para contar", não um número. A contagem do banco
        // novo pode falhar depois do COMMIT (disco cheio, antivírus com o
        // arquivo) — e "-1 amostras foram salvos" seria um número que não
        // existe.
        string texto = Banco.RelatoDoSalvo(-1, 0);

        Assert.DoesNotContain("-1", texto);
        Assert.Contains("não deu para contar as amostras de sensor", texto);
        Assert.Contains("0 registros de uso", texto);
    }

    [Fact]
    public void RelatoDoSalvoComAsDuasContagensCertas()
    {
        string texto = Banco.RelatoDoSalvo(191, 10);

        Assert.Contains("191 amostras de sensor", texto);
        Assert.Contains("10 registros de uso", texto);
        Assert.Contains("foram salvos", texto);
    }

    [Fact]
    public void RelatoDaPerdaNaoInventaQuantidadeQuandoOCountDoBancoNovoFalha()
    {
        // O banco velho tinha 300 amostras e 60 registros de uso; o banco
        // novo não deixou contar o que entrou. A perda é a diferença entre
        // as duas pontas — sem a ponta nova, não há diferença: há um
        // número inventado.
        string texto = Banco.RelatoDaPerda(300, -1, 60, -1);

        Assert.DoesNotContain("Se perderam 301", texto);
        Assert.DoesNotContain("Se perderam 61", texto);
        Assert.DoesNotContain("-1", texto);
        Assert.Contains("Não deu para contar as amostras que entraram no banco novo", texto);
        Assert.Contains("Não deu para contar os registros de uso que entraram no banco novo", texto);
    }

    [Fact]
    public void RelatoDaPerdaComOCountDoBancoNovoCerto()
    {
        string texto = Banco.RelatoDaPerda(300, 250, 60, 50);

        Assert.Contains("Se perderam 50 amostras de sensor", texto);
        Assert.Contains("Se perderam 10 registros de uso", texto);
    }

    // ------------------------------------------------------------------ retrato com tamanho

    [Fact]
    public void RetratoComFalhaNaLeituraNaoInventaTamanhoZero()
    {
        UsarPastasTemporarias();

        GravarTrocasDeUso(2);

        // Um instante que o .NET não sabe converter (o ano 33658): a
        // montagem do retrato quebra no meio.
        Banco.Instancia!.Executar(
            "INSERT OR IGNORE INTO uso (momento, processo, titulo) VALUES (?, ?, ?)",
            999999999999999L, "chrome", "Painel");

        var retrato = Banco.Ver();

        // O retrato não diz que o arquivo tem 0 bytes: ele tem. Um "0 B" ao
        // lado de "não deu para contar as amostras" seriam dois números
        // falsos — e o arquivo existe, com o tamanho dele.
        Assert.NotEqual("ok", retrato.Estado);
        Assert.True(retrato.TamanhoBytes > 0,
            $"o tamanho do banco no disco não pode virar zero (deu {retrato.TamanhoBytes})");
        Assert.True(retrato.TamanhoBytes >= new FileInfo(Banco.Caminho).Length);
        Assert.Equal(-1, retrato.Amostras);
        Assert.Equal(-1, retrato.Uso);
    }

    // ------------------------------------------------------------------ ajudantes

    private static void GravarTrocasDeUso(int quantas)
    {
        for (int i = 0; i < quantas; i++)
        {
            Banco.GravarUso(new TrocaUso(
                DateTime.Today.AddHours(10).AddMinutes(i), Processo, "Painel"));
        }
    }

    /// <summary>
    /// Deixa o índice fora de sintonia com a tabela: troca, dentro da página
    /// do índice, o texto do processo por outro do mesmo tamanho. O tamanho
    /// da célula não muda, então a estrutura da árvore continua válida — só o
    /// conteúdo do índice deixa de corresponder a uma linha da tabela. É
    /// exatamente o estrago que a verificação rápida não enxergava.
    /// </summary>
    private static void DesalinharIndice(string caminho)
    {
        // O app segura o arquivo aberto: sem fechar, nem dá para ler os
        // bytes do banco (o Windows recusa). Fechar também recolhe o WAL no
        // arquivo principal — a corruption precisa ficar no arquivo, não no
        // WAL (que voltaria por cima dela na próxima abertura).
        Banco.Fechar();

        int pagina;

        using (var banco = Sqlite.Abrir(caminho))
        {
            Assert.NotNull(banco);

            // O WAL precisa estar recolhido no arquivo principal: uma página
            // que só existe no WAL voltaria por cima da corruption.
            banco!.Consultar("PRAGMA wal_checkpoint(TRUNCATE)");

            var linhas = banco.Consultar(
                "SELECT rootpage FROM sqlite_master WHERE name = 'sqlite_autoindex_uso_1'");
            Assert.Single(linhas);
            pagina = (int)linhas[0].Inteiro("rootpage");
        }

        byte[] arquivo = File.ReadAllBytes(caminho);
        int tamanhoPagina = (arquivo[16] << 8) | arquivo[17];
        int inicio = (pagina - 1) * tamanhoPagina;

        byte[] alvo = Encoding.UTF8.GetBytes(Processo);
        byte[] troca = Encoding.UTF8.GetBytes(ProcessoFora);

        int posicao = Procurar(arquivo, alvo, inicio, inicio + tamanhoPagina);
        Assert.True(posicao >= 0,
            "o texto do processo não apareceu dentro da página do índice");

        for (int i = 0; i < troca.Length; i++)
            arquivo[posicao + i] = troca[i];

        File.WriteAllBytes(caminho, arquivo);
    }

    private static int Procurar(byte[] arquivo, byte[] alvo, int de, int ate)
    {
        for (int i = de; i + alvo.Length <= ate && i + alvo.Length <= arquivo.Length; i++)
        {
            bool bate = true;
            for (int j = 0; j < alvo.Length; j++)
            {
                if (arquivo[i + j] != alvo[j]) { bate = false; break; }
            }

            if (bate) return i;
        }

        return -1;
    }
}
