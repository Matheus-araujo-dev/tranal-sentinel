namespace TranalSentinel;

/// <summary>
/// Tempos de memória (timings) de um perfil: o padrão JEDEC ou um perfil XMP/EXPO.
/// </summary>
/// <remarks>
/// <para>
/// Os tempos do SPD são contadores de MTB (Medium Time Base) de 0,125 ns. O
/// campo <c>tCKAVGmin</c> do JEDEC (0x12-0x13) é um contador de 0,25 ns.
/// A taxa de transferência é 2 / tCK, porque a memória transfere duas vezes
/// por ciclo de clock.
/// </para>
/// <para>
/// O modelo foi validado contra um módulo real Asgard 16 GB DDR4-3200 lido
/// pela sonda PawnIO: JEDEC = 2400 MT/s com CL 16, e o perfil XMP 2.0 =
/// 3200 MT/s com CL 18 e tRAS 42 — os mesmos números que o CPU-Z mostra.
/// </para>
/// </remarks>
public sealed class SpdTiming
{
    /// <summary>Taxa de transferência em megatransferências por segundo (2400, 3200...).</summary>
    public int VelocidadeMts { get; init; }

    /// <summary>Período do clock de memória em picossegundos.</summary>
    public double TckPs { get; init; }

    /// <summary>Latência CAS, em ciclos.</summary>
    public int Cl { get; init; }

    /// <summary>tRCD (RAS to CAS delay), em ciclos.</summary>
    public int Trcd { get; init; }

    /// <summary>tRP (row precharge), em ciclos.</summary>
    public int Trp { get; init; }

    /// <summary>tRAS (active to precharge), em ciclos.</summary>
    public int Tras { get; init; }

    /// <summary>tRC (row cycle), em ciclos.</summary>
    public int Trc { get; init; }

    public double TaaNs { get; init; }
    public double TrcdNs { get; init; }
    public double TrpNs { get; init; }
    public double TrasNs { get; init; }
    public double TrcNs { get; init; }
    public double Trfc1Ns { get; init; }
    public double Trfc2Ns { get; init; }
    public double Trfc4Ns { get; init; }
    public double TfawNs { get; init; }
    public double TrrdSNs { get; init; }
    public double TrrdLNs { get; init; }
    public double TccdLNs { get; init; }
    public double TwrNs { get; init; }
    public double TwtrSNs { get; init; }
    public double TwtrLNs { get; init; }

    /// <summary>Resumo no formato do CPU-Z: CL-tRCD-tRP-tRAS, em ciclos.</summary>
    public string ResumoCiclos => $"{Cl}-{Trcd}-{Trp}-{Tras}";

    /// <summary>Texto curto dos principais tempos, em nanossegundos.</summary>
    public string ResumoNs =>
        $"CL {Cl} · tRCD {TrcdNs:0.##} ns · tRP {TrpNs:0.##} ns · tRAS {TrasNs:0.##} ns · tRC {TrcNs:0.##} ns";
}

/// <summary>Um perfil de overclock gravado no módulo (XMP 2.0 ou EXPO).</summary>
public sealed class SpdPerfil
{
    /// <summary>Nome do perfil: "XMP 2.0", "EXPO" etc.</summary>
    public string Nome { get; init; } = "";

    /// <summary>Perfil está marcado como habilitado no cabeçalho do SPD.</summary>
    public bool Habilitado { get; init; }

    /// <summary>Tensão do perfil, em volt.</summary>
    public double Voltagem { get; init; }

    /// <summary>Tempos do perfil.</summary>
    public SpdTiming Timing { get; init; } = new();
}

/// <summary>
/// O que o SPD conta sobre um módulo de memória: identidade, capacidade,
/// tempos do padrão JEDEC e os perfis de overclock gravados.
/// </summary>
public sealed class SpdModulo
{
    /// <summary>Tipo de memória ("DDR4", "DDR5"...).</summary>
    public string Tipo { get; init; } = "";

    /// <summary>Capacidade em bytes, calculada a partir do SPD.</summary>
    public long CapacidadeBytes { get; init; }

    /// <summary>Número de ranks do módulo.</summary>
    public int Ranks { get; init; }

    /// <summary>Largura de barramento em bits (64 para DDR4 padrão).</summary>
    public int LarguraBits { get; init; }

    /// <summary>Largura do chip de memória ("x8", "x16"...).</summary>
    public string LarguraChip { get; init; } = "";

    /// <summary>Tipo do módulo ("UDIMM", "SO-DIMM"...).</summary>
    public string TipoModulo { get; init; } = "";

    /// <summary>Tensão nominal do módulo (byte 0x0B do SPD), em volt.</summary>
    public double TensaoNominal { get; init; }

    /// <summary>Fabricante do módulo (código de fabricante JEDEC).</summary>
    public string Fabricante { get; init; } = "";

    /// <summary>Local de fabricação.</summary>
    public string Localizacao { get; init; } = "";

    /// <summary>Ano e semana de fabricação, em texto.</summary>
    public string FabricadoEm { get; init; } = "";

    /// <summary>Número de série do módulo.</summary>
    public string Serial { get; init; } = "";

    /// <summary>Part number do módulo.</summary>
    public string PartNumber { get; init; } = "";

    /// <summary>Fabricante do chip de memória (código JEDEC).</summary>
    public string FabricanteDram { get; init; } = "";

    /// <summary>Tempos do padrão JEDEC (o que o módulo garante sem overclock).</summary>
    public SpdTiming Jedec { get; init; } = new();

    /// <summary>Perfis XMP/EXPO encontrados (vazio quando o módulo não tem nenhum).</summary>
    public List<SpdPerfil> Perfis { get; init; } = new();

    /// <summary>Perfil mais alto que o módulo oferece (o primeiro da lista).</summary>
    public SpdPerfil? MelhorPerfil => Perfis.Count > 0 ? Perfis[0] : null;

    public double Gigabytes => Math.Round(CapacidadeBytes / 1024.0 / 1024.0 / 1024.0, 1);
}

/// <summary>
/// Decodifica o SPD (Serial Presence Detect) de um módulo de memória.
/// </summary>
/// <remarks>
/// <para>
/// O SPD são os 512 bytes que o módulo de memória usa para se descrever:
/// fabricante, capacidade, organização e os tempos (timings) do padrão JEDEC
/// e dos perfis XMP/EXPO. É a mesma informação que o CPU-Z mostra na aba SPD
/// e o HWiNFO na aba de memória.
/// </para>
/// <para>
/// Esta classe é pura: recebe os bytes e devolve o modelo. Não fala com o
/// hardware — quem busca os bytes é <see cref="SpdLeitor"/>.
/// </para>
/// </remarks>
public static class DecodificadorSpd
{
    // ---- offsets da página JEDEC (0x000-0x17F) ----
    // Mapa conferido contra as folhas de SPD oficiais da Samsung (M391A1K43BB1-CRC00
    // e M378A2K43CB1-CPB00): os mesmos bytes, os mesmos valores.
    internal const int OffTipoDram = 0x02;
    internal const int OffTipoModulo = 0x03;
    internal const int OffDensidade = 0x04;
    internal const int OffEnderecamento = 0x05;
    internal const int OffTipoDramDispositivo = 0x06;
    internal const int OffOrganizacaoPrimaria = 0x06;
    internal const int OffTensaoNominal = 0x0B;
    internal const int OffOrganizacaoModulo = 0x0C;
    internal const int OffLarguraBarramento = 0x0D;
    internal const int OffTckMin = 0x12;
    internal const int OffTckMax = 0x13;
    internal const int OffTaa = 0x18;
    internal const int OffRcd = 0x19;
    internal const int OffRp = 0x1A;
    internal const int OffNibbleRasRc = 0x1B;
    internal const int OffRas = 0x1C;
    internal const int OffRc = 0x1D;
    internal const int OffRfc1 = 0x1E;
    internal const int OffRfc2 = 0x20;
    internal const int OffRfc4 = 0x22;
    internal const int OffNibbleFaw = 0x24;
    internal const int OffFaw = 0x25;
    internal const int OffRrdS = 0x26;
    internal const int OffRrdL = 0x27;
    internal const int OffCcdL = 0x28;
    internal const int OffNibbleWr = 0x29;
    internal const int OffWr = 0x2A;
    internal const int OffNibbleWtr = 0x2B;
    internal const int OffWtrS = 0x2C;
    internal const int OffWtrL = 0x2D;
    internal const int OffFtb = 0x75;
    internal const int OffFabricante = 0x140;
    internal const int OffLocalizacao = 0x142;
    internal const int OffAno = 0x143;
    internal const int OffSemana = 0x144;
    internal const int OffSerial = 0x145;
    internal const int OffPartNumber = 0x149;
    internal const int OffFabricanteDram = 0x15E;

    // ---- cabeçalho e perfis XMP 2.0 (página 1) ----
    internal const int OffXmpCabecalho = 0x180;
    internal const int OffXmpPerfil = 0x189;
    internal const int TamanhoXmpPerfil = 0x2F;

    /// <summary>MTB (Medium Time Base): 0,125 ns por contador.</summary>
    internal const double MtbPs = 125.0;

    /// <summary>
    /// O campo tCKAVGmin do JEDEC (0x12) e o tCKAVGmax (0x13) tem um byte
    /// cada, na mesma grade de 0,125 ns dos outros tempos.
    /// </summary>
    internal const double TckJedecPs = MtbPs;

    private static readonly int[] DensidadeBits = { 256, 512, 1024, 2048, 4096, 8192, 16384, 32768, 12288, 24576 };
    private static readonly int[] LarguraChipBits = { 4, 8, 16, 32 };

    /// <summary>Nomes dos tipos de memória reconhecidos pelo código JEDEC.</summary>
    private static string NomeTipo(byte codigo) => codigo switch
    {
        0x07 => "SDR",
        0x08 => "DDR",
        0x09 => "DDR2",
        0x0B => "DDR3",
        0x0C => "DDR4",
        0x12 => "DDR5",
        _ => $"desconhecido (0x{codigo:X2})"
    };

    /// <summary>Nomes dos tipos de módulo (byte 0x03 do DDR4).</summary>
    private static string NomeTipoModulo(byte codigo) => codigo switch
    {
        1 => "RDIMM",
        2 => "UDIMM",
        3 => "LRDIMM",
        4 => "SODIMM",
        _ => $"tipo 0x{codigo:X2}"
    };

    /// <summary>Velocidades padrão de DDR4, em megatransferências por segundo.</summary>
    private static readonly int[] VelocidadesPadrao =
        { 1600, 1866, 2133, 2400, 2666, 2933, 3200, 3466, 3600, 3733, 4000, 4266, 4800, 5600, 6400 };

    /// <summary>
    /// Converte o período do clock de memória na taxa de transferência.
    ///
    /// <para>
    /// O tCK do JEDEC é um tempo mínimo, arredondado para cima na grade de
    /// 0,125 ns: um módulo DDR4-2400 declara 7 (875 ps) quando o nominal é
    /// 833 ps. Por isso a velocidade nominal é a menor velocidade padrão que
    /// fica acima do valor lido — é assim que o módulo se apresenta como
    /// "DDR4-2400" e não "2286".
    /// </para>
    /// </summary>
    private static int VelocidadeMts(double tckPs)
    {
        if (tckPs <= 0) return 0;

        double bruta = 2_000_000.0 / tckPs;

        // Caso o valor lido esteja praticamente em cima de um padrão, ele vence.
        foreach (int padrao in VelocidadesPadrao)
        {
            if (Math.Abs(bruta - padrao) <= padrao * 0.02) return padrao;
        }

        // Senão: o menor padrão que cobre o valor lido.
        foreach (int padrao in VelocidadesPadrao)
        {
            if (padrao >= bruta) return padrao;
        }

        return (int)Math.Round(bruta);
    }

    /// <summary>
    /// Tensão nominal do módulo (byte 0x0B): passos de 50 mV a partir de
    /// 1,05 V. O bit 7 indica a tensão mais alta (1,25 V ou mais).
    /// </summary>
    private static double LerTensaoNominal(byte bruto)
    {
        if ((bruto & 0x80) != 0) return 1.25 + (bruto & 0x7F) * 0.05;
        return 1.05 + (bruto & 0x7F) * 0.05;
    }

    /// <summary>
    /// Decodifica um SPD completo (512 bytes: página 0 + página 1).
    /// </summary>
    public static SpdModulo Decodificar(byte[] spd)
    {
        ArgumentNullException.ThrowIfNull(spd);

        if (spd.Length < 0x1A0)
            return new SpdModulo { Tipo = "SPD incompleto" };

        byte tipo = spd[OffTipoDram];
        if (tipo != 0x0C)
            return new SpdModulo { Tipo = NomeTipo(tipo) };

        // --- capacidade ---
        byte densidade = spd[OffDensidade];
        int codigoDensidade = densidade & 0x0F;
        int bitsPorDie = codigoDensidade < DensidadeBits.Length ? DensidadeBits[codigoDensidade] : 0;

        byte organizacao = spd[OffOrganizacaoModulo];
        int larguraChip = LarguraChipBits.Length > (organizacao & 0x07) ? LarguraChipBits[organizacao & 0x07] : 0;
        int ranks = ((organizacao >> 3) & 0x07) + 1;
        bool rankAssimetrico = (organizacao & 0x40) != 0;

        byte largura = spd[OffLarguraBarramento];
        int larguraBits = largura switch { 0 => 8, 1 => 16, 2 => 32, 3 => 64, _ => 0 };

        int dies = ((spd[OffOrganizacaoPrimaria] >> 4) & 0x07);
        if (dies == 0) dies = 1;

        long capacidade = 0;
        if (bitsPorDie > 0 && larguraChip > 0 && larguraBits > 0)
        {
            long chipsPorRank = larguraBits / larguraChip;
            long bytesPorDie = bitsPorDie * 1024L * 1024L / 8L;
            capacidade = bytesPorDie * dies * chipsPorRank * ranks;
            if (rankAssimetrico) capacidade /= 2;
        }

        var jedec = LerJedec(spd);

        var modulo = new SpdModulo
        {
            Tipo = NomeTipo(tipo),
            CapacidadeBytes = capacidade,
            Ranks = ranks,
            LarguraBits = larguraBits,
            LarguraChip = larguraChip > 0 ? $"x{larguraChip}" : "",
            TipoModulo = NomeTipoModulo(spd[OffTipoModulo]),
            TensaoNominal = LerTensaoNominal(spd[OffTensaoNominal]),
            Fabricante = LerTextoOuHex(spd, OffFabricante, 2),
            Localizacao = LerTexto(spd, OffLocalizacao, 1),
            FabricadoEm = $"{spd[OffAno]:X2}/{spd[OffSemana]:X2}",
            Serial = LerTextoOuHex(spd, OffSerial, 4),
            PartNumber = LerTexto(spd, OffPartNumber, 20),
            FabricanteDram = LerTextoOuHex(spd, OffFabricanteDram, 2),
            Jedec = jedec,
            Perfis = LerPerfis(spd)
        };

        return modulo;
    }

    /// <summary>Lê os tempos do padrão JEDEC (página 0).</summary>
    private static SpdTiming LerJedec(byte[] spd)
    {
        double tckPs = spd[OffTckMin] * TckJedecPs;
        if (tckPs <= 0) tckPs = 0;

        double taaPs = Contador(spd, OffTaa) * MtbPs;
        double trcdPs = Contador(spd, OffRcd) * MtbPs;
        double trpPs = Contador(spd, OffRp) * MtbPs;
        double trasPs = LerComNibble(spd, OffNibbleRasRc, OffRas, 0x0F) * MtbPs;
        double trcPs = LerComNibble(spd, OffNibbleRasRc, OffRc, 0xF0) * MtbPs;

        return MontarTiming(
            tckPs,
            taaPs, trcdPs, trpPs, trasPs, trcPs,
            Contador16(spd, OffRfc1) * MtbPs,
            Contador16(spd, OffRfc2) * MtbPs,
            Contador16(spd, OffRfc4) * MtbPs,
            LerComNibble(spd, OffNibbleFaw, OffFaw, 0x0F) * MtbPs,
            Contador(spd, OffRrdS) * MtbPs,
            Contador(spd, OffRrdL) * MtbPs,
            Contador(spd, OffCcdL) * MtbPs,
            LerComNibble(spd, OffNibbleWr, OffWr, 0x0F) * MtbPs,
            LerComNibble(spd, OffNibbleWtr, OffWtrS, 0x0F) * MtbPs,
            Contador(spd, OffWtrL) * MtbPs);
    }

    /// <summary>Lê os perfis XMP 2.0 (página 1).</summary>
    private static List<SpdPerfil> LerPerfis(byte[] spd)
    {
        var perfis = new List<SpdPerfil>();
        if (spd.Length < OffXmpPerfil + TamanhoXmpPerfil) return perfis;

        // Cabeçalho XMP 2.0: 0x0C 0x4A, byte 0x182 = perfis habilitados, 0x183 = versão.
        if (spd[OffXmpCabecalho] != 0x0C || spd[OffXmpCabecalho + 1] != 0x4A) return perfis;

        byte habilitados = spd[OffXmpCabecalho + 2];
        byte versao = spd[OffXmpCabecalho + 3];
        string nome = $"XMP {versao >> 4}.{versao & 0x0F}";

        for (int slot = 0; slot < 2; slot++)
        {
            int baseOffset = OffXmpPerfil + (slot * TamanhoXmpPerfil);
            if (spd.Length < baseOffset + TamanhoXmpPerfil) break;

            // Perfil sem tempo de ciclo não é um perfil válido.
            byte ciclo = spd[baseOffset + 0x03];
            if (ciclo == 0) continue;

            double tckPs = ciclo * MtbPs;
            byte nibble = spd[baseOffset + 0x0B];
            double taaPs = spd[baseOffset + 0x08] * MtbPs;
            double trcdPs = spd[baseOffset + 0x09] * MtbPs;
            double trpPs = spd[baseOffset + 0x0A] * MtbPs;
            double trasPs = (((nibble & 0x0F) << 8) | spd[baseOffset + 0x0C]) * MtbPs;
            double trcPs = (((nibble & 0xF0) << 4) | spd[baseOffset + 0x0D]) * MtbPs;

            var timing = MontarTiming(
                tckPs,
                taaPs, trcdPs, trpPs, trasPs, trcPs,
                Contador16(spd, baseOffset + 0x0E) * MtbPs,
                Contador16(spd, baseOffset + 0x10) * MtbPs,
                Contador16(spd, baseOffset + 0x12) * MtbPs,
                (((spd[baseOffset + 0x14] & 0x0F) << 8) | spd[baseOffset + 0x15]) * MtbPs,
                spd[baseOffset + 0x16] * MtbPs,
                spd[baseOffset + 0x17] * MtbPs,
                0,
                0,
                0,
                0);

            perfis.Add(new SpdPerfil
            {
                Nome = slot == 0 ? nome : $"{nome} (perfil 2)",
                Habilitado = (habilitados & (1 << slot)) != 0,
                Voltagem = LerVoltagemXmp(spd[baseOffset]),
                Timing = timing
            });
        }

        return perfis;
    }

    /// <summary>
    /// Tensão do perfil XMP: o bit 7 diz que a tensão está declarada e os
    /// outros 7 bits são a parte fracionária em centésimos de volt
    /// (0xA3 = 1,00 V + 0,35 V = 1,35 V, a tensão típica de um perfil
    /// DDR4-3200).
    /// </summary>
    private static double LerVoltagemXmp(byte bruto)
    {
        if ((bruto & 0x80) == 0) return 0;
        return 1.0 + (bruto & 0x7F) * 0.01;
    }

    private static SpdTiming MontarTiming(
        double tckPs, double taaPs, double trcdPs, double trpPs, double trasPs, double trcPs,
        double trfc1Ps, double trfc2Ps, double trfc4Ps, double tfawPs, double trrdSPs, double trrdLPs,
        double tccdLPs, double twrPs, double twtrSPs, double twtrLPs)
    {
        int velocidade = VelocidadeMts(tckPs);

        return new SpdTiming
        {
            VelocidadeMts = velocidade,
            TckPs = tckPs,
            Cl = Ciclos(taaPs, tckPs),
            Trcd = Ciclos(trcdPs, tckPs),
            Trp = Ciclos(trpPs, tckPs),
            Tras = Ciclos(trasPs, tckPs),
            Trc = Ciclos(trcPs, tckPs),
            TaaNs = taaPs / 1000.0,
            TrcdNs = trcdPs / 1000.0,
            TrpNs = trpPs / 1000.0,
            TrasNs = trasPs / 1000.0,
            TrcNs = trcPs / 1000.0,
            Trfc1Ns = trfc1Ps / 1000.0,
            Trfc2Ns = trfc2Ps / 1000.0,
            Trfc4Ns = trfc4Ps / 1000.0,
            TfawNs = tfawPs / 1000.0,
            TrrdSNs = trrdSPs / 1000.0,
            TrrdLNs = trrdLPs / 1000.0,
            TccdLNs = tccdLPs / 1000.0,
            TwrNs = twrPs / 1000.0,
            TwtrSNs = twtrSPs / 1000.0,
            TwtrLNs = twtrLPs / 1000.0
        };
    }

    private static int Ciclos(double ps, double tckPs) =>
        tckPs > 0 ? (int)Math.Round(ps / tckPs, MidpointRounding.AwayFromZero) : 0;

    /// <summary>Contador de 1 byte.</summary>
    private static double Contador(byte[] spd, int offset) => spd[offset];

    /// <summary>Contador de 2 bytes (little endian).</summary>
    private static double Contador16(byte[] spd, int offset) => spd[offset] | (spd[offset + 1] << 8);

    /// <summary>
    /// Valor de um tempo que usa nibble no alto + byte: o nibble vem do byte
    /// <paramref name="offsetNibble"/> e o byte de <paramref name="offset"/>.
    /// </summary>
    private static double LerComNibble(byte[] spd, int offsetNibble, int offset, int mascaraNibble)
    {
        int nibble = (spd[offsetNibble] & mascaraNibble);
        if (mascaraNibble == 0x0F) return ((nibble << 8) | spd[offset]);
        return ((nibble << 4) | spd[offset]);
    }

    /// <summary>
    /// Lê um campo de texto. Alguns campos do SPD são binários e não ASCII
    /// (o número de série, o identificador do fabricante): nesse caso vem em
    /// hexadecimal, que é como o fabricante imprime na etiqueta do módulo.
    /// A mesma regra vale para todo campo de texto.
    /// </summary>
    private static string LerTextoOuHex(byte[] spd, int offset, int tamanho)
    {
        bool imprimivel = true;
        for (int i = 0; i < tamanho && offset + i < spd.Length; i++)
        {
            byte b = spd[offset + i];
            if (b != 0 && (b < 0x20 || b >= 0x7F))
            {
                imprimivel = false;
                break;
            }
        }

        if (imprimivel) return LerTexto(spd, offset, tamanho);

        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < tamanho && offset + i < spd.Length; i++)
            sb.Append(spd[offset + i].ToString("X2"));

        return sb.ToString();
    }

    /// <summary>Lê um texto ASCII de tamanho fixo, cortando no primeiro zero.</summary>
    private static string LerTexto(byte[] spd, int offset, int tamanho)
    {
        if (offset + tamanho > spd.Length) return "";

        int fim = tamanho;
        for (int i = 0; i < tamanho; i++)
        {
            if (spd[offset + i] == 0) { fim = i; break; }
        }

        var sb = new System.Text.StringBuilder(fim);
        for (int i = 0; i < fim; i++)
        {
            byte b = spd[offset + i];
            sb.Append(b >= 0x20 && b < 0x7F ? (char)b : '.');
        }

        return sb.ToString().Trim();
    }
}
