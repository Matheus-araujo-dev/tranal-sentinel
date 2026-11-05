namespace TranalSentinel;

/// <summary>
/// Resultado da leitura dos módulos de memória.
/// </summary>
public sealed class MemoriaColetaResultado
{
    /// <summary>True quando o SMBus respondeu e os módulos foram lidos.</summary>
    public bool Disponivel { get; init; }

    /// <summary>Motivo da falha (quando <see cref="Disponivel"/> é false).</summary>
    public string? Erro { get; init; }

    /// <summary>Tipo de barramento SMBus encontrado.</summary>
    public SmbusModelo? Barramento { get; init; }

    /// <summary>Módulos encontrados.</summary>
    public List<MemoriaSpd> Modulos { get; init; } = new();

    /// <summary>Clock de memória configurado na BIOS, em megatransferências por segundo.</summary>
    public int? VelocidadeConfigMts { get; init; }

    /// <summary>Clock máximo que o Windows informa para os módulos, em megatransferências por segundo.</summary>
    public int? VelocidadeMaximaMts { get; init; }

    /// <summary>Capacidade total, em bytes.</summary>
    public long CapacidadeTotalBytes => Modulos.Sum(m => m.Modulo.CapacidadeBytes);
}

/// <summary>
/// Abre o SMBus do processador e lê o SPD de todos os módulos de memória.
/// </summary>
/// <remarks>
/// <para>
/// A leitura é somente leitura: o módulo PawnIO só conversa com o barramento
/// SMBus e nada do conteúdo gravado no DIMM é alterado. A única escrita é a
/// troca de página do hub SPD, que é estado volátil do próprio hub.
/// </para>
/// <para>
/// O resultado fica em cache: ler o SPD de todos os módulos leva cerca de um
/// segundo e o resultado não muda entre duas análises.
/// </para>
/// </remarks>
public static class MemoriaColeta
{
    private static readonly object Trinco = new();
    private static MemoriaColetaResultado? _cache;

    /// <summary>Lê os módulos de memória (com cache por processo).</summary>
    public static MemoriaColetaResultado Coletar(bool forcar = false)
    {
        lock (Trinco)
        {
            if (!forcar && _cache != null) return _cache;

            MemoriaColetaResultado resultado = Ler();
            _cache = resultado;
            return resultado;
        }
    }

    private static MemoriaColetaResultado Ler()
    {
        byte[]? moduloAmd = ModulosPawnIo.Obter("SmbusPIIX4.bin", ModulosPawnIo.RecursoModuloAmd);
        byte[]? moduloIntel = ModulosPawnIo.Obter("SmbusI801.bin", ModulosPawnIo.RecursoModuloIntel);

        if (moduloAmd == null && moduloIntel == null)
        {
            return new MemoriaColetaResultado
            {
                Erro = "Os módulos PawnIO não estão embutidos nesta instalação."
            };
        }

        SmbusPawnIo? smbus = SmbusPawnIo.Abrir(moduloAmd, moduloIntel, out string? erro);
        if (smbus == null)
        {
            return new MemoriaColetaResultado { Erro = erro };
        }

        try
        {
            var modulos = SpdLeitor.ProcurarModulos(smbus);
            if (modulos.Count == 0)
            {
                return new MemoriaColetaResultado
                {
                    Barramento = smbus.Modelo,
                    Erro = "O barramento SMBus respondeu, mas nenhum módulo de memória foi encontrado."
                };
            }

            return new MemoriaColetaResultado
            {
                Disponivel = true,
                Barramento = smbus.Modelo,
                Modulos = modulos,
                VelocidadeConfigMts = VelocidadeConfigMts(),
                VelocidadeMaximaMts = VelocidadeMaximaMts()
            };
        }
        finally
        {
            smbus.Dispose();
        }
    }

    /// <summary>
    /// Clock de memória que a BIOS configurou (o que o Windows chama de
    /// "velocidade configurada"), em megatransferências por segundo.
    /// </summary>
    private static int? VelocidadeConfigMts()
    {
        try
        {
            int maior = 0;
            foreach (LinhaWmi linha in Wmi.Consultar("SELECT ConfiguredClockSpeed FROM Win32_PhysicalMemory"))
            {
                int valor = (int)linha.ValorLong("ConfiguredClockSpeed");
                if (valor > maior) maior = valor;
            }

            return maior > 0 ? maior : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Clock máximo que o Windows informa para os módulos.</summary>
    private static int? VelocidadeMaximaMts()
    {
        try
        {
            int maior = 0;
            foreach (LinhaWmi linha in Wmi.Consultar("SELECT Speed FROM Win32_PhysicalMemory"))
            {
                int valor = (int)linha.ValorLong("Speed");
                if (valor > maior) maior = valor;
            }

            return maior > 0 ? maior : null;
        }
        catch
        {
            return null;
        }
    }
}
