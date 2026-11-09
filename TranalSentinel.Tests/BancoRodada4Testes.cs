using System.IO;
using Xunit;

namespace TranalSentinel.Tests;

/// <summary>
/// Testes das correções da quarta revisão adversarial do banco de dados.
///
/// <para>
/// Cada teste prende uma falha que a revisão encontrou: o reparo cancelado
/// que deixava o banco novo órfão na pasta (o handle do SQLite estava
/// aberto, e o Windows não deixa apagar assim), o relato que escondia a
/// perda de uma das tabelas quando a contagem da outra falhava, o retrato
/// que inventava zero, a leitura que não distinguia "não tinha" de "não
/// deu para ler", a cópia que apontava para um arquivo inexistente e a
/// sonda da instância única que criava a trava do app.
/// </para>
///
/// <para>
/// A classe fica na coleção "Banco": o banco, as pastas do CSV antigo e a
/// trava de instância única são estado global, e o xUnit roda as classes
/// da mesma coleção em série.
/// </para>
/// </summary>
[Collection("Banco")]
public class BancoRodada4Testes : IDisposable
{
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

    private static AmostraSensor Amostra(DateTime momento) => new(
        momento,
        78.5f, 43f, 92.4f, 3200f,
        71f, 100f, 2010f, 2048f,
        87.4f, 12.5f, 0.3f);

    // ------------------------------------------------------------------ órfão do reparo cancelado

    [Fact]
    public void ReparoCanceladoPorGravacaoQueFalha_NaoDeixaOBancoNovoOrfao()
    {
        UsarPastasTemporarias();

        Directory.CreateDirectory(Banco.Pasta);

        // Um banco com uma amostra de instante textual (o esquema novo
        // exige a chave inteira). A gravação no banco novo falha, o reparo
        // cancela — que é exatamente o caminho que deixava o banco novo
        // órfão (a limpeza acontecia com o handle do SQLite ainda aberto,
        // e o Windows não deixa apagar um arquivo aberto).
        CriarBancoComAmostraInvalida();

        var resultado = Banco.Reparar();

        Assert.False(resultado.Sucesso, resultado.Relato);
        Assert.Contains("cancelado", resultado.Relato, StringComparison.OrdinalIgnoreCase);

        // O banco novo não pode ficar na pasta: ele ocuparia dezenas de
        // megabytes sem servir para nada.
        Assert.False(File.Exists(Banco.Caminho + ".reparo"), resultado.Relato);
        Assert.False(File.Exists(Banco.Caminho + ".reparo-wal"), resultado.Relato);

        // E o banco velho continua no lugar (é melhor o banco estragado no
        // lugar do que nenhum).
        Assert.True(File.Exists(Banco.Caminho));
    }

    /// <summary>
    /// Cria um banco de verdade cuja amostra tem o instante como texto (o
    /// esquema novo exige a chave inteira), com uma página corrompida
    /// (para o reparo ter o que reconstruir).
    ///
    /// <para>
    /// A linha com o instante textual é gravada DEPOIS da corrupção: assim
    /// ela não é apagada junto com a página estragada, e a gravação no
    /// banco novo falha de verdade (o SQLite recusa um texto na chave
    /// inteira da amostra).
    /// </para>
    /// </summary>
    private static void CriarBancoComAmostraInvalida()
    {
        using (var banco = Sqlite.Abrir(Banco.Caminho))
        {
            Assert.NotNull(banco);

            // O instante como texto: o banco velho aceita, o novo (que
            // exige a chave inteira) recusa.
            banco!.Executar(@"
                CREATE TABLE amostras (
                    momento TEXT, cpu_temp REAL, cpu_carga REAL, cpu_consumo REAL, cpu_clock REAL,
                    gpu_temp REAL, gpu_carga REAL, gpu_clock REAL, gpu_vram REAL, ram_uso REAL,
                    disco_leitura REAL, disco_escrita REAL)");

            banco.Executar(@"
                CREATE TABLE uso (
                    momento INTEGER NOT NULL, processo TEXT NOT NULL, titulo TEXT NOT NULL)");

            banco.Executar(
                "INSERT INTO amostras (momento, cpu_temp) VALUES (?, ?)",
                "1700000000000", 78.5);

            banco.Executar(
                "INSERT INTO uso (momento, processo, titulo) VALUES (?, ?, ?)",
                1700000000000L, "chrome", "Painel");
        }

        // Uma página corrompida: a verificação reprova, mas as linhas
        // continuam legíveis.
        BancoCorrupcao.CorromperPaginaLegivel(Banco.Caminho, 1, 1);

        // A amostra que não cabe no banco novo — gravada depois da
        // corrupção, para não ser apagada junto com a página estragada.
        using (var banco = Sqlite.Abrir(Banco.Caminho))
        {
            Assert.NotNull(banco);

            banco!.Executar(
                "INSERT INTO amostras (momento, cpu_temp) VALUES (?, ?)",
                "nao-e-um-inteiro", 79.5);
        }

        // A amostra tem que estar lá: sem ela, o reparo não encontraria
        // nada que não coubesse no banco novo.
        using (var banco = Sqlite.Abrir(Banco.Caminho))
        {
            Assert.NotNull(banco);

            var linhas = banco!.Consultar("SELECT momento FROM amostras");
            Assert.Equal(2, linhas.Count);
        }
    }

    // ------------------------------------------------------------------ relato da perda

    [Fact]
    public void RelatoDaPerda_ComAContagemDeUmaDasTabelasFalha_ContaAPerdaDaOutra()
    {
        // A contagem das amostras não deu (-1), a do uso deu: o relato
        // precisa dizer as duas coisas. Uma contagem que falhou não pode
        // apagar a conta da outra tabela.
        string texto = Banco.RelatoDaPerda(-1, 250, 60, 50);

        Assert.Contains("Não deu para contar as amostras", texto);
        Assert.Contains("Se perderam 10 registros de uso", texto);
    }

    [Fact]
    public void RelatoDaPerda_ComAContagemDoUsoFalha_ContaAPerdaDasAmostras()
    {
        string texto = Banco.RelatoDaPerda(300, 250, -1, 0);

        Assert.Contains("Se perderam 50 amostras de sensor", texto);
        Assert.Contains("Não deu para contar os registros de uso", texto);
    }

    [Fact]
    public void RelatoDaPerda_SemPerdaNaoDizQueSePerdeu()
    {
        string texto = Banco.RelatoDaPerda(300, 300, 60, 60);

        Assert.DoesNotContain("Se perderam", texto);
        Assert.DoesNotContain("Não deu para contar", texto);
    }

    // ------------------------------------------------------------------ zero inventado e leitura pela metade

    [Fact]
    public void Ver_ComInstanteForaDaFaixa_NaoDizQueTemZeroAmostras()
    {
        UsarPastasTemporarias();

        // Um instante que o .NET não sabe converter (o ano 33658): a
        // leitura do histórico quebra no meio. O retrato não pode
        // responder "0 amostras" — o banco pode ter milhares.
        Banco.Instancia!.Executar(
            "INSERT OR IGNORE INTO uso (momento, processo, titulo) VALUES (?, ?, ?)",
            999999999999999L, "chrome", "Painel");

        var retrato = Banco.Ver();

        Assert.Equal(-1, retrato.Amostras);
        Assert.Equal(-1, retrato.Uso);
    }

    [Fact]
    public void LerUso_ComConsultaQueFalha_DizQueNaoDeuParaLer()
    {
        UsarPastasTemporarias();

        // Um banco de uso sem a coluna "titulo": a consulta do histórico
        // de uso falha ("no such column"), e a leitura precisa dizer
        // isso — em vez de mostrar um dia vazio como se o PC não tivesse
        // sido usado.
        Directory.CreateDirectory(Banco.Pasta);

        using (var banco = Sqlite.Abrir(Banco.Caminho))
        {
            Assert.NotNull(banco);

            banco!.Executar("CREATE TABLE uso (momento INTEGER NOT NULL, processo TEXT NOT NULL)");
        }

        // A leitura pela camada que a tela usa: lista vazia, e o sinal de
        // que a leitura NÃO ficou completa.
        var leitura = UsoHistorico.Ler(DateTime.Today);

        Assert.Empty(leitura.Linhas);
        Assert.False(leitura.Completa);
    }

    [Fact]
    public void LerUso_ComABancoQueNaoAbre_DizQueNaoDeuParaLer()
    {
        Banco.PastaOverride = "Z:\\inexistente\\pasta\\que\\nao\\existe";
        Banco.Fechar();

        try
        {
            var leitura = UsoHistorico.Ler(DateTime.Today);

            // Sem banco não há leitura: a tela precisa saber que não é um
            // dia sem uso.
            Assert.Empty(leitura.Linhas);
            Assert.False(leitura.Completa);
        }
        finally
        {
            BancoDeTeste.Restaurar();
        }
    }

    [Fact]
    public void LerUso_ComBancoVazio_DizQueALeituraFicouCompleta()
    {
        UsarPastasTemporarias();

        // Banco vazio é "não teve uso nesse dia" — e não "não deu para
        // ler". As duas coisas não podem virar a mesma frase.
        var leitura = UsoHistorico.Ler(DateTime.Today);

        Assert.Empty(leitura.Linhas);
        Assert.True(leitura.Completa);
    }

    // ------------------------------------------------------------------ cópia que não existe

    [Fact]
    public void ReparoComOArquivoTrancado_NaoApontaParaUmaCopiaInexistente()
    {
        UsarPastasTemporarias();

        for (int i = 0; i < 300; i++)
            Banco.GravarAmostra(Amostra(DateTime.Today.AddMinutes(i)));

        Banco.GravarUso(new TrocaUso(DateTime.Today.AddHours(10), "chrome", "Painel"));
        Banco.Fechar();

        BancoCorrupcao.CorromperPaginaLegivel(Banco.Caminho, 300, 1);

        // O app aberto segura o arquivo: a troca falha.
        using (var segura = Sqlite.Abrir(Banco.Caminho))
        {
            Assert.NotNull(segura);

            var resultado = Banco.Reparar();

            Assert.False(resultado.Sucesso);

            // A troca não aconteceu: o banco velho tem que continuar no
            // lugar, e não pode haver cópia apontada (nada foi movido, e
            // um caminho que aponta para um arquivo que não existe não
            // pode ser oferecido como desfazer).
            //
            // A afirmação não é condicional: um "if" que não entra no
            // caminho deixa a correção sem prova nenhuma.
            Assert.True(File.Exists(Banco.Caminho), "o banco antigo tem que continuar no lugar");
            Assert.Null(resultado.Copia);

            // E o banco novo do reparo não pode ficar órfão na pasta.
            Assert.False(File.Exists(Banco.Caminho + ".reparo"));
        }
    }

    // ------------------------------------------------------------------ trava de instância única

    [Fact]
    public void EstaRodando_NaoCriaATravaDoApp()
    {
        // A sonda só olha: se ela criasse a trava, o app que subisse na
        // mesma hora acharia que outra instância estava rodando — e não
        // subiria, sem mensagem.
        Assert.False(InstanciaUnica.EstaRodando());

        using Mutex? trava = InstanciaUnica.Segurar();

        Assert.NotNull(trava);
    }

    [Fact]
    public void EstaRodando_ComATravaSegurada_DizQueSim()
    {
        using var trava = new Mutex(true, InstanciaUnica.Nome, out bool criado);

        Assert.True(criado);
        Assert.True(InstanciaUnica.EstaRodando());
    }
}
