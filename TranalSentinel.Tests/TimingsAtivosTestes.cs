using Xunit;

namespace TranalSentinel.Tests;

/// <summary>
/// Testes dos tempos ativos de memória: o decodificador dos registradores do
/// UMC e a comparação com o perfil do módulo.
///
/// <para>
/// A evidência é real: os registradores foram lidos no Ryzen 7 5700X do dono
/// pelo módulo PawnIO do SMN (veja RegistrosUmcRyzen5700X).
/// </para>
/// </summary>
public class TimingsAtivosTestes
{
    private const int VelocidadeConfigurada = 3466;

    private static TimingsAtivos? DecodificarReal(out string? erro) =>
        TimingsAtivosDecoder.Decodificar(new RegistrosSmn(RegistrosUmcRyzen5700X.DoisCanais()), VelocidadeConfigurada, out erro);

    [Fact]
    public void RegistrosReais_DecodificamOsTemposAtivosDoRyzen5700X()
    {
        TimingsAtivos? tempos = DecodificarReal(out string? erro);

        Assert.NotNull(tempos);
        Assert.Null(erro);

        Assert.Equal(TipoMemoriaDdr.Ddr4, tempos!.Tipo);
        Assert.Equal(1733, tempos.ClockMhz);
        Assert.Equal(3466, tempos.VelocidadeMts);
        Assert.Equal(1, tempos.CommandRate);
        Assert.True(tempos.GearDownMode);
        Assert.False(tempos.BankGroupSwap);
    }

    [Fact]
    public void RegistrosReais_DecodificamOsTemposPrincipais()
    {
        TimingsAtivos? tempos = DecodificarReal(out _);

        Assert.NotNull(tempos);
        Assert.Equal(18, tempos!.TCl);
        Assert.Equal(42, tempos.TRas);
        Assert.Equal(22, tempos.TRcdLeitura);
        Assert.Equal(22, tempos.TRcdEscrita);
        Assert.Equal(22, tempos.TRp);
        Assert.Equal(80, tempos.TRc);
        Assert.Equal("18-22-22-42", tempos.ResumoCiclos);
    }

    [Fact]
    public void RegistrosReais_DecodificamOsTemposSecundarios()
    {
        TimingsAtivos? tempos = DecodificarReal(out _);

        Assert.NotNull(tempos);
        Assert.Equal(6, tempos!.TRrds);
        Assert.Equal(9, tempos.TRrdl);
        Assert.Equal(14, tempos.TRtp);
        Assert.Equal(37, tempos.TFaw);
        Assert.Equal(18, tempos.TCwl);
        Assert.Equal(5, tempos.TWtrs);
        Assert.Equal(13, tempos.TWtrl);
        Assert.Equal(26, tempos.TWr);
        Assert.Equal(0, tempos.TCke);
        Assert.Equal(255, tempos.TStag);
    }

    [Fact]
    public void IntervaloDeRefreshFicaEmMicrossegundos()
    {
        TimingsAtivos? tempos = DecodificarReal(out _);

        Assert.NotNull(tempos);
        Assert.Equal(13495, tempos!.TRefi);

        // 7,8 us é o intervalo de refresh do DDR4 (2× 3,9 us).
        Assert.InRange(tempos.TRefiNs, 7000, 8000);
    }

    [Fact]
    public void DuracaoDeUmCicloEUmTercoDeMeioPeriodo()
    {
        TimingsAtivos? tempos = DecodificarReal(out _);

        Assert.NotNull(tempos);
        Assert.Equal(1000.0 / 1733, tempos!.CicloNs, 6);
        Assert.Equal(18 * tempos.CicloNs, tempos.TClNs, 6);
    }

    [Fact]
    public void VelocidadeConfiguradaDiferenteDoClockLidoRecusa()
    {
        // O clock lido é 3466 MT/s: se o Windows disser outra coisa, os
        // registradores não são os de memória e o app não mostra nada.
        TimingsAtivos? tempos = TimingsAtivosDecoder.Decodificar(
            new RegistrosSmn(RegistrosUmcRyzen5700X.DoisCanais()), 3200, out string? erro);

        Assert.Null(tempos);
        Assert.Contains("não bate", erro);
    }

    [Fact]
    public void MemoriaDdr5NaoERecuperadaComoDdr4()
    {
        var registros = RegistrosUmcRyzen5700X.DoisCanais();
        registros[0x50100] = 0x80000201; // bits 1-0 = 01 = DDR5

        TimingsAtivos? tempos = TimingsAtivosDecoder.Decodificar(new RegistrosSmn(registros), VelocidadeConfigurada, out string? erro);

        Assert.Null(tempos);
        Assert.Contains("DDR5", erro);
    }

    [Fact]
    public void RegistradorInexistenteRecusaEmVezDeDevolverZero()
    {
        var registros = RegistrosUmcRyzen5700X.Canal0();
        registros[0x50204] = RegistrosSmn.SemDado;

        TimingsAtivos? tempos = TimingsAtivosDecoder.Decodificar(new RegistrosSmn(registros), VelocidadeConfigurada, out string? erro);

        Assert.Null(tempos);
        Assert.NotNull(erro);
    }

    [Fact]
    public void TemposInexistentesRecusam()
    {
        var registros = RegistrosUmcRyzen5700X.Canal0();
        registros[0x50204] = 0x16160000; // tCL zerado: não é memória

        TimingsAtivos? tempos = TimingsAtivosDecoder.Decodificar(new RegistrosSmn(registros), VelocidadeConfigurada, out string? erro);

        Assert.Null(tempos);
        Assert.Contains("coerentes", erro);
    }

    [Fact]
    public void CanalVazioMandaLerNoCanal1()
    {
        // O canal 0 respondendo 0x300 significa "canal vazio": o bloco de
        // verdade é o do canal 1 (regra do ryzen_smu).
        var registros = RegistrosUmcRyzen5700X.DoisCanais();
        registros[0x50200] = TimingsAtivosDecoder.MarcadorCanalVazio;

        TimingsAtivos? tempos = TimingsAtivosDecoder.Decodificar(new RegistrosSmn(registros), VelocidadeConfigurada, out _);

        Assert.NotNull(tempos);
        Assert.Equal((int)TimingsAtivosDecoder.BlocoCanal1, tempos!.EnderecoBloco);
        Assert.Equal(18, tempos.TCl);
    }

    [Fact]
    public void TempoZeradoNaoPodeVirarAcusacaoDeAjusteManual()
    {
        // Um tRCD zerado entraria na comparação como "mais apertado que o
        // perfil" e o app acusaria um ajuste manual que não existe.
        var registros = RegistrosUmcRyzen5700X.Canal0();
        registros[0x50204] = 0x16162A00; // tRCD de leitura zerado

        TimingsAtivos? tempos = TimingsAtivosDecoder.Decodificar(new RegistrosSmn(registros), VelocidadeConfigurada, out string? erro);

        Assert.Null(tempos);
        Assert.Contains("coerentes", erro);
    }

    [Fact]
    public void TipoDeMemoriaNaoLidoNaoVirouLpddr5()
    {
        // 0xFFFFFFFF nos dois bits baixos é 3, que seria LPDDR5: o registro
        // não lido tem que ser "não sei".
        var registros = RegistrosUmcRyzen5700X.Canal0();
        registros[0x50100] = RegistrosSmn.SemDado;

        TimingsAtivos? tempos = TimingsAtivosDecoder.Decodificar(new RegistrosSmn(registros), VelocidadeConfigurada, out string? erro);

        Assert.Null(tempos);
        Assert.Contains("não informa o tipo", erro);
    }

    [Fact]
    public void RegistroDeTrocaDeBancoNaoLidoNaoAfirmaQueEstaLigada()
    {
        var registros = RegistrosUmcRyzen5700X.Canal0();
        registros[0x50050] = RegistrosSmn.SemDado;

        TimingsAtivos? tempos = TimingsAtivosDecoder.Decodificar(new RegistrosSmn(registros), VelocidadeConfigurada, out _);

        Assert.NotNull(tempos);
        Assert.False(tempos!.BankGroupSwap);
    }

    [Fact]
    public void ComparacaoComOPerfilRealNaoApontaAjusteManual()
    {
        var modulo = DecodificadorSpd.Decodificar(SpdReal.Dimm1);
        TimingsAtivos? tempos = DecodificarReal(out _);

        Assert.NotNull(tempos);
        RelatorioTimings relatorio = MemoriaRegras.CompararTimings(tempos!, modulo, VelocidadeConfigurada);

        Assert.Equal("XMP 2.0", relatorio.Referencia);
        Assert.False(relatorio.HaAjusteManual);
        Assert.Equal(0, relatorio.ApertadosPrimarios);

        // O clock subiu (3200 → 3466), mas os ciclos dos tempos continuam os
        // do perfil: a BIOS aplicou o XMP e só mexeu na frequência.
        Assert.Equal(modulo.MelhorPerfil!.Timing.ResumoCiclos, tempos!.ResumoCiclos);

        ComparacaoTiming tcl = Item(relatorio, "tCL");
        Assert.Equal(SituacaoTiming.Igual, tcl.Situacao);
        Assert.Equal(18, tcl.Ativo);
        Assert.Equal(18, tcl.Perfil);

        ComparacaoTiming trcd = Item(relatorio, "tRCD");
        Assert.Equal(SituacaoTiming.Igual, trcd.Situacao);
        Assert.Equal(22, trcd.Ativo);
        Assert.Equal(22, trcd.Perfil);

        ComparacaoTiming tras = Item(relatorio, "tRAS");
        Assert.Equal(SituacaoTiming.Igual, tras.Situacao);
        Assert.Equal(42, tras.Ativo);
        Assert.Equal(42, tras.Perfil);

        // O tRC sobe junto com o clock: mais solto, nunca mais apertado.
        ComparacaoTiming trc = Item(relatorio, "tRC");
        Assert.Equal(SituacaoTiming.MaisSolto, trc.Situacao);

        // O XMP deste módulo não grava tWR nem tWTR: a referência é
        // desconhecida, não zero.
        ComparacaoTiming twr = Item(relatorio, "tWR");
        Assert.Equal(SituacaoTiming.SemComparacao, twr.Situacao);
        Assert.Null(twr.Perfil);
        Assert.Equal(26, twr.Ativo);
    }

    [Fact]
    public void TempoApertadoAMaoApontaAjusteManual()
    {
        var modulo = DecodificadorSpd.Decodificar(SpdReal.Dimm1);
        TimingsAtivos? tempos = DecodificarReal(out _);
        Assert.NotNull(tempos);

        // Simula o usuário apertando o tCL na mão (16 em vez de 18).
        var apertado = new TimingsAtivos
        {
            Tipo = tempos!.Tipo,
            ClockMhz = tempos.ClockMhz,
            VelocidadeMts = tempos.VelocidadeMts,
            TCl = 16,
            TRas = tempos.TRas,
            TRcdLeitura = tempos.TRcdLeitura,
            TRcdEscrita = tempos.TRcdEscrita,
            TRp = tempos.TRp,
            TRc = tempos.TRc,
            TFaw = tempos.TFaw,
            TCwl = tempos.TCwl,
            TWtrs = tempos.TWtrs,
            TWtrl = tempos.TWtrl,
            TWr = tempos.TWr
        };

        RelatorioTimings relatorio = MemoriaRegras.CompararTimings(apertado, modulo, VelocidadeConfigurada);

        Assert.True(relatorio.HaAjusteManual);
        Assert.Equal(1, relatorio.ApertadosPrimarios);
        Assert.Equal(SituacaoTiming.MaisAperto, Item(relatorio, "tCL").Situacao);

        // E o TRANAL SAÚDE passa a relatar isso.
        Achado? achado = MemoriaRegras.Avaliar(VelocidadeConfigurada, modulo, apertado);
        Assert.NotNull(achado);
        Assert.Equal("Memória", achado!.Area);
        Assert.Contains("mais apertados", achado.Mensagem);
    }

    [Fact]
    public void MemoriaNoPadraoJedecNaoEAcusadaDeAjusteManual()
    {
        // Regressão: quem não ligou o XMP roda no padrão JEDEC, com tempos
        // MENORES que os do perfil (16 contra 18). Comparar contra o perfil
        // acusaria um ajuste manual que não existe.
        var modulo = DecodificadorSpd.Decodificar(SpdReal.Dimm1);

        var jedec = new TimingsAtivos
        {
            Tipo = TipoMemoriaDdr.Ddr4,
            ClockMhz = 1200,
            VelocidadeMts = 2400,
            TCl = 16,
            TRas = 37,
            TRcdLeitura = 16,
            TRcdEscrita = 16,
            TRp = 16,
            TRc = 52
        };

        RelatorioTimings relatorio = MemoriaRegras.CompararTimings(jedec, modulo, 2400);

        Assert.Equal("JEDEC", relatorio.Referencia);
        Assert.False(relatorio.HaAjusteManual);
        Assert.Equal(SituacaoTiming.Igual, Item(relatorio, "tCL").Situacao);

        // E o TRANAL SAÚDE não inventa causa de travamento.
        Achado? achado = MemoriaRegras.Avaliar(2400, modulo, jedec);
        Assert.NotNull(achado);
        Assert.DoesNotContain("mais apertados", achado!.Mensagem);
    }

    [Fact]
    public void MesmosTemposAcimaDoPerfilContamComoAjusteManual()
    {
        // O oposto do caso anterior: os tempos do JEDEC com o clock acima do
        // perfil ARE um ajuste manual — aí a comparação é contra o perfil.
        var modulo = DecodificadorSpd.Decodificar(SpdReal.Dimm1);

        var jedec = new TimingsAtivos
        {
            Tipo = TipoMemoriaDdr.Ddr4,
            ClockMhz = 1733,
            VelocidadeMts = 3466,
            TCl = 16,
            TRas = 37,
            TRcdLeitura = 16,
            TRcdEscrita = 16,
            TRp = 16,
            TRc = 52
        };

        RelatorioTimings relatorio = MemoriaRegras.CompararTimings(jedec, modulo, 3466);

        Assert.Equal("XMP 2.0", relatorio.Referencia);
        Assert.True(relatorio.HaAjusteManual);
    }

    [Fact]
    public void SemTemposAtivosNAoHaAjusteManual()
    {
        var modulo = DecodificadorSpd.Decodificar(SpdReal.Dimm1);

        // Sem os tempos ativos não há o que comparar: o veredito é o de antes.
        Achado? achado = MemoriaRegras.Avaliar(VelocidadeConfigurada, modulo, null);

        Assert.NotNull(achado);
        Assert.DoesNotContain("mais apertados", achado!.Mensagem);
    }

    private static ComparacaoTiming Item(RelatorioTimings relatorio, string nome) =>
        relatorio.Itens.First(i => i.Nome == nome);
}
