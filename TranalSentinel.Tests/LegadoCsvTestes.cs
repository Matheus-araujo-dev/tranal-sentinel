using System.Globalization;
using System.IO;
using Xunit;

namespace TranalSentinel.Tests;

/// <summary>
/// Testes do formato do histórico antigo em CSV — o que o TRANAL SENTINEL
/// gravava antes do banco de dados.
///
/// <para>
/// O CSV é lido uma vez só, quando o banco é criado pela primeira vez. As
/// pastas são apontadas para uma pasta temporária
/// (<see cref="LegadoCsv.PastaSensoresOverride"/>): nada aqui toca o
/// %LOCALAPPDATA% do usuário.
/// </para>
/// </summary>
[Collection("Banco")]
public class LegadoCsvTestes : IDisposable
{
    private string? _pastaSensores;
    private string? _pastaUso;

    private void UsarPastasTemporarias()
    {
        _pastaSensores = Path.Combine(Path.GetTempPath(), "tranal-legado-sens-" + Guid.NewGuid());
        _pastaUso = Path.Combine(Path.GetTempPath(), "tranal-legado-uso-" + Guid.NewGuid());

        LegadoCsv.PastaSensoresOverride = _pastaSensores;
        LegadoCsv.PastaUsoOverride = _pastaUso;
    }

    public void Dispose()
    {
        LegadoCsv.PastaSensoresOverride = null;
        LegadoCsv.PastaUsoOverride = null;

        foreach (string? pasta in new[] { _pastaSensores, _pastaUso })
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

    private static AmostraSensor Amostra(DateTime momento) => new(
        momento,
        78.5f, 43f, 92.4f, 3200f,
        71f, 100f, 2010f, 2048f,
        87.4f, 12.5f, 0.3f);

    // ------------------------------------------------------------- linha de sensor

    [Fact]
    public void LinhaMontadaELida_VoltaAMesmaAmostra()
    {
        var original = Amostra(new DateTime(2026, 11, 5, 14, 31, 22, 500, DateTimeKind.Local));

        var lida = LegadoCsv.LerAmostra(LegadoCsv.MontarLinhaAmostra(original));

        Assert.NotNull(lida);
        Assert.Equal(original.Momento, lida!.Momento);
        Assert.Equal(original.CpuTemp, lida.CpuTemp);
        Assert.Equal(original.CpuCarga, lida.CpuCarga);
        Assert.Equal(original.CpuConsumo, lida.CpuConsumo);
        Assert.Equal(original.CpuClock, lida.CpuClock);
        Assert.Equal(original.GpuTemp, lida.GpuTemp);
        Assert.Equal(original.GpuCarga, lida.GpuCarga);
        Assert.Equal(original.GpuClock, lida.GpuClock);
        Assert.Equal(original.GpuVram, lida.GpuVram);
        Assert.Equal(original.RamUso, lida.RamUso);
        Assert.Equal(original.DiscoLeitura, lida.DiscoLeitura);
        Assert.Equal(original.DiscoEscrita, lida.DiscoEscrita);
    }

    [Fact]
    public void LinhaMontadaELida_ComSensorInexistente_VoltaComValorVazio()
    {
        var original = new AmostraSensor(
            new DateTime(2026, 11, 5, 14, 31, 22, 500, DateTimeKind.Local),
            78.5f, null, null, null,
            null, null, null, null,
            null, null, null);

        var lida = LegadoCsv.LerAmostra(LegadoCsv.MontarLinhaAmostra(original));

        Assert.NotNull(lida);
        Assert.Equal(78.5f, lida!.CpuTemp);
        Assert.Null(lida.CpuCarga);
        Assert.Null(lida.CpuConsumo);
        Assert.Null(lida.CpuClock);
        Assert.Null(lida.GpuTemp);
        Assert.Null(lida.GpuCarga);
        Assert.Null(lida.GpuClock);
        Assert.Null(lida.GpuVram);
        Assert.Null(lida.RamUso);
        Assert.Null(lida.DiscoLeitura);
        Assert.Null(lida.DiscoEscrita);
    }

    [Fact]
    public void LinhaMontada_UsaDataComMilissegundosEpontoDecimal()
    {
        var original = Amostra(new DateTime(2026, 11, 5, 14, 31, 22, 500, DateTimeKind.Local));

        string linha = LegadoCsv.MontarLinhaAmostra(original);

        Assert.StartsWith("2026-11-05T14:31:22.500;", linha);
        Assert.Contains(";78.5;", linha);   // ponto decimal, como manda o CSV
        Assert.Equal(12, linha.Split(';').Length);  // timestamp + 11 campos
    }

    [Fact]
    public void LinhaMontada_NaoDependeDaCulturaDoWindows()
    {
        var original = Amostra(new DateTime(2026, 11, 5, 14, 31, 22, 500, DateTimeKind.Local));
        var cultura = CultureInfo.CurrentCulture;

        try
        {
            // Num PC português o número sairia com vírgula: o arquivo é
            // invariante, senão a leitura quebraria.
            CultureInfo.CurrentCulture = new CultureInfo("pt-BR");

            string linha = LegadoCsv.MontarLinhaAmostra(original);

            Assert.Contains(";78.5;", linha);
            Assert.DoesNotContain("78,5", linha);
            Assert.Equal(78.5f, LegadoCsv.LerAmostra(linha)!.CpuTemp);
        }
        finally
        {
            CultureInfo.CurrentCulture = cultura;
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("isto nao e uma linha")]
    [InlineData("2026-11-05T14:31:22.500;95")]
    [InlineData("2026-11-05 14:31:22;95;50;100;3200;70;90;2000;2048;80;10;5")]
    public void LinhaInvalida_DevolveNulo(string linha)
    {
        Assert.Null(LegadoCsv.LerAmostra(linha));
    }

    [Fact]
    public void LinhaComCampoIlegivel_VoltaComOCampoVazio()
    {
        // Um campo ruim não pode apagar o histórico inteiro.
        var lida = LegadoCsv.LerAmostra("2026-11-05T14:31:22.500;95;abc;100;3200;70;90;2000;2048;80;10;5");

        Assert.NotNull(lida);
        Assert.Equal(95f, lida!.CpuTemp);
        Assert.Null(lida.CpuCarga);
        Assert.Equal(100f, lida.CpuConsumo);
    }

    // ------------------------------------------------------------- linha de uso

    [Fact]
    public void LinhaDeUsoMontadaELida_VoltaAMesmaTroca()
    {
        var original = new TrocaUso(new DateTime(2026, 11, 6, 14, 31, 0, DateTimeKind.Local), "chrome", "Painel - TRANAL");

        var lida = LegadoCsv.LerUso(LegadoCsv.MontarLinhaUso(original));

        Assert.NotNull(lida);
        Assert.Equal(original.Momento, lida!.Momento);
        Assert.Equal(original.Processo, lida.Processo);
        Assert.Equal(original.Titulo, lida.Titulo);
    }

    [Fact]
    public void TituloComSeparadorEDeQuebraDeLinhaNaoQuebraOCsv()
    {
        var original = new TrocaUso(new DateTime(2026, 11, 6, 14, 31, 0, DateTimeKind.Local), "code", "a;b\r\nc");

        string linha = LegadoCsv.MontarLinhaUso(original);
        var lida = LegadoCsv.LerUso(linha);

        Assert.Equal(3, linha.Split(';').Length);
        Assert.NotNull(lida);

        // O que importa é que o título não quebra o CSV: nem separador, nem
        // quebra de linha sobram dentro do campo.
        Assert.DoesNotContain(';', lida!.Titulo);
        Assert.DoesNotContain('\r', lida.Titulo);
        Assert.DoesNotContain('\n', lida.Titulo);
        Assert.Contains("a b", lida.Titulo);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("isto nao e uma linha")]
    [InlineData("2026-11-06T14:31:22.500")]
    [InlineData("2026-11-06 14:31:22;chrome;Painel")]
    public void LinhaDeUsoInvalida_DevolveNulo(string linha)
    {
        Assert.Null(LegadoCsv.LerUso(linha));
    }

    // ------------------------------------------------------------- leitura dos arquivos

    [Fact]
    public void LerAmostras_SemArquivo_DevolveListaVazia()
    {
        UsarPastasTemporarias();

        Assert.Empty(LegadoCsv.LerAmostras());
    }

    [Fact]
    public void LerAmostras_ComArquivo_LeTodasAsLinhasValidas()
    {
        UsarPastasTemporarias();

        Directory.CreateDirectory(_pastaSensores!);
        File.WriteAllLines(Path.Combine(_pastaSensores!, "historico.csv"), new[]
        {
            "2026-11-05T14:31:22.500;78.5;43;92.4;3200;71;100;2010;2048;87.4;12.5;0.3",
            "linha quebrada",
            "2026-11-05T14:31:27.500;79;44;93;3200;72;100;2010;2048;87.5;12.5;0.3"
        });

        var amostras = LegadoCsv.LerAmostras();

        Assert.Equal(2, amostras.Count);
        Assert.Equal(78.5f, amostras[0].CpuTemp);
        Assert.Equal(79f, amostras[1].CpuTemp);
    }

    [Fact]
    public void LerTrocas_LêTodosOsArquivosDeUso()
    {
        UsarPastasTemporarias();

        Directory.CreateDirectory(_pastaUso!);
        File.WriteAllLines(LegadoCsv.ArquivoDeUso(new DateTime(2026, 11, 5)), new[]
        {
            "2026-11-05T10:00:00.000;chrome;Painel"
        });
        File.WriteAllLines(LegadoCsv.ArquivoDeUso(new DateTime(2026, 11, 6)), new[]
        {
            "2026-11-06T10:00:00.000;code;projeto.cs"
        });

        var trocas = LegadoCsv.LerTrocas();

        Assert.Equal(2, trocas.Count);
    }

    [Fact]
    public void ArquivoDeUso_UsaDataInVariante()
    {
        UsarPastasTemporarias();

        string arquivo = LegadoCsv.ArquivoDeUso(new DateTime(2026, 11, 6));

        Assert.EndsWith("uso-2026-11-06.csv", arquivo);
    }

    // ------------------------------------------------------------- prazo de leitura

    [Fact]
    public void LerAmostras_ComPrazo_IgnoraOQueEstaFora()
    {
        UsarPastasTemporarias();

        Directory.CreateDirectory(_pastaSensores!);
        File.WriteAllLines(Path.Combine(_pastaSensores!, "historico.csv"), new[]
        {
            "2026-11-05T10:00:00.000;78.5;43;92.4;3200;71;100;2010;2048;87.4;12.5;0.3",
            "2026-11-04T10:00:00.000;79;44;93;3200;72;100;2010;2048;87.5;12.5;0.3"
        });

        // O prazo de 15 dias da tabela: o que está fora não entra no
        // banco (importar anos de histórico só encheria o banco com o
        // que seria podado em seguida).
        var amostras = LegadoCsv.LerAmostras(new DateTime(2026, 11, 5));

        var amostra = Assert.Single(amostras);
        Assert.Equal(78.5f, amostra.CpuTemp);
    }

    [Fact]
    public void LerTrocas_ComPrazo_IgnoraArquivoDeDiaAntigo()
    {
        UsarPastasTemporarias();

        Directory.CreateDirectory(_pastaUso!);
        File.WriteAllLines(LegadoCsv.ArquivoDeUso(new DateTime(2026, 11, 6)), new[]
        {
            "2026-11-06T10:00:00.000;chrome;Painel"
        });
        File.WriteAllLines(LegadoCsv.ArquivoDeUso(new DateTime(2026, 9, 1)), new[]
        {
            "2026-09-01T10:00:00.000;code;antigo.cs"
        });

        // O arquivo de um dia fora do prazo é pulado inteiro.
        var trocas = LegadoCsv.LerTrocas(new DateTime(2026, 11, 1));

        var troca = Assert.Single(trocas);
        Assert.Equal("chrome", troca.Processo);
    }
}
