using System;
using System.Runtime.InteropServices;

namespace TranalSentinel;

/// <summary>
/// Declarações P/Invoke do transporte HID do visor Kalkan.
///
/// Só usa DLLs do próprio Windows: setupapi.dll (enumerar interfaces),
/// hid.dll/hid.pd.dll (descritor do dispositivo) e kernel32.dll (I/O).
/// </summary>
internal static class KalkanNative
{
    internal const int ErrorIoPending = 997;
    internal const uint WaitObject0 = 0;
    internal const uint WaitTimeout = 258;

    internal const uint DigcfPresent = 0x00000002;

    /// <summary>DIGCF_PRESENT | DIGCF_DEVICEINTERFACE — obrigatório para enumerar interfaces.</summary>
    internal const uint DigcfInterface = 0x00000002 | 0x00000010;

    // ---------- kernel32 ----------

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern IntPtr CreateFileW(string nomeArquivo, uint acesso, uint compartilhamento,
        IntPtr atributosSeguranca, uint disposicaoCriacao, uint flagsAtributos, IntPtr template);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool WriteFile(IntPtr handle, byte[] buffer, uint bytesParaEscrever,
        out uint bytesEscritos, ref OVERLAPPED overlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ReadFile(IntPtr handle, byte[] buffer, uint bytesParaLer,
        out uint bytesLidos, ref OVERLAPPED overlapped);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern IntPtr CreateEvent(IntPtr atributosSeguranca,
        [MarshalAs(UnmanagedType.Bool)] bool manualReset, [MarshalAs(UnmanagedType.Bool)] bool estadoInicial, string? nome);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern uint WaitForSingleObject(IntPtr handle, uint milissegundos);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetOverlappedResult(IntPtr handle, ref OVERLAPPED overlapped,
        out uint bytesTransferidos, [MarshalAs(UnmanagedType.Bool)] bool aguardar);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool CancelIoEx(IntPtr handle, IntPtr overlapped);

    // ---------- setupapi ----------

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern IntPtr SetupDiGetClassDevsW(ref Guid classeGuia, IntPtr enumerador,
        IntPtr janelaPai, uint flags);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetupDiEnumDeviceInterfaces(IntPtr conjuntoDispositivos, uint membro,
        ref Guid classeGuia, uint indiceMembro, ref SP_DEVICE_INTERFACE_DATA dadosInterface);

    [DllImport("setupapi.dll", EntryPoint = "SetupDiGetDeviceInterfaceDetail", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetupDiGetDeviceInterfaceDetailW(IntPtr conjuntoDispositivos,
        ref SP_DEVICE_INTERFACE_DATA dadosInterface, IntPtr dadosDetalhe, uint tamanhoDetalhe,
        out uint tamanhoNecessario, IntPtr dadosDispositivo);

    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetupDiDestroyDeviceInfoList(IntPtr conjuntoDispositivos);

    // ---------- hid.dll / hid.pd.dll ----------

    [DllImport("hid.dll", SetLastError = true)]
    internal static extern void HidD_GetHidGuid(out Guid guid);

    [DllImport("hid.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool HidD_GetPreparsedData(IntPtr handle, out IntPtr dadosPreparsados);

    [DllImport("hid.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool HidD_FreePreparsedData(IntPtr dadosPreparsados);

    /// <summary>
    /// HidP_GetCaps mora em hid.pd.dll em algumas versões do Windows e em
    /// hid.dll em outras — por isso a resolução é dinâmica.
    /// </summary>
    private delegate int HidPGetCaps(IntPtr dadosPreparsados, IntPtr caps);

    private static readonly HidPGetCaps? _hidPGetCaps = ResolverHidPGetCaps();

    private static HidPGetCaps? ResolverHidPGetCaps()
    {
        foreach (string dll in new[] { "hid.pd.dll", "hid.dll" })
        {
            try
            {
                if (!NativeLibrary.TryLoad(dll, out IntPtr lib)) continue;
                if (!NativeLibrary.TryGetExport(lib, "HidP_GetCaps", out IntPtr funcao)) continue;
                return Marshal.GetDelegateForFunctionPointer<HidPGetCaps>(funcao);
            }
            catch
            {
                // tenta a próxima
            }
        }
        return null;
    }

    /// <summary>
    /// Lê o tamanho do relatório de saída e a página de uso do descritor.
    /// Devolve false se a biblioteca não estiver disponível.
    /// </summary>
    internal static bool LerCaps(IntPtr dadosPreparsados, out ushort paginaDeUso, out ushort tamanhoSaida)
    {
        paginaDeUso = 0;
        tamanhoSaida = 0;
        if (_hidPGetCaps == null || dadosPreparsados == IntPtr.Zero) return false;

        IntPtr buffer = Marshal.AllocHGlobal(128);
        try
        {
            // HIDP_CAPS: Usage(0) UsagePage(2) InputReportByteLength(4) OutputReportByteLength(6)
            int status = _hidPGetCaps(dadosPreparsados, buffer);
            if (status != 0x00110000) return false;   // HIDP_STATUS_SUCCESS
            paginaDeUso = (ushort)Marshal.ReadInt16(buffer, 2);
            tamanhoSaida = (ushort)Marshal.ReadInt16(buffer, 6);
            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    /// <summary>Caminho da interface HID (o \\?\HID#… do SetupAPI).</summary>
    internal static string ObterCaminhoInterface(IntPtr conjunto, ref SP_DEVICE_INTERFACE_DATA dados)
    {
        uint necessario = 0;
        SetupDiGetDeviceInterfaceDetailW(conjunto, ref dados, IntPtr.Zero, 0, out necessario, IntPtr.Zero);
        if (necessario == 0) return "";

        IntPtr buffer = Marshal.AllocHGlobal((int)necessario);
        try
        {
            // cbSize do SP_DEVICE_INTERFACE_DETAIL_DATA: 8 em 64 bits, 6 em 32 bits.
            Marshal.WriteInt32(buffer, IntPtr.Size == 8 ? 8 : 6);
            uint tamanho = necessario;
            if (!SetupDiGetDeviceInterfaceDetailW(conjunto, ref dados, buffer, tamanho, out necessario, IntPtr.Zero))
                return "";
            // O DevicePath começa 4 bytes depois do cbSize.
            return Marshal.PtrToStringUni(new IntPtr(buffer.ToInt64() + 4)) ?? "";
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    // ---------- estruturas ----------

    [StructLayout(LayoutKind.Sequential)]
    internal struct SP_DEVICE_INTERFACE_DATA
    {
        public int cbSize;
        public Guid InterfaceClassGuid;
        public uint Flags;
        public IntPtr Reserved;
    }

    /// <summary>OVERLAPPED do Windows: 32 bytes em 64 bits, 20 em 32 bits.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct OVERLAPPED
    {
        public IntPtr Internal;
        public IntPtr InternalHigh;
        public int Offset;
        public int OffsetHigh;
        public IntPtr hEvent;
    }
}
