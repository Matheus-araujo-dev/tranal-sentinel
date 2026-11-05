using System;
using System.Collections.Generic;
using System.Linq;
using TranalSentinel;
using Xunit;

namespace TranalSentinel.Tests;

/// <summary>
/// Testes da linha do tempo do PC (comparação de retratos) e do perfil de recursos.
/// </summary>
public class MudancasTestes
{
    private static InstantaneoPC Retrato(
        string windows = "11",
        string bios = "4641",
        string video = "AMD · 32.0.1",
        string rede = "Realtek · 1.0",
        string energia = "Alto desempenho",
        string memoria = "32 GB",
        string config = "Modo de jogo: ligado",
        List<string>? programas = null,
        List<string>? inicializacao = null,
        int dia = 1)
    {
        return new InstantaneoPC
        {
            Quando = new DateTime(2026, 11, dia, 10, 0, 0),
            Windows = windows,
            Bios = bios,
            DriverVideo = video,
            DriverRede = rede,
            PlanoEnergia = energia,
            Memoria = memoria,
            Configuracoes = config,
            Programas = programas ?? new List<string>(),
            Inicializacao = inicializacao ?? new List<string>(),
        };
    }

    // ---------------------------------------------------------------- comparação

    [Fact]
    public void SemRetratoAnteriorNaoHaMudanca()
    {
        var mudancas = Mudancas.Comparar(null, Retrato());
        Assert.Empty(mudancas);
    }

    [Fact]
    public void RetratosIguaisNaoGeramMudanca()
    {
        var antes = Retrato(programas: new List<string> { "Steam · 2.0" });
        var depois = Retrato(programas: new List<string> { "Steam · 2.0" });
        Assert.Empty(Mudancas.Comparar(antes, depois));
    }

    [Fact]
    public void DriverDeVideoNovoVirouAlteracao()
    {
        var antes = Retrato(video: "AMD · 32.0.1");
        var depois = Retrato(video: "AMD · 32.0.2");

        var mudancas = Mudancas.Comparar(antes, depois);

        Assert.Single(mudancas);
        Assert.Equal(TipoMudanca.Alterado, mudancas[0].Tipo);
        Assert.Equal(CategoriaMudanca.Driver, mudancas[0].Categoria);
        Assert.Equal("AMD · 32.0.1", mudancas[0].Antes);
        Assert.Equal("AMD · 32.0.2", mudancas[0].Depois);
    }

    [Fact]
    public void ProgramaInstaladoVirouNovo()
    {
        var antes = Retrato(programas: new List<string> { "Steam · 2.0" });
        var depois = Retrato(programas: new List<string> { "Steam · 2.0", "Discord · 1.0" });

        var mudancas = Mudancas.Comparar(antes, depois);

        Assert.Single(mudancas);
        Assert.Equal(TipoMudanca.Novo, mudancas[0].Tipo);
        Assert.Equal(CategoriaMudanca.Programa, mudancas[0].Categoria);
        Assert.Equal("Programa: Discord", mudancas[0].Item);
        Assert.Equal("Discord · 1.0", mudancas[0].Depois);
    }

    [Fact]
    public void ProgramaDesinstaladoVirouRemovido()
    {
        var antes = Retrato(programas: new List<string> { "Steam · 2.0", "Discord · 1.0" });
        var depois = Retrato(programas: new List<string> { "Steam · 2.0" });

        var mudancas = Mudancas.Comparar(antes, depois);

        Assert.Single(mudancas);
        Assert.Equal(TipoMudanca.Removido, mudancas[0].Tipo);
        Assert.Equal("Programa: Discord", mudancas[0].Item);
    }

    [Fact]
    public void VersaoNovaDoMesmoProgramaEAlteracaoNaoNovoERemovido()
    {
        var antes = Retrato(programas: new List<string> { "Steam · 2.0" });
        var depois = Retrato(programas: new List<string> { "Steam · 2.1" });

        var mudancas = Mudancas.Comparar(antes, depois);

        Assert.Single(mudancas);
        Assert.Equal(TipoMudanca.Alterado, mudancas[0].Tipo);
        Assert.Equal("Programa: Steam", mudancas[0].Item);
        Assert.Equal("Steam · 2.0", mudancas[0].Antes);
        Assert.Equal("Steam · 2.1", mudancas[0].Depois);
    }

    [Fact]
    public void MesmoProgramaEmDuasEntradasNaoGeraRuido()
    {
        // O mesmo app aparece duas vezes (32 e 64 bits): não é instalação nova.
        var antes = Retrato(programas: new List<string> { "Visual C++ · 14.0" });
        var depois = Retrato(programas: new List<string> { "Visual C++ · 14.0", "Visual C++ · 14.0 (x64)" });

        var mudancas = Mudancas.Comparar(antes, depois);

        Assert.Single(mudancas);
        Assert.Equal(TipoMudanca.Alterado, mudancas[0].Tipo);
    }

    [Fact]
    public void ItemDeInicializacaoNovoFoiClassificado()
    {
        var antes = Retrato(inicializacao: new List<string> { "OneDrive · Registro · usuário" });
        var depois = Retrato(inicializacao: new List<string> { "OneDrive · Registro · usuário", "Steam · Registro · usuário" });

        var mudancas = Mudancas.Comparar(antes, depois);

        Assert.Single(mudancas);
        Assert.Equal(CategoriaMudanca.Inicializacao, mudancas[0].Categoria);
        Assert.Equal(TipoMudanca.Novo, mudancas[0].Tipo);
    }

    [Fact]
    public void VariosCamposMudradosAoMesmoTempo()
    {
        var antes = Retrato(windows: "11 26100", bios: "3804", energia: "Equilibrado");
        var depois = Retrato(windows: "11 26100", bios: "4641", energia: "Alto desempenho");

        var mudancas = Mudancas.Comparar(antes, depois);

        Assert.Equal(2, mudancas.Count);
        Assert.Contains(mudancas, m => m.Categoria == CategoriaMudanca.Bios);
        Assert.Contains(mudancas, m => m.Categoria == CategoriaMudanca.Energia);
    }

    [Fact]
    public void CampoVazioNosDoisLadosNaoVirouMudanca()
    {
        // Não deu para ler: vazio nos dois lados não é mudança do PC.
        var antes = Retrato(video: "");
        var depois = Retrato(video: "");

        Assert.Empty(Mudancas.Comparar(antes, depois));
    }

    [Fact]
    public void MudancaCarregaADataDoRetrato()
    {
        var antes = Retrato(dia: 1);
        var depois = Retrato(dia: 2, bios: "5001");

        var mudancas = Mudancas.Comparar(antes, depois);

        Assert.Single(mudancas);
        Assert.Equal(new DateTime(2026, 11, 2, 10, 0, 0), mudancas[0].Quando);
    }

    [Fact]
    public void ResumoDaMudancaFalaPortugues()
    {
        var antes = Retrato(programas: new List<string> { "Steam · 2.0" });
        var depois = Retrato(programas: new List<string> { "Steam · 2.0", "Discord · 1.0" });

        var mudanca = Mudancas.Comparar(antes, depois).Single();

        Assert.Equal("Programa: Discord — novo (Discord · 1.0)", mudanca.Resumo);
    }

    // ---------------------------------------------------------------- auxiliares

    [Theory]
    [InlineData("Steam · 2.0", "Steam", "2.0")]
    [InlineData("Steam", "Steam", "")]
    [InlineData("A · B · 3.0", "A · B", "3.0")]
    public void SeparaNomeEVersao(string item, string nome, string versao)
    {
        Assert.Equal(nome, Mudancas.SemVersao(item));
        Assert.Equal(versao, Mudancas.Versao(item));
    }

    [Fact]
    public void PlanoDeEnergiaConhecidoPeloGuid()
    {
        Assert.Equal("Alto desempenho", PlanosEnergia.NomeDoPlano("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c"));
        Assert.Equal("Equilibrado", PlanosEnergia.NomeDoPlano("381b4222-f694-41f0-9685-ff5bb260df2e"));
        Assert.Equal("Economia de energia", PlanosEnergia.NomeDoPlano("a1841308-3541-4fab-bc81-f71556f20b4a"));
        // GUID desconhecido volta como está: melhor mostrar o GUID do que mentir.
        Assert.Equal("nao-conhecido", PlanosEnergia.NomeDoPlano("nao-conhecido"));
    }

    [Fact]
    public void MemoriaEmBytesViraGigabytes()
    {
        // O WMI devolve a capacidade em bytes: 16 GB não é 16384 GB.
        Assert.Equal(16, Mudancas.BytesEmGb(16L * 1024 * 1024 * 1024));
        Assert.Equal(32, Mudancas.BytesEmGb(32L * 1024 * 1024 * 1024));
        Assert.Equal(0, Mudancas.BytesEmGb(0));
    }

    // ---------------------------------------------------------------- perfil de recursos

    [Fact]
    public void EconomiaConsomeMenosQueCompleto()
    {
        Assert.True(PoliticaRecursos.IntervaloSensoresMs(PerfilRecursos.Economia)
                    > PoliticaRecursos.IntervaloSensoresMs(PerfilRecursos.Completo));
        Assert.True(PoliticaRecursos.IntervaloInstantaneoMinutos(PerfilRecursos.Economia)
                    > PoliticaRecursos.IntervaloInstantaneoMinutos(PerfilRecursos.Completo));
    }

    [Fact]
    public void EconomiaDesligaOverlayEAnaliseAoAbrir()
    {
        Assert.False(PoliticaRecursos.OverlayAutomatico(PerfilRecursos.Economia));
        Assert.False(PoliticaRecursos.AnaliseSaudeAoAbrir(PerfilRecursos.Economia));
        Assert.False(PoliticaRecursos.GravarHistorico(PerfilRecursos.Economia));

        Assert.True(PoliticaRecursos.OverlayAutomatico(PerfilRecursos.Completo));
        Assert.True(PoliticaRecursos.AnaliseSaudeAoAbrir(PerfilRecursos.Completo));
        Assert.True(PoliticaRecursos.GravarHistorico(PerfilRecursos.Completo));
    }

    [Fact]
    public void CadaPerfilTemIntervaloDeSensorPositivo()
    {
        foreach (var perfil in Enum.GetValues<PerfilRecursos>())
            Assert.True(PoliticaRecursos.IntervaloSensoresMs(perfil) >= 500);
    }
}
