namespace TranalSentinel;

/// <summary>
/// Lê o SPD dos módulos de memória pelo SMBus.
/// </summary>
/// <remarks>
/// <para>
/// O hub SPD de um módulo DDR4 guarda 512 bytes: duas páginas de 256.
/// A página 0 traz o padrão JEDEC (fabricante, capacidade, tempos); a
/// página 1 traz os perfis XMP/EXPO.
/// </para>
/// <para>
/// Trocar de página exige escrever no endereço de controle do hub (0x36/0x37).
/// Isso não altera o conteúdo gravado no módulo: é só o "qual página ler"
/// que o próprio hub mantém. O conteúdo do SPD continua intocado.
/// </para>
/// </remarks>
public static class SpdLeitor
{
    /// <summary>Endereço SMBus do controle de página do hub DDR4 (0x36 = página 0, 0x37 = página 1).</summary>
    public const int EnderecoControlePagina = 0x36;

    /// <summary>Primeiro endereço SMBus de módulo (A1).</summary>
    public const int PrimeiroEndereco = 0x50;

    /// <summary>Último endereço SMBus de módulo (A8).</summary>
    public const int UltimoEndereco = 0x57;

    /// <summary>Tamanho de uma página do SPD.</summary>
    public const int TamanhoPagina = 256;

    /// <summary>Total de bytes lidos de um módulo (página 0 + página 1).</summary>
    public const int TamanhoTotal = TamanhoPagina * 2;

    /// <summary>Valor gravado no controle de página para selecioná-la.</summary>
    private const byte ValorSelecionarPagina = 0xFF;

    /// <summary>
    /// Lê o SPD completo de um módulo. Devolve null quando não há módulo
    /// neste endereço do barramento.
    /// </summary>
    public static byte[]? LerModulo(ISmbusLeitor smbus, int endereco, int paginas = 2)
    {
        ArgumentNullException.ThrowIfNull(smbus);

        byte[] total = new byte[TamanhoPagina * paginas];

        for (int pagina = 0; pagina < paginas; pagina++)
        {
            if (!smbus.EscreverByteData(EnderecoControlePagina + pagina, 0x00, ValorSelecionarPagina))
                return null;

            for (int offset = 0; offset < TamanhoPagina; offset += 2)
            {
                if (!smbus.LerWordData(endereco, offset, out ushort palavra))
                    return null;

                total[(pagina * TamanhoPagina) + offset] = (byte)(palavra & 0xFF);
                if (offset + 1 < TamanhoPagina)
                    total[(pagina * TamanhoPagina) + offset + 1] = (byte)((palavra >> 8) & 0xFF);
            }
        }

        return total;
    }

    /// <summary>
    /// Procura módulos de memória em todos os endereços SMBus possíveis.
    /// </summary>
    public static List<MemoriaSpd> ProcurarModulos(SmbusPawnIo smbus, Action<string>? progresso = null)
    {
        ArgumentNullException.ThrowIfNull(smbus);

        var modulos = new List<MemoriaSpd>();

        // No AMD FCH o módulo pode estar no barramento principal ou no auxiliar.
        int[] portas = smbus.Modelo == SmbusModelo.AmdFch ? new[] { 0, 1 } : new[] { 0 };

        foreach (int porta in portas)
        {
            if (!smbus.SelecionarPorta(porta)) continue;

            for (int endereco = PrimeiroEndereco; endereco <= UltimoEndereco; endereco++)
            {
                progresso?.Invoke($"Lendo módulo em 0x{endereco:X2}…");

                byte[]? spd = smbus.ComTrava(() => LerModulo(smbus, endereco));
                if (spd == null) continue;

                // O byte 2 diz o tipo de memória: 0x0C é DDR4.
                if (spd.Length < 3 || spd[2] != 0x0C) continue;

                var modulo = DecodificadorSpd.Decodificar(spd);
                modulos.Add(new MemoriaSpd
                {
                    EnderecoSmbus = endereco,
                    Porta = porta,
                    Modulo = modulo
                });
            }
        }

        return modulos;
    }
}

/// <summary>Um módulo de memória lido do barramento SMBus.</summary>
public sealed class MemoriaSpd
{
    /// <summary>Endereço SMBus onde o módulo foi encontrado (0x50 a 0x57).</summary>
    public int EnderecoSmbus { get; init; }

    /// <summary>Porta do barramento (0 = principal, 1 = auxiliar no AMD FCH).</summary>
    public int Porta { get; init; }

    /// <summary>Módulo decodificado.</summary>
    public SpdModulo Modulo { get; init; } = new();
}
