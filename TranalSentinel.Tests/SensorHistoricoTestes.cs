using System.IO;
using Xunit;

namespace TranalSentinel.Tests;

/// <summary>
/// Testes do histórico contínuo de sensores (o que o PC estava fazendo antes
/// de travar).
///
/// <para>
/// O histórico mora num banco SQLite (<see cref="Banco"/>). Os testes
/// apontam o banco para uma pasta temporária
/// (<see cref="Banco.PastaOverride"/>): nada aqui toca o %LOCALAPPDATA% do
/// usuário. Como essa pasta é estado global, a classe fica na coleção
/// "Banco" — o xUnit executa as classes da mesma coleção em série, e assim
/// uma classe não apaga a pasta da outra.
/// </para>
/// </summary>
[Collection("Banco")]
public class SensorHistoricoTestes : IDisposable
{
    private string? _pasta;
    private string? _pastaLegada;

    /// <summary>
    /// Aponta o banco para uma pasta temporária e devolve o caminho dela.
    /// </summary>
    private string UsarPastaTemporaria()
    {
        (_pasta, _pastaLegada) = BancoDeTeste.Isolar();
        return _pasta;
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

    private static AmostraSensor Amostra(DateTime momento) => new(
        momento,
        78.5f, 43f, 92.4f, 3200f,
        71f, 100f, 2010f, 2048f,
        87.4f, 12.5f, 0.3f);

    [Fact]
    public void GravarELer_DevolveAsAmostrasGravadas()
    {
        UsarPastaTemporaria();

        var amostra = Amostra(new DateTime(2026, 11, 5, 14, 31, 22, 500, DateTimeKind.Local));
        SensorHistorico.Gravar(amostra);

        var lidas = SensorHistorico.Ler(DateTime.MinValue).Linhas;

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

        var lidas = SensorHistorico.Ler(DateTime.MinValue).Linhas;

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

        var lidas = SensorHistorico.Ler(new DateTime(2026, 11, 3, 0, 0, 0, DateTimeKind.Local)).Linhas;

        var lida = Assert.Single(lidas);
        Assert.Equal(new DateTime(2026, 11, 5, 10, 0, 0, DateTimeKind.Local), lida.Momento);
    }

    [Fact]
    public void Ler_ComInicioEFim_SoDevolveODentroDaJanela()
    {
        // A leitura limitada é o que permite abrir a aba "Uso" sem varrer o
        // histórico inteiro (que chega a dezenas de megabytes).
        UsarPastaTemporaria();

        SensorHistorico.Gravar(Amostra(new DateTime(2026, 11, 1, 10, 0, 0, DateTimeKind.Local)));
        SensorHistorico.Gravar(Amostra(new DateTime(2026, 11, 5, 10, 0, 0, DateTimeKind.Local)));
        SensorHistorico.Gravar(Amostra(new DateTime(2026, 11, 5, 12, 0, 0, DateTimeKind.Local)));
        SensorHistorico.Gravar(Amostra(new DateTime(2026, 11, 6, 10, 0, 0, DateTimeKind.Local)));

        var lidas = SensorHistorico.Ler(
            new DateTime(2026, 11, 5, 0, 0, 0, DateTimeKind.Local),
            new DateTime(2026, 11, 5, 23, 59, 59, DateTimeKind.Local)).Linhas;

        Assert.Equal(2, lidas.Count);
        Assert.Equal(new DateTime(2026, 11, 5, 10, 0, 0, DateTimeKind.Local), lidas[0].Momento);
        Assert.Equal(new DateTime(2026, 11, 5, 12, 0, 0, DateTimeKind.Local), lidas[1].Momento);
    }

    [Fact]
    public void Ler_SemBanco_NaoErra()
    {
        UsarPastaTemporaria();

        Assert.Empty(SensorHistorico.Ler(DateTime.MinValue).Linhas);
    }

    [Fact]
    public void Gravar_ComBancoIndisponivel_NaoErra()
    {
        Banco.PastaOverride = "Z:\\inexistente\\pasta\\que\\nao\\existe";
        Banco.Fechar();

        try
        {
            // O log é um extra: falhar nele não pode derrubar o app.
            SensorHistorico.Gravar(Amostra(new DateTime(2026, 11, 5, 10, 0, 0, DateTimeKind.Local)));
            Assert.Empty(SensorHistorico.Ler(DateTime.MinValue).Linhas);
        }
        finally
        {
            BancoDeTeste.Restaurar();
        }
    }

    // ---------------------------------------------------------------------- podar

    [Fact]
    public void Podar_CortaOQueEAntigoEDeixaORecente()
    {
        UsarPastaTemporaria();

        // A regra é pelo tempo (os últimos SensorHistorico.DiasRetidos dias),
        // não pela metade: o que interessa é o que aconteceu antes de travar.
        var agora = DateTime.Now;

        SensorHistorico.Gravar(Amostra(agora.AddDays(-60)));
        SensorHistorico.Gravar(Amostra(agora.AddMinutes(-5)));

        SensorHistorico.Podar();

        var lidas = SensorHistorico.Ler(DateTime.MinValue).Linhas;

        var lida = Assert.Single(lidas);
        Assert.True(lida.Momento > agora.AddDays(-SensorHistorico.DiasRetidos));
    }
}
