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
}
