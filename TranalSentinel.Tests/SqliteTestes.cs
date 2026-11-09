using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace TranalSentinel.Tests;

/// <summary>
/// Testes do acesso ao SQLite do Windows (o wrapper por P/Invoke).
///
/// <para>
/// Cada teste abre o banco em uma pasta temporária e apaga no fim: nada
/// toca o banco de verdade do usuário.
/// </para>
/// </summary>
public class SqliteTestes : IDisposable
{
    private readonly string _pasta;

    public SqliteTestes()
    {
        _pasta = Path.Combine(Path.GetTempPath(), "tranal-test-sqlite-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_pasta);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_pasta)) Directory.Delete(_pasta, true); }
        catch { /* a pasta temporária vai embora sozinha */ }
    }

    private string Caminho => Path.Combine(_pasta, "banco.db");

    [Fact]
    public void AbreOBancoECriaOArquivo()
    {
        using var banco = Sqlite.Abrir(Caminho);

        Assert.NotNull(banco);
        Assert.True(File.Exists(Caminho));
    }

    [Fact]
    public void GravaELeOsValoresComOTipoCerto()
    {
        using var banco = Sqlite.Abrir(Caminho);
        Assert.NotNull(banco);

        Assert.True(banco.Executar("CREATE TABLE teste (texto TEXT, numero REAL, grande INTEGER, vazio TEXT)"));
        Assert.True(banco.Executar(
            "INSERT INTO teste (texto, numero, grande, vazio) VALUES (?, ?, ?, ?)",
            "acentuação: ção, é, ♥", 42.5, 9000000000L, null));

        var linhas = banco.Consultar("SELECT texto, numero, grande, vazio FROM teste");

        var linha = Assert.Single(linhas);
        Assert.Equal("acentuação: ção, é, ♥", linha.Texto("texto"));
        Assert.Equal(42.5, linha.Real("numero"), 3);
        Assert.Equal(9000000000L, linha.Inteiro("grande"));
        Assert.False(linha.TemValor("vazio"));
        Assert.Equal("", linha.Texto("vazio"));
    }

    [Fact]
    public void OTextoComAcentoVoltaExatamenteComoFoiGravado()
    {
        using var banco = Sqlite.Abrir(Caminho);
        Assert.NotNull(banco);

        banco.Executar("CREATE TABLE t (v TEXT)");
        banco.Executar("INSERT INTO t (v) VALUES (?)", "Português — ação, coração ♥");

        var linha = Assert.Single(banco.Consultar("SELECT v FROM t"));

        Assert.Equal("Português — ação, coração ♥", linha.Texto("v"));
    }

    [Fact]
    public void OInstanteVoltaNaHoraLocal()
    {
        using var banco = Sqlite.Abrir(Caminho);
        Assert.NotNull(banco);

        banco.Executar("CREATE TABLE t (momento INTEGER)");
        var instante = new DateTime(2026, 11, 6, 14, 31, 22, 500, DateTimeKind.Local);
        banco.Executar("INSERT INTO t (momento) VALUES (?)", instante);

        var linha = Assert.Single(banco.Consultar("SELECT momento FROM t"));

        Assert.Equal(instante, linha.Momento("momento"));
    }

    [Fact]
    public void TransacaoQueFalhaNaoDeixaNemUmaLinha()
    {
        using var banco = Sqlite.Abrir(Caminho);
        Assert.NotNull(banco);

        banco.Executar("CREATE TABLE t (v TEXT)");

        bool ok = banco.EmTransacao(b =>
        {
            b.Executar("INSERT INTO t (v) VALUES (?)", "primeira");
            throw new InvalidOperationException("falha no meio");
        });

        Assert.False(ok);
        Assert.Empty(banco.Consultar("SELECT v FROM t"));
    }

    [Fact]
    public void TransacaoQueTerminaGuardaTudo()
    {
        using var banco = Sqlite.Abrir(Caminho);
        Assert.NotNull(banco);

        banco.Executar("CREATE TABLE t (v TEXT)");

        bool ok = banco.EmTransacao(b =>
        {
            for (int i = 0; i < 50; i++)
                b.Executar("INSERT INTO t (v) VALUES (?)", "linha " + i);
        });

        Assert.True(ok);
        Assert.Equal(50, banco.Consultar("SELECT v FROM t").Count);
    }

    [Fact]
    public void BancoNovoEstaIntegro()
    {
        using var banco = Sqlite.Abrir(Caminho);
        Assert.NotNull(banco);

        Assert.Equal("ok", banco.VerificarIntegridade());
    }

    [Fact]
    public void ComandoQuebradoNaoDerrubaEFalhaComMensagem()
    {
        using var banco = Sqlite.Abrir(Caminho);
        Assert.NotNull(banco);

        Assert.False(banco.Executar("ISTO NAO E SQL"));

        Assert.NotEqual("", banco.UltimoErro);
    }

    [Fact]
    public void ConsultaQuebradaDevolveListaVazia()
    {
        using var banco = Sqlite.Abrir(Caminho);
        Assert.NotNull(banco);

        Assert.Empty(banco.Consultar("SELECT * DE TABELA QUE NAO EXISTE"));

        // O motivo fica registrado: é o que separa "a tabela está vazia"
        // de "a consulta falhou".
        Assert.NotEqual("", banco.UltimoErro);
    }

    [Fact]
    public void ConsultaQueDaCertoLimpaOErrorAntigo()
    {
        using var banco = Sqlite.Abrir(Caminho);
        Assert.NotNull(banco);

        banco.Executar("CREATE TABLE t (v TEXT)");

        // Um comando quebrado deixa uma mensagem de erro no SQLite.
        Assert.False(banco.Executar("ISTO NAO E SQL"));
        Assert.NotEqual("", banco.UltimoErro);

        // A consulta seguinte dá certo: o erro antigo não pode continuar
        // valendo, senão uma tabela vazia parece uma leitura falha.
        banco.Executar("INSERT INTO t (v) VALUES ('linha')");
        Assert.Single(banco.Consultar("SELECT v FROM t"));
        Assert.Equal("", banco.UltimoErro);
    }

    [Fact]
    public void ConsultaQueParaNoMeioNaoDevolveListaParcial()
    {
        // O arquivo do banco é estragado por fora: nem é um banco.
        File.WriteAllBytes(Caminho, Lixo());

        using var banco = Sqlite.Abrir(Caminho);
        Assert.NotNull(banco);

        // A leitura quebra no meio. A consulta não pode devolver as
        // linhas lidas até ali como se fossem o resultado inteiro — quem
        // lê precisa saber que o histórico veio pela metade.
        var linhas = banco.Consultar("SELECT v FROM t");

        Assert.Empty(linhas);
        Assert.NotEqual("", banco.UltimoErro);
    }

    [Fact]
    public void TransacaoQueFalhaDeixaOBancoProntoParaUsar()
    {
        using var banco = Sqlite.Abrir(Caminho);
        Assert.NotNull(banco);

        banco.Executar("CREATE TABLE t (v TEXT)");

        bool ok = banco.EmTransacao(b =>
        {
            b.Executar("INSERT INTO t (v) VALUES ('primeira')");
            throw new InvalidOperationException("falha no meio");
        });

        Assert.False(ok);

        // A transação foi desfeita: a escrita seguinte funciona (com a
        // transação aberta, ela falharia sem aviso).
        Assert.True(banco.Executar("INSERT INTO t (v) VALUES ('segunda')"));
        Assert.Single(banco.Consultar("SELECT v FROM t"));
    }

    [Fact]
    public void TransacaoDeLeituraVeOMesmoRetrato()
    {
        using var banco = Sqlite.Abrir(Caminho);
        Assert.NotNull(banco);

        banco.Executar("CREATE TABLE t (v TEXT)");
        banco.Executar("INSERT INTO t (v) VALUES ('linha')");

        long counted = 0;
        int lidas = 0;

        bool ok = banco.EmTransacaoLeitura(b =>
        {
            var contagem = b.Consultar("SELECT COUNT(*) AS total FROM t");
            counted = contagem[0].Inteiro(contagem[0].Colunas[0]);
            lidas = b.Consultar("SELECT v FROM t").Count;
        });

        Assert.True(ok);
        Assert.Equal(1, counted);
        Assert.Equal(1, lidas);
    }

    /// <summary>Bytes que não são um banco SQLite.</summary>
    private static byte[] Lixo()
    {
        var lixo = new byte[4096];
        for (int i = 0; i < lixo.Length; i++)
            lixo[i] = (byte)(i % 251);

        return lixo;
    }

    [Fact]
    public void BancoFechadoNaoErra()
    {
        var banco = Sqlite.Abrir(Caminho);
        Assert.NotNull(banco);
        banco.Dispose();

        Assert.False(banco.Executar("SELECT 1"));
        Assert.Empty(banco.Consultar("SELECT 1"));
        Assert.Equal("banco fechado", banco.UltimoErro);
    }

    [Fact]
    public void CaminhoImpossivelDevolveNull()
    {
        // Sem exceção: quem chama decide o que fazer quando não há banco.
        Assert.Null(Sqlite.Abrir("Z:\\inexistente\\pasta\\que\\nao\\existe\\banco.db"));
    }

    [Fact]
    public void InsercaoEmLoteComOsMesmosParametros()
    {
        using var banco = Sqlite.Abrir(Caminho);
        Assert.NotNull(banco);

        banco.Executar("CREATE TABLE t (a TEXT, b INTEGER)");

        var argumentos = new List<object?> { "x", 1 };
        for (int i = 0; i < 10; i++)
        {
            argumentos[0] = "linha " + i;
            argumentos[1] = i;
            Assert.True(banco.Executar("INSERT INTO t (a, b) VALUES (?, ?)", argumentos));
        }

        Assert.Equal(10, banco.Consultar("SELECT a, b FROM t").Count);
    }
}
