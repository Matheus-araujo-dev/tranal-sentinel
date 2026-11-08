using System.Globalization;
using System.IO;
using Xunit;

namespace TranalSentinel.Tests;

/// <summary>
/// Testes do banco de dados do histórico (onde o TRANAL SENTINEL guarda o
/// que o PC estava fazendo e qual programa estava em frente).
///
/// <para>
/// Os testes apontam o banco para uma pasta temporária
/// (<see cref="Banco.PastaOverride"/>) e as pastas do CSV antigo
/// (<see cref="LegadoCsv.PastaSensoresOverride"/>) para outra: nada aqui
/// toca o %LOCALAPPDATA% nem o %APPDATA% do usuário. Como esse desvio é
/// estado global, a classe fica na coleção "Banco" — o xUnit executa as
/// classes da mesma coleção em série.
/// </para>
/// </summary>
[Collection("Banco")]
public class BancoTestes : IDisposable
{
    private string? _pastaBanco;
    private string? _pastaLegada;

    /// <summary>
    /// Aponta o banco (e as pastas do CSV antigo) para pastas temporárias
    /// só deste teste.
    /// </summary>
    private void UsarPastasTemporarias()
    {
        (_pastaBanco, _pastaLegada) = BancoDeTeste.Isolar();
    }

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

    private static DateTime Momento(int dia, int hora, int minuto, int segundo = 0) =>
        new(2026, 11, dia, hora, minuto, segundo, DateTimeKind.Local);

    private static AmostraSensor Amostra(DateTime momento, float? temp = 78.5f) => new(
        momento,
        temp, 43f, 92.4f, 3200f,
        71f, 100f, 2010f, 2048f,
        87.4f, 12.5f, 0.3f);

    [Fact]
    public void GravarAmostra_ComSensorInexistente_VoltaComValorVazio()
    {
        UsarPastasTemporarias();

        // Zero é um valor válido (a GPU pode estar a 0 °C de consumo): o que
        // não foi lido tem que voltar vazio, não zero.
        var original = new AmostraSensor(
            Momento(5, 14, 31, 22),
            78.5f, null, null, null,
            null, null, null, null,
            null, null, null);

        Assert.True(Banco.GravarAmostra(original));

        var lida = Assert.Single(Banco.LerAmostras(Momento(5, 0, 0), Momento(6, 0, 0)));

        Assert.Equal(78.5f, lida.CpuTemp);
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
    public void GravarAmostra_NoMesmoInstante_SubstituiEmVezDeDuplicar()
    {
        UsarPastasTemporarias();

        var momento = Momento(5, 14, 31, 22);
        Banco.GravarAmostra(Amostra(momento, 70f));
        Banco.GravarAmostra(Amostra(momento, 80f));

        var lidas = Banco.LerAmostras(momento.AddSeconds(-1), momento.AddSeconds(1));

        var lida = Assert.Single(lidas);
        Assert.Equal(80f, lida.CpuTemp);
    }

    [Fact]
    public void LerAmostras_ComJanela_SoDevolveODentroDaJanela()
    {
        UsarPastasTemporarias();

        Banco.GravarAmostra(Amostra(Momento(1, 10, 0)));
        Banco.GravarAmostra(Amostra(Momento(5, 10, 0)));
        Banco.GravarAmostra(Amostra(Momento(5, 12, 0)));
        Banco.GravarAmostra(Amostra(Momento(6, 10, 0)));

        var lidas = Banco.LerAmostras(Momento(5, 0, 0), Momento(5, 23, 59, 59));

        Assert.Equal(2, lidas.Count);
        Assert.Equal(Momento(5, 10, 0), lidas[0].Momento);
        Assert.Equal(Momento(5, 12, 0), lidas[1].Momento);
    }

    [Fact]
    public void LerAmostras_OrdenaPorInstante()
    {
        UsarPastasTemporarias();

        Banco.GravarAmostra(Amostra(Momento(5, 12, 0)));
        Banco.GravarAmostra(Amostra(Momento(5, 8, 0)));
        Banco.GravarAmostra(Amostra(Momento(5, 10, 0)));

        var lidas = Banco.LerAmostras(Momento(5, 0, 0), Momento(5, 23, 59));

        Assert.Equal(3, lidas.Count);
        Assert.Equal(Momento(5, 8, 0), lidas[0].Momento);
        Assert.Equal(Momento(5, 10, 0), lidas[1].Momento);
        Assert.Equal(Momento(5, 12, 0), lidas[2].Momento);
    }

    // ------------------------------------------------------------------ uso

    [Fact]
    public void GravarUsoELer_DevolveAsTrocasDoDiaEmOrdem()
    {
        UsarPastasTemporarias();

        Assert.True(Banco.GravarUso(new TrocaUso(Momento(5, 10, 0), "chrome", "Painel")));
        Assert.True(Banco.GravarUso(new TrocaUso(Momento(5, 10, 30), "code", "projeto.cs")));

        var lidas = Banco.LerUso(Momento(5, 10, 0));

        Assert.Equal(2, lidas.Count);
        Assert.Equal("chrome", lidas[0].Processo);
        Assert.Equal("code", lidas[1].Processo);
        Assert.Equal("projeto.cs", lidas[1].Titulo);
    }

    [Fact]
    public void GravarUso_ComAcentoETituloLongo_VoltaIgual()
    {
        UsarPastasTemporarias();

        var original = new TrocaUso(Momento(5, 10, 0), "navegador", "Ação ♥ — coração");

        Assert.True(Banco.GravarUso(original));

        var lida = Assert.Single(Banco.LerUso(Momento(5, 10, 0)));
        Assert.Equal("navegador", lida.Processo);
        Assert.Equal("Ação ♥ — coração", lida.Titulo);
    }

    [Fact]
    public void GravarUso_ComATituloComSeparador_NaoQuebraOLinha()
    {
        UsarPastasTemporarias();

        Assert.True(Banco.GravarUso(new TrocaUso(Momento(5, 10, 0), "code", "a;b\r\nc")));

        var lida = Assert.Single(Banco.LerUso(Momento(5, 10, 0)));
        Assert.DoesNotContain(';', lida.Titulo);
        Assert.DoesNotContain('\r', lida.Titulo);
        Assert.DoesNotContain('\n', lida.Titulo);
    }

    [Fact]
    public void GravarUso_AMesmaTrocaRepetida_NaoDuplica()
    {
        UsarPastasTemporarias();

        var troca = new TrocaUso(Momento(5, 10, 0), "chrome", "Painel");

        Assert.True(Banco.GravarUso(troca));
        Assert.False(Banco.GravarUso(troca));

        Assert.Single(Banco.LerUso(Momento(5, 10, 0)));
    }

    [Fact]
    public void LerUso_DeOutroDiaNaoDevolveAsTrocasDeHoje()
    {
        UsarPastasTemporarias();

        Banco.GravarUso(new TrocaUso(Momento(5, 10, 0), "chrome", "Painel"));
        Banco.GravarUso(new TrocaUso(Momento(6, 10, 0), "code", "Painel"));

        var lidas = Banco.LerUso(Momento(6, 10, 0));

        var lida = Assert.Single(lidas);
        Assert.Equal("code", lida.Processo);
    }

    [Fact]
    public void LerUsoComAnterior_TrazALastimaTrocaDoDiaAnterior()
    {
        UsarPastasTemporarias();

        // A troca das 23:50 de ontem atravessa a meia-noite: sem ela a noite
        // ficaria sem dono.
        Banco.GravarUso(new TrocaUso(Momento(4, 23, 50), "chrome", "Painel"));
        Banco.GravarUso(new TrocaUso(Momento(5, 9, 0), "code", "Painel"));

        var trocas = Banco.LerUsoComAnterior(Momento(5, 10, 0));

        Assert.Equal(2, trocas.Count);
        Assert.Equal("chrome", trocas[0].Processo);
        Assert.Equal(Momento(4, 23, 50), trocas[0].Momento);
    }

    [Fact]
    public void DiasComUso_ListaODiaQueTemHistorico()
    {
        UsarPastasTemporarias();

        Banco.GravarUso(new TrocaUso(Momento(5, 10, 0), "chrome", "Painel"));
        Banco.GravarUso(new TrocaUso(Momento(6, 10, 0), "code", "Painel"));

        var dias = Banco.DiasComUso();

        Assert.Equal(2, dias.Count);
        Assert.Equal(Momento(6, 10, 0).Date, dias[0]);   // do mais novo para o mais antigo
        Assert.Equal(Momento(5, 10, 0).Date, dias[1]);
    }

    // ------------------------------------------------------------------ poda

    [Fact]
    public void ApagarAntigos_ApagaOQueEAntigoEmAmbasAsTabelas()
    {
        UsarPastasTemporarias();

        Banco.GravarAmostra(Amostra(Momento(1, 10, 0)));
        Banco.GravarAmostra(Amostra(Momento(5, 10, 0)));
        Banco.GravarUso(new TrocaUso(Momento(1, 10, 0), "chrome", "Painel"));
        Banco.GravarUso(new TrocaUso(Momento(5, 10, 0), "chrome", "Painel"));

        int apagados = Banco.ApagarAntigos(Momento(3, 0, 0));

        Assert.Equal(2, apagados);
        Assert.Equal(1, Banco.LerAmostras(DateTime.MinValue, DateTime.MaxValue).Count);
        Assert.Single(Banco.LerUso(Momento(5, 10, 0)));
        Assert.Empty(Banco.LerUso(Momento(1, 10, 0)));
    }

    // ------------------------------------------------------------------ migração

    [Fact]
    public void Migracao_ImportaOCsvAntigoDeSensores()
    {
        UsarPastasTemporarias();

        // O log antigo: duas amostras em CSV, como o app gravava antes.
        Directory.CreateDirectory(_pastaLegada!);
        File.WriteAllLines(Path.Combine(_pastaLegada!, "historico.csv"), new[]
        {
            "2026-11-05T14:31:22.500;78.5;43;92.4;3200;71;100;2010;2048;87.4;12.5;0.3",
            "2026-11-05T14:31:27.500;79;44;93;3200;72;100;2010;2048;87.5;12.5;0.3"
        });

        var lidas = Banco.LerAmostras(Momento(5, 0, 0), Momento(6, 0, 0));

        Assert.Equal(2, lidas.Count);
        Assert.Equal(78.5f, lidas[0].CpuTemp);
        Assert.Equal(79f, lidas[1].CpuTemp);
    }

    [Fact]
    public void Migracao_ImportaOCsvAntigoDeUso()
    {
        UsarPastasTemporarias();

        Directory.CreateDirectory(_pastaLegada!);
        File.WriteAllLines(Path.Combine(_pastaLegada!, "uso-2026-11-05.csv"), new[]
        {
            "2026-11-05T10:00:00.000;chrome;Painel",
            "2026-11-05T10:30:00.000;code;projeto.cs"
        });

        var lidas = Banco.LerUso(Momento(5, 10, 0));

        Assert.Equal(2, lidas.Count);
        Assert.Equal("chrome", lidas[0].Processo);
        Assert.Equal("code", lidas[1].Processo);
    }

    [Fact]
    public void Migracao_NaoApagaOCsvAntigo()
    {
        UsarPastasTemporarias();

        string arquivo = Path.Combine(_pastaLegada!, "uso-2026-11-05.csv");
        Directory.CreateDirectory(_pastaLegada!);
        File.WriteAllLines(arquivo, new[] { "2026-11-05T10:00:00.000;chrome;Painel" });

        Banco.LerUso(Momento(5, 10, 0));

        // O CSV antigo fica como cópia do histórico anterior.
        Assert.True(File.Exists(arquivo));
    }

    [Fact]
    public void Migracao_RodandoDeNovoNaoDuplicaOHistorico()
    {
        UsarPastasTemporarias();

        Directory.CreateDirectory(_pastaLegada!);
        File.WriteAllLines(Path.Combine(_pastaLegada!, "uso-2026-11-05.csv"), new[]
        {
            "2026-11-05T10:00:00.000;chrome;Painel",
            "2026-11-05T10:30:00.000;code;projeto.cs"
        });

        Assert.Equal(2, Banco.LerUso(Momento(5, 10, 0)).Count);

        // O app reabre o banco (é o que acontece quando o TRANAL SENTINEL
        // é fechado e aberto de novo): a migração não roda de novo.
        Banco.Fechar();

        Assert.Equal(2, Banco.LerUso(Momento(5, 10, 0)).Count);
    }

    // ------------------------------------------------------------------ reparo

    [Fact]
    public void Reparar_ComBancoIntegro_NaoFazNada()
    {
        UsarPastasTemporarias();

        Banco.GravarAmostra(Amostra(Momento(5, 10, 0)));

        var resultado = Banco.Reparar();

        Assert.True(resultado.Sucesso);
        Assert.Equal(0, resultado.AmostrasRecuperadas);
        Assert.Null(resultado.Copia);
        Assert.Single(Banco.LerAmostras(Momento(5, 0, 0), Momento(6, 0, 0)));
    }

    [Fact]
    public void Reparar_ComArquivoEstragado_ReconstroiOGuardaACopia()
    {
        UsarPastasTemporarias();

        Banco.GravarAmostra(Amostra(Momento(5, 10, 0)));
        Banco.Fechar();

        // O arquivo do banco é estragado por fora: nem é um banco SQLite.
        File.WriteAllBytes(Banco.Caminho, LixoDeArquivo());

        var resultado = Banco.Reparar();

        Assert.True(resultado.Sucesso);
        Assert.NotNull(resultado.Copia);
        Assert.True(File.Exists(resultado.Copia!));

        // O banco novo funciona: a coleta volta a gravar.
        Assert.True(Banco.GravarAmostra(Amostra(Momento(5, 11, 0))));
        Assert.Single(Banco.LerAmostras(Momento(5, 0, 0), Momento(6, 0, 0)));
        Assert.Equal("ok", Banco.Estado);
    }

    [Fact]
    public void Reparar_ComArquivoEstragado_RelataQueNadaFoiSalvo()
    {
        UsarPastasTemporarias();

        Banco.GravarAmostra(Amostra(Momento(5, 10, 0)));
        Banco.Fechar();
        File.WriteAllBytes(Banco.Caminho, LixoDeArquivo());

        var resultado = Banco.Reparar();

        // Perder em silêncio é o que o app não faz: o relato diz que nada
        // pôde ser lido.
        Assert.Contains("Nada pôde ser lido", resultado.Relato);
        Assert.Equal(0, resultado.AmostrasRecuperadas);
    }

    [Fact]
    public void Reparar_ComBancoEstragadoMasLegivel_RecuperaAsLinhas()
    {
        UsarPastasTemporarias();

        Banco.GravarAmostra(Amostra(Momento(5, 10, 0)));
        Banco.GravarUso(new TrocaUso(Momento(5, 10, 0), "chrome", "Painel"));
        Banco.Fechar();

        // O arquivo continua íntegro: o reparo não tem o que fazer.
        var resultado = Banco.Reparar();

        Assert.True(resultado.Sucesso);
        Assert.Contains("íntegro", resultado.Relato);
        Assert.Single(Banco.LerAmostras(Momento(5, 0, 0), Momento(6, 0, 0)));
        Assert.Single(Banco.LerUso(Momento(5, 10, 0)));
    }

    [Fact]
    public void RestaurarCopia_DevolveOBancoQueEstavaGuardado()
    {
        UsarPastasTemporarias();

        Banco.GravarAmostra(Amostra(Momento(5, 10, 0)));
        Banco.Fechar();
        File.WriteAllBytes(Banco.Caminho, LixoDeArquivo());
        Banco.Reparar();

        // Depois do reparo há uma cópia do banco estragado.
        Assert.NotNull(Banco.CopiaAvariadaRecente());

        var resultado = Banco.RestaurarCopia();

        Assert.True(resultado.Sucesso);
        Assert.NotNull(Banco.CopiaAvariadaRecente());
    }

    [Fact]
    public void Reparar_SemBancoNaoErra()
    {
        UsarPastasTemporarias();

        var resultado = Banco.Reparar();

        Assert.False(resultado.Sucesso);
        Assert.Contains("Não há banco de dados", resultado.Relato);
    }

    // ------------------------------------------------------------------ indisponível

    [Fact]
    public void BancoIndisponivel_NaoErraENaoDevolveNumeroInventado()
    {
        Banco.PastaOverride = "Z:\\inexistente\\pasta\\que\\nao\\existe";
        Banco.Fechar();

        try
        {
            Assert.Null(Banco.Instancia);
            Assert.False(Banco.GravarAmostra(Amostra(Momento(5, 10, 0))));
            Assert.False(Banco.GravarUso(new TrocaUso(Momento(5, 10, 0), "chrome", "Painel")));
            Assert.Empty(Banco.LerAmostras(DateTime.MinValue, DateTime.MaxValue));
            Assert.Empty(Banco.LerUso(Momento(5, 10, 0)));
            Assert.Empty(Banco.DiasComUso());
            Assert.Equal(0, Banco.Contagem().Amostras);

            // A tela mostra o motivo, não um "ok" mentiroso.
            Assert.NotEqual("ok", Banco.Estado);
        }
        finally
        {
            BancoDeTeste.Restaurar();
        }
    }

    // ------------------------------------------------------------------ contagem e tamanho

    [Fact]
    public void ContagemETamanho_FalamDoQueExisteNoBanco()
    {
        UsarPastasTemporarias();

        Banco.GravarAmostra(Amostra(Momento(5, 10, 0)));
        Banco.GravarUso(new TrocaUso(Momento(5, 10, 0), "chrome", "Painel"));

        var (amostras, uso) = Banco.Contagem();

        Assert.Equal(1, amostras);
        Assert.Equal(1, uso);
        Assert.True(Banco.Tamanho > 0);
    }

    [Fact]
    public void Limpar_ApagaTudo()
    {
        UsarPastasTemporarias();

        Banco.GravarAmostra(Amostra(Momento(5, 10, 0)));
        Banco.GravarUso(new TrocaUso(Momento(5, 10, 0), "chrome", "Painel"));

        Assert.True(Banco.Limpar());

        Assert.Equal(0, Banco.Contagem().Amostras);
        Assert.Equal(0, Banco.Contagem().Uso);
    }

    /// <summary>
    /// Bytes que não são um banco SQLite: é assim que um arquivo estragado
    /// se parece.
    /// </summary>
    private static byte[] LixoDeArquivo()
    {
        var lixo = new byte[4096];
        for (int i = 0; i < lixo.Length; i++)
            lixo[i] = (byte)(i % 251);

        return lixo;
    }
}
