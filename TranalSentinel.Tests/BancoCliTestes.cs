using System.Globalization;
using System.IO;
using Xunit;

namespace TranalSentinel.Tests;

/// <summary>
/// Testes da CLI do banco de dados do histórico.
///
/// <para>
/// A CLI não abre janela: ela mostra o mesmo retrato que a aba TRANAL
/// SAÚDE mostra. O ponto dos testes é o texto — é ele que o usuário lê
/// quando o banco não está bem.
/// </para>
/// </summary>
[Collection("Banco")]
public class BancoCliTestes : IDisposable
{
    private string? _pasta;
    private string? _pastaLegada;

    private void UsarPastasTemporarias()
    {
        (_pasta, _pastaLegada) = BancoDeTeste.Isolar();
    }

    public void Dispose()
    {
        BancoDeTeste.Restaurar();
        RegistroDiario.PastaOverride = null;

        foreach (string? pasta in new[] { _pasta, _pastaLegada })
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

    [Fact]
    public void RelatorioMostraOEstadoEOQueTemNoBanco()
    {
        var dados = new Banco.Diagnostico("ok", 2048, 120, 7, @"C:\banco\historico.db", null);

        string texto = BancoCli.MontarRelatorio(dados);

        Assert.Contains("íntegro", texto);
        Assert.Contains(@"C:\banco\historico.db", texto);
        Assert.Contains("2 KB", texto);
        Assert.Contains("Amostras de sensor: 120", texto);
        Assert.Contains("Registros de uso: 7", texto);
    }

    [Fact]
    public void RelatorioMostraOCaminhoDaCopiaAvariada()
    {
        var dados = new Banco.Diagnostico("ok", 0, 0, 0, @"C:\banco\historico.db", @"C:\banco\historico.db.avariado-20261108-101500");

        string texto = BancoCli.MontarRelatorio(dados);

        Assert.Contains("Cópia avariada guardada", texto);
        Assert.Contains("avariado-20261108-101500", texto);
    }

    [Fact]
    public void RelatorioNaoEsqueceOEstadoRuim()
    {
        // Um banco que precisa de reparo tem que aparecer como tal: um
        // "íntegro" mentiroso é o pior jeito de errar.
        var dados = new Banco.Diagnostico("O banco de dados precisa de reparo: file is not a database", 0, 0, 0, @"C:\banco\historico.db", null);

        string texto = BancoCli.MontarRelatorio(dados);

        Assert.Contains("precisa de reparo", texto);
        Assert.DoesNotContain("Estado: íntegro", texto);
    }

    [Fact]
    public void RelatorioNaoDependeDaCulturaDoWindows()
    {
        var cultura = CultureInfo.CurrentCulture;

        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("pt-BR");

            var dados = new Banco.Diagnostico("ok", 1024 * 1024 * 3, 1, 1, @"C:\banco\historico.db", null);

            string texto = BancoCli.MontarRelatorio(dados);

            Assert.Contains("3 MB", texto);
            Assert.DoesNotContain("3,0", texto);
        }
        finally
        {
            CultureInfo.CurrentCulture = cultura;
        }
    }

    [Fact]
    public void RelatorioNaoInventaZeroQuandoACountagemNaoDeu()
    {
        // -1 = a contagem não deu para fazer (o banco está estragado).
        // "0 amostras" seria mentira.
        var dados = new Banco.Diagnostico("O banco de dados precisa de reparo", 2048, -1, -1, @"C:\banco\historico.db", null);

        string texto = BancoCli.MontarRelatorio(dados);

        Assert.Contains("não deu para contar", texto);
        Assert.DoesNotContain("Amostras de sensor: 0", texto);
        Assert.DoesNotContain("Registros de uso: 0", texto);
    }

    // ------------------------------------------------------------------ reparo pela linha de comando

    [Fact]
    public void RepararSemConfirmarNaoMexeNoBanco()
    {
        UsarPastasTemporarias();

        Directory.CreateDirectory(_pasta!);
        string banco = Path.Combine(_pasta!, "historico.db");
        File.WriteAllBytes(banco, LixoDeArquivo());

        int codigo = ExecutarLinhaDeComando("--banco", "--reparar", "--pasta", _pasta!);

        // Sem --confirmar o reparo não acontece: trocar o arquivo do
        // histórico é decisão do usuário.
        Assert.Equal(BancoCli.CodigoErro, codigo);
        Assert.Equal(LixoDeArquivo().Length, new FileInfo(banco).Length);
        Assert.Empty(Directory.GetFiles(_pasta!, "historico.db.avariado-*"));
    }

    [Fact]
    public void RepararComConfirmarFicaRegistradoNoDiario()
    {
        UsarPastasTemporarias();

        string pastaDiario = Path.Combine(_pasta!, "diario");
        RegistroDiario.PastaOverride = pastaDiario;

        Directory.CreateDirectory(_pasta!);
        File.WriteAllBytes(Path.Combine(_pasta!, "historico.db"), LixoDeArquivo());

        int codigo = ExecutarLinhaDeComando("--banco", "--reparar", "--confirmar", "--pasta", _pasta!);

        Assert.Equal(BancoCli.CodigoOk, codigo);

        // A ação que mexeu no arquivo do histórico fica no diário do app
        // — é o mesmo registro que a tela faz.
        string diario = "";
        if (Directory.Exists(pastaDiario))
        {
            foreach (string arquivo in Directory.GetFiles(pastaDiario))
                diario += File.ReadAllText(arquivo);
        }

        Assert.Contains("banco: reparo", diario);
    }

    /// <summary>
    /// Roda a CLI sem sujar a saída do teste (e sem mexer no console de
    /// verdade).
    /// </summary>
    private static int ExecutarLinhaDeComando(params string[] argumentos)
    {
        var original = Console.Out;
        try
        {
            Console.SetOut(new StringWriter());
            return BancoCli.Executar(argumentos);
        }
        finally
        {
            Console.SetOut(original);
        }
    }

    /// <summary>Bytes que não são um banco SQLite.</summary>
    private static byte[] LixoDeArquivo()
    {
        var lixo = new byte[4096];
        for (int i = 0; i < lixo.Length; i++)
            lixo[i] = (byte)(i % 251);

        return lixo;
    }
}
