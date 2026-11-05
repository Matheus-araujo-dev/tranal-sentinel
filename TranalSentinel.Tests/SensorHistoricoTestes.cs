using System.Globalization;
using System.IO;
using Xunit;

namespace TranalSentinel.Tests;

/// <summary>
/// Testes do log contínuo de sensores (o arquivo que diz o que o PC estava
/// fazendo antes de travar).
///
/// <para>
/// Os testes que gravam arquivo pointed o log para uma pasta temporária
/// (<see cref="SensorHistorico.PastaOverride"/>): nada aqui toca o
/// %LOCALAPPDATA% do usuário. Como essa pasta é estado global, a classe fica
/// na coleção "Sensores" — o xUnit executa as classes da mesma coleção em
/// série, e assim uma classe não apaga a pasta da outra.
/// </para>
/// </summary>
[Collection("Sensores")]
public class SensorHistoricoTestes : IDisposable
{
    private string? _pasta;

    /// <summary>
    /// Aponta o log para uma pasta temporária e devolve o caminho dela.
    /// </summary>
    private string UsarPastaTemporaria()
    {
        _pasta = Path.Combine(Path.GetTempPath(), "tranal-test-" + Guid.NewGuid());
        SensorHistorico.PastaOverride = _pasta;
        return _pasta;
    }

    public void Dispose()
    {
        SensorHistorico.PastaOverride = null;
        if (_pasta == null) return;
        try
        {
            if (Directory.Exists(_pasta)) Directory.Delete(_pasta, true);
        }
        catch
        {
            // A pasta é temporária: se o Windows ainda estiver segurando o
            // arquivo, ela some sozinha depois.
        }
    }

    private static AmostraSensor Amostra(DateTime momento) => new(
        momento,
        78.5f, 43f, 92.4f, 3200f,
        71f, 100f, 2010f, 2048f,
        87.4f, 12.5f, 0.3f);

    // ------------------------------------------------------------- linha (puro)

    [Fact]
    public void LinhaMontadaELida_VoltaAMesmaAmostra()
    {
        var original = Amostra(new DateTime(2026, 11, 5, 14, 31, 22, 500, DateTimeKind.Local));

        var lida = SensorHistorico.LerLinha(SensorHistorico.MontarLinha(original));

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

        var lida = SensorHistorico.LerLinha(SensorHistorico.MontarLinha(original));

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

        string linha = SensorHistorico.MontarLinha(original);

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

            string linha = SensorHistorico.MontarLinha(original);

            Assert.Contains(";78.5;", linha);
            Assert.DoesNotContain("78,5", linha);
            Assert.Equal(78.5f, SensorHistorico.LerLinha(linha)!.CpuTemp);
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
        Assert.Null(SensorHistorico.LerLinha(linha));
    }

    [Fact]
    public void LinhaComCampoIlegivel_VoltaComOCampoVazio()
    {
        // Um campo ruim não pode apagar o histórico inteiro.
        var lida = SensorHistorico.LerLinha("2026-11-05T14:31:22.500;95;abc;100;3200;70;90;2000;2048;80;10;5");

        Assert.NotNull(lida);
        Assert.Equal(95f, lida!.CpuTemp);
        Assert.Null(lida.CpuCarga);
        Assert.Equal(100f, lida.CpuConsumo);
    }

    // ------------------------------------------------------- arquivo (pasta temporária)

    [Fact]
    public void GravarELer_DevolveAsAmostrasGravadas()
    {
        UsarPastaTemporaria();

        var amostra = Amostra(new DateTime(2026, 11, 5, 14, 31, 22, 500, DateTimeKind.Local));
        SensorHistorico.Gravar(amostra);

        Assert.True(File.Exists(SensorHistorico.CaminhoArquivo));

        var lidas = SensorHistorico.Ler(DateTime.MinValue);

        var lida = Assert.Single(lidas);
        Assert.Equal(amostra.Momento, lida.Momento);
        Assert.Equal(78.5f, lida.CpuTemp);
        Assert.Equal(87.4f, lida.RamUso);
    }

    [Fact]
    public void Gravar_AcrescentaLinhasSemApagarAsAnteriores()
    {
        UsarPastaTemporaria();

        SensorHistorico.Gravar(Amostra(new DateTime(2026, 11, 5, 14, 31, 0, DateTimeKind.Local)));
        SensorHistorico.Gravar(Amostra(new DateTime(2026, 11, 5, 14, 31, 5, DateTimeKind.Local)));

        var lidas = SensorHistorico.Ler(DateTime.MinValue);

        Assert.Equal(2, lidas.Count);
        Assert.Equal(14, lidas[0].Momento.Hour);
        Assert.Equal(31, lidas[0].Momento.Minute);
        Assert.Equal(5, lidas[1].Momento.Second);
    }

    [Fact]
    public void Ler_ComDataDeCorte_IgnoraAsAmostrasMaisAntigas()
    {
        UsarPastaTemporaria();

        SensorHistorico.Gravar(Amostra(new DateTime(2026, 11, 1, 10, 0, 0, DateTimeKind.Local)));
        SensorHistorico.Gravar(Amostra(new DateTime(2026, 11, 5, 10, 0, 0, DateTimeKind.Local)));

        var lidas = SensorHistorico.Ler(new DateTime(2026, 11, 3, 0, 0, 0, DateTimeKind.Local));

        var lida = Assert.Single(lidas);
        Assert.Equal(new DateTime(2026, 11, 5, 10, 0, 0, DateTimeKind.Local), lida.Momento);
    }

    [Fact]
    public void Ler_SemArquivo_NaoErra()
    {
        UsarPastaTemporaria();

        Assert.Empty(SensorHistorico.Ler(DateTime.MinValue));
    }

    // ---------------------------------------------------------------------- podar

    [Fact]
    public void Podar_AcimaDoLimite_CortaOQueEAntigoEDeixaORecente()
    {
        UsarPastaTemporaria();

        // A regra é pelo tempo (os últimos SensorHistorico.DiasRetidos dias),
        // não pela metade: o que interessa é o que aconteceu antes de travar.
        // Linhas compridas para passar do limite sem escrever 32 MB de verdade.
        int total = 4000;
        var agora = DateTime.Now;
        var linhas = new List<string>(total);
        for (int i = 0; i < total; i++)
        {
            // Metade das linhas é de 60 dias atrás (fora da janela) e metade é
            // de agora (dentro).
            var momento = (i % 2 == 0 ? agora.AddDays(-60) : agora.AddMinutes(-i))
                .ToString("yyyy-MM-ddTHH:mm:ss.fff", CultureInfo.InvariantCulture);
            linhas.Add($"{momento};95;50;100;3200;70;90;2000;2048;80;10;{new string('9', 9000)}");
        }
        Directory.CreateDirectory(SensorHistorico.Pasta);
        File.WriteAllLines(SensorHistorico.CaminhoArquivo, linhas);
        Assert.True(new FileInfo(SensorHistorico.CaminhoArquivo).Length > SensorHistorico.LimiteBytes);

        SensorHistorico.Podar();

        var restantes = File.ReadAllLines(SensorHistorico.CaminhoArquivo);
        Assert.NotEmpty(restantes);
        Assert.True(restantes.Length < total);
        Assert.True(new FileInfo(SensorHistorico.CaminhoArquivo).Length < SensorHistorico.LimiteBytes);

        // Nenhuma linha mais antiga que a janela sobreviveu.
        string corte = agora.AddDays(-SensorHistorico.DiasRetidos).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        foreach (string linha in restantes)
            Assert.True(string.CompareOrdinal(linha, 0, corte, 0, 10) >= 0, $"linha antiga sobreviveu: {linha[..10]}");

        // A reescrita é por arquivo novo: não sobra temporário.
        Assert.False(File.Exists(SensorHistorico.CaminhoArquivo + ".tmp"));
    }

    [Fact]
    public void Podar_AbaixoDoLimite_NaoMexeNoArquivo()
    {
        UsarPastaTemporaria();

        SensorHistorico.Gravar(Amostra(new DateTime(2026, 11, 5, 10, 0, 0, DateTimeKind.Local)));
        string antes = File.ReadAllText(SensorHistorico.CaminhoArquivo);

        SensorHistorico.Podar();

        Assert.Equal(antes, File.ReadAllText(SensorHistorico.CaminhoArquivo));
    }
}
