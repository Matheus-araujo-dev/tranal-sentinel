using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace TranalSentinel.Tests;

/// <summary>
/// Testes do decodificador de SPD, do leitor de SMBus e da regra de memória.
///
/// A evidência usada aqui são os 512 bytes reais do Asgard VALKYRIE 16 GB
/// DDR4-3200 (lidos no hardware pela sonda PawnIO), conferidos contra o que
/// o CPU-Z mostra para o mesmo módulo: 16384 MB, DDR4-2400, XMP 2.0,
/// timings ativos 18-20-20-42.
/// </summary>
public class SpdTestes
{
    // ------------------------------------------------------------------ decodificador

    [Fact]
    public void SpdReal_DecodificaTipoCapacidadeEIdentidade()
    {
        var modulo = DecodificadorSpd.Decodificar(SpdReal.Dimm1);

        Assert.Equal("DDR4", modulo.Tipo);
        Assert.Equal(16L * 1024 * 1024 * 1024, modulo.CapacidadeBytes);
        Assert.Equal(1, modulo.Ranks);
        Assert.Equal(64, modulo.LarguraBits);
        Assert.Equal("x8", modulo.LarguraChip);
        Assert.Equal("UDIMM", modulo.TipoModulo);
        Assert.Equal("VMA45UH-MEC1U2AV5", modulo.PartNumber);
        Assert.Equal("D4163227", modulo.Serial);
        Assert.Equal("23/22", modulo.FabricadoEm);
    }

    [Fact]
    public void SpdReal_IdentificadorBinarioViraHexadecimal()
    {
        // O fabricante do módulo (0x140) e o do chip de memória (0x15E) não são
        // ASCII neste módulo: a regra é o mesmo texto não imprimível vira hex.
        var modulo = DecodificadorSpd.Decodificar(SpdReal.Dimm1);

        Assert.Equal("88F1", modulo.Fabricante);
        Assert.Equal("80CE", modulo.FabricanteDram);
    }

    [Fact]
    public void SpdReal_OsDoisModulosSaoIguais()
    {
        var primeiro = DecodificadorSpd.Decodificar(SpdReal.Dimm1);
        var segundo = DecodificadorSpd.Decodificar(SpdReal.Dimm2);

        Assert.Equal(primeiro.PartNumber, segundo.PartNumber);
        Assert.Equal(primeiro.CapacidadeBytes, segundo.CapacidadeBytes);
        Assert.Equal(primeiro.Jedec.VelocidadeMts, segundo.Jedec.VelocidadeMts);
        Assert.Equal(primeiro.MelhorPerfil?.Timing.VelocidadeMts, segundo.MelhorPerfil?.Timing.VelocidadeMts);
    }

    [Fact]
    public void SpdReal_JedecE2400ComCl16()
    {
        var modulo = DecodificadorSpd.Decodificar(SpdReal.Dimm1);

        Assert.Equal(2400, modulo.Jedec.VelocidadeMts);
        Assert.Equal(16, modulo.Jedec.Cl);
        Assert.Equal(16, modulo.Jedec.Trcd);
        Assert.Equal(16, modulo.Jedec.Trp);
        Assert.Equal(13.75, modulo.Jedec.TaaNs, 2);
    }

    [Fact]
    public void SpdReal_TemPerfilXmp2Com3200ECl18()
    {
        var modulo = DecodificadorSpd.Decodificar(SpdReal.Dimm1);

        SpdPerfil? perfil = modulo.MelhorPerfil;
        Assert.NotNull(perfil);
        Assert.Equal("XMP 2.0", perfil!.Nome);
        Assert.True(perfil.Habilitado);
        Assert.Equal(3200, perfil.Timing.VelocidadeMts);
        Assert.Equal(18, perfil.Timing.Cl);
        Assert.Equal(42, perfil.Timing.Tras);
        Assert.Equal(1.35, perfil.Voltagem, 2);
    }

    [Fact]
    public void SpdReal_PerfilBateComOsTimingsAtivosDoCpuZ()
    {
        // O CPU-Z mostra 18-20-20-42 para este módulo. O SPD do perfil XMP
        // tem de reproduzir pelo menos os mesmos números de CL e tRAS.
        var modulo = DecodificadorSpd.Decodificar(SpdReal.Dimm1);
        SpdTiming perfil = modulo.MelhorPerfil!.Timing;

        Assert.Equal(18, perfil.Cl);
        Assert.Equal(42, perfil.Tras);
    }

    [Fact]
    public void SpdIncompleto_NaoQuebra()
    {
        var modulo = DecodificadorSpd.Decodificar(new byte[] { 0x23, 0x11, 0x0C });

        Assert.Equal("SPD incompleto", modulo.Tipo);
        Assert.Equal(0, modulo.CapacidadeBytes);
    }

    [Fact]
    public void SpdDeOutraFamilia_NaoERelidoComoDdr4()
    {
        byte[] spd = new byte[512];
        spd[2] = 0x0B; // DDR3

        var modulo = DecodificadorSpd.Decodificar(spd);

        Assert.Equal("DDR3", modulo.Tipo);
        Assert.Empty(modulo.Perfis);
    }

    // ------------------------------------------------------------------ leitor

    [Fact]
    public void Leitor_LePaginaSelecionandoAntesDeCadaPagina()
    {
        var falso = new SmbusFalso(SpdReal.Dimm1);

        byte[]? lido = SpdLeitor.LerModulo(falso, 0x52);

        Assert.NotNull(lido);
        Assert.Equal(512, lido!.Length);
        Assert.Equal(SpdReal.Dimm1, lido);

        // A página 1 só é lida depois de o controle de página ser trocado.
        Assert.Equal(0x36, falso.Escritas[0].Endereco);
        Assert.Equal(0x37, falso.Escritas[1].Endereco);
    }

    [Fact]
    public void Leitor_DevolveNuloQuandoOBarramentoNaoResponde()
    {
        var falso = new SmbusFalso(SpdReal.Dimm1) { Responder = false };

        Assert.Null(SpdLeitor.LerModulo(falso, 0x52));
    }

    // ------------------------------------------------------------------ regra

    [Fact]
    public void Regra_ClockDoPerfilClassificaComoPerfilAtivo()
    {
        var modulo = DecodificadorSpd.Decodificar(SpdReal.Dimm1);

        Assert.Equal(ModoMemoria.PerfilAtivo, MemoriaRegras.Classificar(3200, modulo));
    }

    [Fact]
    public void Regra_ClockJedecClassificaComoPadraoJedec()
    {
        var modulo = DecodificadorSpd.Decodificar(SpdReal.Dimm1);

        Assert.Equal(ModoMemoria.PadraoJedec, MemoriaRegras.Classificar(2400, modulo));
    }

    [Fact]
    public void Regra_AcimaDoJedecEForaDoPerfilEOverclockManual()
    {
        var modulo = DecodificadorSpd.Decodificar(SpdReal.Dimm1);

        Assert.Equal(ModoMemoria.OverclockManual, MemoriaRegras.Classificar(2800, modulo));
    }

    [Fact]
    public void Regra_AcimaDoPerfilEAchaMaisGrave()
    {
        var modulo = DecodificadorSpd.Decodificar(SpdReal.Dimm1);

        Assert.Equal(ModoMemoria.AcimaDoPerfil, MemoriaRegras.Classificar(3600, modulo));

        Achado? achado = MemoriaRegras.Avaliar(3600, modulo);
        Assert.NotNull(achado);
        Assert.Equal(Severidade.Medio, achado!.Severidade);
    }

    [Fact]
    public void Regra_OverclockManualEBaixo()
    {
        var modulo = DecodificadorSpd.Decodificar(SpdReal.Dimm1);

        Achado? achado = MemoriaRegras.Avaliar(2800, modulo);

        Assert.NotNull(achado);
        Assert.Equal(Severidade.Baixo, achado!.Severidade);
    }

    [Fact]
    public void Regra_NoPerfilNaoGeraAchado()
    {
        var modulo = DecodificadorSpd.Decodificar(SpdReal.Dimm1);

        Assert.Null(MemoriaRegras.Avaliar(3200, modulo));
    }

    [Fact]
    public void Regra_NoPadraoJedecSugereAtivarOPerfil()
    {
        var modulo = DecodificadorSpd.Decodificar(SpdReal.Dimm1);

        Achado? achado = MemoriaRegras.Avaliar(2400, modulo);

        Assert.NotNull(achado);
        Assert.Equal(Severidade.Informativo, achado!.Severidade);
        Assert.Contains("XMP 2.0", achado.Acao);
    }

    [Fact]
    public void Regra_SemClockNaoClassifica()
    {
        var modulo = DecodificadorSpd.Decodificar(SpdReal.Dimm1);

        Assert.Equal(ModoMemoria.Desconhecido, MemoriaRegras.Classificar(0, modulo));
        Assert.Null(MemoriaRegras.Avaliar(0, modulo));
    }

    /// <summary>SMBus falso: devolve um SPD conhecido sem tocar em hardware.</summary>
    private sealed class SmbusFalso : ISmbusLeitor
    {
        private readonly byte[] _spd;
        private int _paginaAtual;

        public SmbusFalso(byte[] spd) => _spd = spd;

        public bool Responder { get; set; } = true;

        public List<(int Endereco, int Comando, byte Valor)> Escritas { get; } = new();

        public bool SelecionarPorta(int porta) => true;

        public bool LerByteData(int endereco, int comando, out byte valor)
        {
            valor = 0;
            if (!Responder) return false;

            int indice = (_paginaAtual * 256) + comando;
            if (indice < 0 || indice >= _spd.Length) return false;

            valor = _spd[indice];
            return true;
        }

        public bool LerWordData(int endereco, int comando, out ushort valor)
        {
            valor = 0;
            if (!Responder) return false;

            int indice = (_paginaAtual * 256) + comando;
            if (indice < 0 || indice + 1 >= _spd.Length) return false;

            valor = (ushort)(_spd[indice] | (_spd[indice + 1] << 8));
            return true;
        }

        public bool EscreverByteData(int endereco, int comando, byte valor)
        {
            if (!Responder) return false;

            Escritas.Add((endereco, comando, valor));

            // O controle de página do hub (0x36/0x37) escolhe de onde ler.
            if (endereco == 0x36 || endereco == 0x37)
                _paginaAtual = endereco - 0x36;

            return true;
        }
    }
}
