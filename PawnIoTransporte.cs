using System;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace TranalSentinel;

/// <summary>
/// Acesso ao driver PawnIO. É ele que dá leitura do SMBus do processador,
/// de onde sai o SPD (o "cartão de visita" de 512 bytes) de cada módulo de memória.
/// </summary>
/// <remarks>
/// <para>
/// O PawnIO é um driver open source (MIT/LGPL) que expõe um pequeno protocolo
/// de IOCTLs: <c>LOAD_BINARY</c> carrega um módulo Pawn (código de kernel)
/// e <c>EXECUTE_FN</c> chama uma função exported por esse módulo.
/// </para>
/// <para>
/// Aqui só existe a mecânica bruta. Quem sabe falar SMBus é
/// <see cref="SmbusPawnIo"/>.
/// </para>
/// </remarks>
public interface IPawnIoTransporte : IDisposable
{
    /// <summary>
    /// Carrega um módulo Pawn no driver. Devolve false quando o driver
    /// recusa o módulo (normalmente porque o hardware que ele exige não
    /// existe nesta máquina).
    /// </summary>
    bool CarregarModulo(byte[] modulo);

    /// <summary>
    /// Chama uma função do módulo carregado. <paramref name="entrada"/> e
    /// <paramref name="saida"/> são vetores de 8 bytes (long).
    /// </summary>
    bool Executar(string funcao, long[] entrada, int saidaLongs, out long[] saida);
}

/// <summary>Implementação do transporte PawnIO sobre DeviceIoControl.</summary>
public sealed class PawnIoTransporte : IPawnIoTransporte
{
    private const string CaminhoDispositivo = @"\\?\GLOBALROOT\Device\PawnIO";

    // O driver usa DEVICE_TYPE = 41394 << 16 e três IOCTLs:
    //   0x821 = LOAD_BINARY, 0x841 = EXECUTE_FN (o << 2 é o bit de método).
    private const uint TipoDispositivo = 41394u << 16;
    private const uint IoctlCarregarBinario = TipoDispositivo | (0x821u << 2);
    private const uint IoctlExecutarFuncao = TipoDispositivo | (0x841u << 2);

    private const uint AcessoLeitura = 0x80000000;
    private const uint AcessoEscrita = 0x40000000;
    private const uint CompartilhamentoLeitura = 1;
    private const uint CompartilhamentoEscrita = 2;
    private const uint AbrirExistente = 3;
    private const uint AtributoNormal = 0x80;

    /// <summary>No protocolo PawnIO o nome da função ocupa 32 bytes antes dos argumentos.</summary>
    private const int TamanhoNomeFuncao = 32;

    private SafeFileHandle? _dispositivo;
    private bool _descartado;

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeFileHandle CreateFileW(
        string nomeArquivo, uint acessoDesejado, uint modoCompartilhamento, IntPtr atributosSeguranca,
        uint disposicaoCriacao, uint flagsAtributos, IntPtr modelo);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(
        SafeFileHandle dispositivo, uint codigoIoctl, byte[] bufferEntrada, int tamanhoEntrada,
        byte[] bufferSaida, int tamanhoSaida, out int bytesRetornados, IntPtr sobreposto);

    /// <summary>Abre o dispositivo do driver. Devolve false se o PawnIO não estiver instalado.</summary>
    public bool Abrir()
    {
        if (_dispositivo != null) return true;
        if (_descartado) return false;

        SafeFileHandle handle = CreateFileW(
            CaminhoDispositivo,
            AcessoLeitura | AcessoEscrita,
            CompartilhamentoLeitura | CompartilhamentoEscrita,
            IntPtr.Zero,
            AbrirExistente,
            AtributoNormal,
            IntPtr.Zero);

        if (handle.IsInvalid)
        {
            handle.Dispose();
            return false;
        }

        _dispositivo = handle;
        return true;
    }

    public bool CarregarModulo(byte[] modulo)
    {
        ArgumentNullException.ThrowIfNull(modulo);
        if (!Abrir()) return false;

        return DeviceIoControl(_dispositivo!, IoctlCarregarBinario, modulo, modulo.Length, Array.Empty<byte>(), 0, out _, IntPtr.Zero);
    }

    public bool Executar(string funcao, long[] entrada, int saidaLongs, out long[] saida)
    {
        ArgumentNullException.ThrowIfNull(funcao);
        ArgumentNullException.ThrowIfNull(entrada);

        saida = new long[saidaLongs];
        if (!Abrir()) return false;

        // O buffer de entrada é: nome da função (32 bytes) + os argumentos.
        byte[] total = new byte[(entrada.Length * 8) + TamanhoNomeFuncao];
        byte[] nome = Encoding.ASCII.GetBytes(funcao);
        Buffer.BlockCopy(nome, 0, total, 0, Math.Min(TamanhoNomeFuncao - 1, nome.Length));

        byte[] argumentos = new byte[entrada.Length * 8];
        Buffer.BlockCopy(entrada, 0, argumentos, 0, argumentos.Length);
        Buffer.BlockCopy(argumentos, 0, total, TamanhoNomeFuncao, argumentos.Length);

        byte[] bufferSaida = new byte[saidaLongs * 8];
        if (!DeviceIoControl(_dispositivo!, IoctlExecutarFuncao, total, total.Length,
                             bufferSaida, bufferSaida.Length, out int lidos, IntPtr.Zero))
        {
            return false;
        }

        int quantidade = Math.Min(lidos / 8, saidaLongs);
        if (quantidade > 0)
            Buffer.BlockCopy(bufferSaida, 0, saida, 0, quantidade * 8);

        return true;
    }

    public void Dispose()
    {
        if (_descartado) return;
        _descartado = true;

        _dispositivo?.Dispose();
        _dispositivo = null;
    }
}
