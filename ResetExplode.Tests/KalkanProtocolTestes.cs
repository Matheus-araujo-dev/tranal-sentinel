using Xunit;

namespace ResetExplode.Tests;

/// <summary>
/// Testes do protocolo do visor Kalkan (firmware 1.2).
///
/// Os vetores dourados vêm da análise do binário oficial
/// "Kalkan PC SmartMonitor DZ V1.2.exe", publicada no projeto aberto
/// kalkan-monitor-rs (MIT). Se um quadro embaralhado aqui não bate byte a byte
/// com o vetor, o visor não vai entender o que o RESET EXPLODE manda.
/// </summary>
public class KalkanProtocolTestes
{
    // temp=45, fan=1500, cpu_load=20 — quadro completo de 64 bytes no fio.
    private static readonly (byte Seed, byte Idx, string Hex)[] VetoresDourados =
    {
        (0x00, 0, "0186e0002fa0947b42afa51b8fe1de592044c5d6c19a9cf6855f4a371fc3bcb240898bad833539ed0abe946e3f8779648113175b066a73da157d28dc7f0ef2c9"),
        (0x00, 1, "0186e0013cb7cf7d28dc7f0ef2c902262eb60cd4e7b42afa51b8fe1de592044c5d6c19a9cf6855f4a371fc3bcb240898bad833539ed0abe946e3f87796481131"),
        (0xFF, 0, "0186e00756c4156855f4a371fc3bcb240898bad833539ed0abe946e3f8779648113175b066a73da157d28dc7f0ef2c902262eb60cd4e7b42afa51b8fe1de5920"),
        (0xFF, 1, "0186e00645d34e6e3f8779648113175b066a73da157d28dc7f0ef2c902262eb60cd4e7b42afa51b8fe1de592044c5d6c19a9cf6855f4a371fc3bcb240898bad8"),
        (0x2A, 0, "0186e07d077ceb75b066a73da157d28dc7f0ef2c902262eb60cd4e7b42afa51b8fe1de592044c5d6c19a9cf6855f4a371fc3bcb240898bad833539ed0abe946e"),
        (0x2A, 1, "0186e07c146bb073da157d28dc7f0ef2c902262eb60cd4e7b42afa51b8fe1de592044c5d6c19a9cf6855f4a371fc3bcb240898bad833539ed0abe946e3f87796"),
    };

    [Fact]
    public void QuadroEmbaralhadoBateComOsVetoresDourados()
    {
        foreach (var (seed, idx, hex) in VetoresDourados)
        {
            var quadro = KalkanProtocol.BuildWireFrame(seed, idx, 45, 1500, 20);
            Assert.Equal(KalkanProtocol.ReportLen, quadro.Length);
            Assert.Equal(hex, KalkanProtocol.ToHex(quadro));
        }
    }

    [Fact]
    public void VetorDeRegressaoBateNosPrimeiros16Bytes()
    {
        // semente 0x2A, idx 1, temp 40, fan 1200, carga 55.
        var quadro = KalkanProtocol.BuildWireFrame(0x2A, 1, 40, 1200, 55);
        byte[] esperado =
        {
            0x01, 0x86, 0xE0, 0x7C, 0x11, 0x35, 0x83, 0x01,
            0x13, 0x17, 0x5B, 0x06, 0x6A, 0x73, 0xDA, 0x15
        };
        Assert.Equal(esperado, quadro[..16]);
    }

    [Fact]
    public void PedidoDeSementeTemOFormatoDoAppOficial()
    {
        var pedido = KalkanProtocol.BuildSeedRequest();
        Assert.Equal(KalkanProtocol.ReportLen, pedido.Length);
        Assert.Equal(new byte[] { 0x01, 0x81, 0xE3 }, pedido[..3]);
        Assert.All(pedido[3..], b => Assert.Equal(0, b));
    }

    [Fact]
    public void QuadroBrutoTemOCabealhoEOTamanhoCertos()
    {
        var f = KalkanProtocol.BuildFrame(0x05, 42, 0x0ABC, 73);
        Assert.Equal(new byte[] { 0x01, 0x86, 0xE0, 0x05, 42, 0x0A, 0xBC, 73 }, f[..8]);
        Assert.All(f[8..], b => Assert.Equal(0, b));
    }

    [Fact]
    public void EmbaralhadorPreservaOCabecalho()
    {
        var f = KalkanProtocol.BuildFrame(1, 40, 1200, 55);
        var saida = KalkanProtocol.Scramble(0x2A, f);
        Assert.Equal(f.Length, saida.Length);
        Assert.Equal(new byte[] { 0x01, 0x86, 0xE0 }, saida[..3]);
    }

    [Fact]
    public void EmbaralhadorReiniciaDaSementeEmCadaQuadro()
    {
        // A semente entra por valor: dois quadros com a mesma semente e o mesmo
        // índice têm de sair idênticos, mesmo depois de outros quadros.
        var a = KalkanProtocol.BuildWireFrame(0x2A, 7, 50, 1000, 30);
        _ = KalkanProtocol.BuildWireFrame(0x2A, 8, 60, 1200, 40);
        var b = KalkanProtocol.BuildWireFrame(0x2A, 7, 50, 1000, 30);
        Assert.Equal(a, b);
    }

    [Fact]
    public void SementeEhExtraidaNasDuasPosicoesPossiveis()
    {
        Assert.True(KalkanProtocol.TryParseSeed(new byte[] { 0xE3, 0x2A }, out byte s1));
        Assert.Equal(0x2A, s1);

        Assert.True(KalkanProtocol.TryParseSeed(new byte[] { 0x01, 0xE3, 0xFF }, out byte s2));
        Assert.Equal(0xFF, s2);

        // Resposta real do visor: report ID na frente, marcador e semente.
        var real = new byte[65];
        real[0] = 0x01; real[1] = 0xE3; real[2] = 0x23;
        Assert.True(KalkanProtocol.TryParseSeed(real, out byte s3));
        Assert.Equal(0x23, s3);
    }

    [Fact]
    public void RespostaSemMarcadorNaoTemSemente()
    {
        Assert.False(KalkanProtocol.TryParseSeed(new byte[] { 0x00, 0x00, 0xE3 }, out _));
        Assert.False(KalkanProtocol.TryParseSeed(Array.Empty<byte>(), out _));
        Assert.False(KalkanProtocol.TryParseSeed(new byte[] { 0xE3 }, out _));
        Assert.False(KalkanProtocol.TryParseSeed(new byte[] { 0x00, 0xE3 }, out _));
        Assert.False(KalkanProtocol.TryParseSeed(new byte[65], out _));
    }

    [Fact]
    public void ConstrutorDeQuadrosComecaNoIndiceZeroEDaVoltaEm256()
    {
        var b = new KalkanFrameBuilder(0x2A);
        Assert.Equal(0x2A, b.Seed);
        for (int i = 0; i <= 255; i++)
        {
            var quadro = b.Next(50, 1000, 30);
            // O índice vai cifrado no fio: o quadro tem de bater com o quadro
            // embaralhado para aquele índice.
            Assert.Equal(KalkanProtocol.BuildWireFrame(0x2A, (byte)i, 50, 1000, 30), quadro);
        }
        Assert.Equal(0, b.NextIndex);
        b.Next(50, 1000, 30);
        Assert.Equal(1, b.NextIndex);
    }

    [Fact]
    public void ConstrutorDeQuadrosGeraOsVetoresDouradosNaOrdem()
    {
        var b = new KalkanFrameBuilder(0x2A);
        var primeiro = b.Next(45, 1500, 20);
        var segundo = b.Next(45, 1500, 20);
        Assert.Equal(VetoresDourados[4].Hex, KalkanProtocol.ToHex(primeiro));
        Assert.Equal(VetoresDourados[5].Hex, KalkanProtocol.ToHex(segundo));
        Assert.Equal(0x2A, b.Seed);
    }

    [Fact]
    public void QuadroVazioNaoQuebraOEmbaralhador()
    {
        Assert.Empty(KalkanProtocol.Scramble(0x2A, Array.Empty<byte>()));
        Assert.Equal(new byte[] { 1, 2 }, KalkanProtocol.Scramble(0x2A, new byte[] { 1, 2 }));
    }
}
