using System;
using System.Threading;

namespace TranalSentinel;

/// <summary>Modelo do módulo PawnIO que sabe falar SMBus.</summary>
public enum SmbusModelo
{
    /// <summary>Controlador SMBus do AMD FCH (AM4, Ryzen 1000-5000) — módulo SmbusPIIX4.</summary>
    AmdFch,

    /// <summary>Controlador SMBus da Intel (ICH/PCH) — módulo SmbusI801.</summary>
    IntelI801
}

/// <summary>
/// As transações de SMBus que o leitor de SPD precisa. Existe para que o
/// leitor possa ser testado sem driver e sem hardware.
/// </summary>
public interface ISmbusLeitor
{
    /// <summary>
    /// No AMD FCH existem dois barramentos (principal em 0xB00 e auxiliar em
    /// 0xB20). Este comando escolhe qual deles as próximas transações usam.
    /// </summary>
    bool SelecionarPorta(int porta);

    /// <summary>Lê um byte no endereço SMBus, no comando informado.</summary>
    bool LerByteData(int endereco, int comando, out byte valor);

    /// <summary>Lê dois bytes (word) no endereço SMBus, no comando informado.</summary>
    bool LerWordData(int endereco, int comando, out ushort valor);

    /// <summary>
    /// Escreve um byte. Usado só para escolher a página do hub SPD — não
    /// altera o conteúdo gravado no módulo de memória.
    /// </summary>
    bool EscreverByteData(int endereco, int comando, byte valor);
}

/// <summary>
/// Fala SMBus por cima do PawnIO: é a rota que o CPU-Z e o HWiNFO usam para
/// ler o SPD dos módulos de memória.
/// </summary>
/// <remarks>
/// <para>
/// As transações são as do SMBus padrão: endereço de 7 bits, comando e dados.
/// O módulo do kernel cuida do detalhe de baixo nível (portas de I/O do FCH
/// ou do PCH) e devolve o resultado em blocos de 8 bytes.
/// </para>
/// <para>
/// A seleção de porta do AMD FCH é estado global do módulo, então as
/// transações ficam atrás de um mutex nomeado para não atropelar outra
/// ferramenta que esteja lendo o mesmo barramento.
/// </para>
/// </remarks>
public sealed class SmbusPawnIo : ISmbusLeitor, IDisposable
{
    /// <summary>Nome do mutex que serializa o acesso ao SMBus.</summary>
    internal const string MutexSmbus = @"Global\TranalSentinel.Smbus";

    private const int Ler = 1;
    private const int Escrever = 0;
    private const int ProtocoloByteData = 2;
    private const int ProtocoloWordData = 3;

    private readonly IPawnIoTransporte _transporte;
    private readonly SmbusModelo _modelo;
    private readonly Mutex _mutex;
    private bool _descartado;

    private SmbusPawnIo(IPawnIoTransporte transporte, SmbusModelo modelo)
    {
        _transporte = transporte;
        _modelo = modelo;
        _mutex = new Mutex(false, MutexSmbus);
    }

    public SmbusModelo Modelo => _modelo;

    /// <summary>
    /// Tenta abrir o SMBus: primeiro o módulo do AMD, depois o da Intel.
    /// Devolve null quando nenhum dos dois funciona nesta máquina.
    /// </summary>
    public static SmbusPawnIo? Abrir(byte[] moduloAmd, byte[] moduloIntel, out string? erro)
    {
        erro = null;

        foreach ((SmbusModelo modelo, byte[] modulo) in new[]
                 {
                     (SmbusModelo.AmdFch, moduloAmd),
                     (SmbusModelo.IntelI801, moduloIntel)
                 })
        {
            if (modulo == null || modulo.Length == 0) continue;

            var transporte = new PawnIoTransporte();
            if (!transporte.Abrir())
            {
                transporte.Dispose();
                erro = "O driver PawnIO não está instalado neste computador.";
                return null;
            }

            if (!transporte.CarregarModulo(modulo))
            {
                // O módulo recusou: este hardware não é do tipo dele.
                transporte.Dispose();
                continue;
            }

            var smbus = new SmbusPawnIo(transporte, modelo);
            if (smbus.Identificar())
                return smbus;

            smbus.Dispose();
        }

        erro ??= "Nenhum controlador SMBus compatível foi encontrado (nem AMD FCH, nem Intel I801).";
        return null;
    }

    /// <summary>Confirma que o módulo responde: a função de identidade devolve o tipo e o endereço de I/O.</summary>
    public bool Identificar()
    {
        if (!_transporte.Executar("ioctl_identity", Array.Empty<long>(), 3, out long[] identidade))
            return false;

        return identidade.Length >= 3 && identidade[0] != 0;
    }

    /// <summary>
    /// No AMD FCH existem dois barramentos (principal em 0xB00 e auxiliar em
    /// 0xB20). Este comando escolhe qual deles as próximas transações usam.
    /// </summary>
    public bool SelecionarPorta(int porta)
    {
        if (_modelo != SmbusModelo.AmdFch) return true;
        return _transporte.Executar("ioctl_piix4_port_sel", new long[] { porta }, 1, out _);
    }

    /// <summary>Lê um byte no endereço SMBus, no comando informado.</summary>
    public bool LerByteData(int endereco, int comando, out byte valor)
    {
        valor = 0;
        if (!_transporte.Executar("ioctl_smbus_xfer",
                new long[] { endereco, Ler, comando, ProtocoloByteData }, 1, out long[] saida))
        {
            return false;
        }

        valor = (byte)(saida[0] & 0xFF);
        return true;
    }

    /// <summary>Lê dois bytes (word) no endereço SMBus, no comando informado.</summary>
    public bool LerWordData(int endereco, int comando, out ushort valor)
    {
        valor = 0;
        if (!_transporte.Executar("ioctl_smbus_xfer",
                new long[] { endereco, Ler, comando, ProtocoloWordData }, 1, out long[] saida))
        {
            return false;
        }

        valor = (ushort)(saida[0] & 0xFFFF);
        return true;
    }

    /// <summary>
    /// Escreve um byte. Usado só para escolher a página do hub SPD — não
    /// altera o conteúdo gravado no módulo de memória.
    /// </summary>
    public bool EscreverByteData(int endereco, int comando, byte valor)
    {
        return _transporte.Executar("ioctl_smbus_xfer",
            new long[] { endereco, Escrever, comando, ProtocoloByteData, valor }, 1, out _);
    }

    /// <summary>Executa uma ação protegida pelo mutex do SMBus.</summary>
    public T ComTrava<T>(Func<T> acao)
    {
        bool entrou = false;
        try
        {
            // O mutex pode ser abandonado por outra ferramenta: nesse caso
            // seguimos, porque o estado do barramento é reconectado a cada uso.
            entrou = _mutex.WaitOne(TimeSpan.FromSeconds(5));
            return acao();
        }
        finally
        {
            if (entrou) _mutex.ReleaseMutex();
        }
    }

    public void Dispose()
    {
        if (_descartado) return;
        _descartado = true;

        _mutex.Dispose();
        _transporte.Dispose();
    }
}
