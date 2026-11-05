namespace TranalSentinel;

/// <summary>
/// Protocolo do visor Kalkan SmartMonitor (firmware 1.2 — linha Alpha Rev.1).
///
/// Reconstruído a partir da análise estática do binário oficial
/// "Kalkan PC SmartMonitor DZ V1.2.exe" (classe HidDeviceController) e
/// conferido contra os vetores de teste publicados no projeto aberto
/// kalkan-monitor-rs (MIT).
///
/// A conversa acontece em USB HID (VID 0416 / PID 1100):
///   1. Handshake: o PC escreve 64 bytes <c>01 81 E3 00…</c> e lê a resposta;
///      o byte seguinte ao marcador <c>0xE3</c> é a semente do embaralhador.
///   2. Quadro de dados: 64 bytes
///      <c>01 86 E0 idx temp fanHi fanLo cpuLoad 00…</c> (sem checksum).
///   3. Embaralhador: os bytes 0–2 saem literais; do byte 3 em diante cada byte
///      é embaralhado bit a bit e a semente evolui dentro do quadro.
///
/// Tudo aqui é função pura: nada de hardware, dá para testar sem o visor.
/// </summary>
public static class KalkanProtocol
{
    /// <summary>Tamanho do relatório HID de saída (o byte 0 é o report ID).</summary>
    public const int ReportLen = 64;

    /// <summary>Tamanho da leitura usada pelo app oficial (65 bytes).</summary>
    public const int ReadLen = 65;

    /// <summary>Marcador que aponta a posição da semente na resposta do handshake.</summary>
    public const byte SeedMarker = 0xE3;

    /// <summary>Comando "mostrar informações" do quadro de dados.</summary>
    public const byte ShowInfo = 0xE0;

    /// <summary>VID do visor (chip AP8510) na linha Alpha Rev.1.</summary>
    public const ushort VendorId = 0x0416;

    /// <summary>PID do visor (chip AP8510) na linha Alpha Rev.1.</summary>
    public const ushort ProductId = 0x1100;

    /// <summary>Cabeçalho literal do quadro de dados (não vai pelo embaralhador).</summary>
    private static readonly byte[] Cabecalho = { 0x01, 0x86, 0xE0 };

    /// <summary>
    /// Pedido de semente: 64 bytes zerados com <c>[0]=01 [1]=81 [2]=E3</c>.
    /// Equivale ao GenerateScramblerSeed do app oficial.
    /// </summary>
    public static byte[] BuildSeedRequest()
    {
        var buf = new byte[ReportLen];
        buf[0] = 0x01;
        buf[1] = 0x81;
        buf[2] = SeedMarker;
        return buf;
    }

    /// <summary>
    /// Extrai a semente da resposta do handshake.
    /// O marcador <c>0xE3</c> pode vir no índice 0 (semente no 1) ou no índice 1
    /// (semente no 2) — como na resposta real, que traz o report ID na frente:
    /// <c>01 E3 23 00…</c> → semente <c>0x23</c>.
    /// </summary>
    public static bool TryParseSeed(ReadOnlySpan<byte> resposta, out byte seed)
    {
        seed = 0;
        if (resposta.Length > 1 && resposta[0] == SeedMarker)
        {
            seed = resposta[1];
            return true;
        }
        if (resposta.Length > 2 && resposta[1] == SeedMarker)
        {
            seed = resposta[2];
            return true;
        }
        return false;
    }

    /// <summary>
    /// Monta o quadro bruto (antes do embaralhador):
    /// <c>[0]=01 [1]=86 [2]=E0 [3]=idx [4]=temp [5]=fanHi [6]=fanLo [7]=cargaCpu</c>,
    /// com o resto zerado.
    /// </summary>
    public static byte[] BuildFrame(byte indice, byte temperatura, ushort fan, byte cargaCpu)
    {
        var f = new byte[ReportLen];
        f[0] = Cabecalho[0];
        f[1] = Cabecalho[1];
        f[2] = Cabecalho[2];
        f[3] = indice;
        f[4] = temperatura;
        f[5] = (byte)(fan >> 8);
        f[6] = (byte)(fan & 0xFF);
        f[7] = cargaCpu;
        return f;
    }

    /// <summary>
    /// Embaralha um quadro com a semente do handshake.
    /// Os 3 primeiros bytes saem literais; do byte 3 em diante cada byte é
    /// embaralhado bit a bit e a semente evolui (<c>semente = semente*2 + (v&gt;&gt;6)</c>)
    /// carregando de um byte para o outro.
    ///
    /// A semente entra <b>por valor</b>: todo quadro recomeça da semente do
    /// handshake (igual ao app oficial), então o chamador nunca precisa
    /// guardar o estado do embaralhador.
    /// </summary>
    public static byte[] Scramble(byte seed, ReadOnlySpan<byte> origem)
    {
        var saida = new byte[origem.Length];
        int copia = Math.Min(3, origem.Length);
        origem[..copia].CopyTo(saida);
        if (origem.Length <= 3) return saida;

        byte semente = seed;
        for (int pos = 3; pos < origem.Length; pos++)
        {
            byte v0 = origem[pos];
            byte acc = 0;
            for (int bit = 0; bit < 8; bit++)
            {
                // Toda a aritmética é de 8 bits com estouro (equivale ao wrapping_* do Rust).
                byte v = (byte)(((v0 << bit) & 0x80) + ((semente & 0xF0) << 3) + (semente & 0x80));
                acc = (byte)(acc + (v >> bit));
                semente = (byte)(semente * 2 + (v >> 6));
            }
            saida[pos] = acc;
        }
        return saida;
    }

    /// <summary>Quadro pronto para o fio (montado e embaralhado).</summary>
    public static byte[] BuildWireFrame(byte seed, byte indice, byte temperatura, ushort fan, byte cargaCpu)
        => Scramble(seed, BuildFrame(indice, temperatura, fan, cargaCpu));

    /// <summary>Formato hexadecimal minúsculo, sem separadores (para log e teste).</summary>
    public static string ToHex(ReadOnlySpan<byte> dados)
    {
        var sb = new System.Text.StringBuilder(dados.Length * 2);
        foreach (byte b in dados) sb.Append(b.ToString("x2"));
        return sb.ToString();
    }
}

/// <summary>
/// Sessão de envio para o visor: guarda a semente do handshake e o índice do
/// quadro. O índice começa em 0 a cada nova sessão e dá a volta em 256 —
/// o app oficial mantém o contador entre aberturas, mas o visor não se
/// importa com o valor inicial.
/// </summary>
public sealed class KalkanFrameBuilder
{
    private byte _indice;

    public KalkanFrameBuilder(byte seed) => Seed = seed;

    /// <summary>Semente vinda do handshake.</summary>
    public byte Seed { get; }

    /// <summary>Índice que o próximo quadro vai carregar.</summary>
    public byte NextIndex => _indice;

    /// <summary>
    /// Devolve o próximo quadro já embaralhado e avança o índice.
    /// </summary>
    public byte[] Next(byte temperatura, ushort fan, byte cargaCpu)
    {
        var wire = KalkanProtocol.BuildWireFrame(Seed, _indice, temperatura, fan, cargaCpu);
        _indice = (byte)(_indice + 1);
        return wire;
    }
}
