using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace TranalSentinel;

/// <summary>
/// Detecção de dispositivos órfãos (fantasma).
///
/// Um dispositivo vira "fantasma" quando some do hardware mas continua
/// registrado no Windows — sobra de troca de SSD, placa removida, etc.
///
/// Não dá para usar o WMI aqui: Win32_PnPEntity.ConfigManagerErrorCode
/// devolve sempre 0 e a classe Win32_PnPDeviceProperty não é populada.
/// A rota que funciona é a mesma do Get-PnpDevice do PowerShell:
///   1. SetupAPI enumera todas as instâncias de dispositivo;
///   2. cfgmgr32 (CM_Locate_DevNode) responde se a instância ainda existe
///      na árvore de dispositivos — se não responde, é fantasma;
///   3. o nome amigável sai do registro (Enum\{classe}\{instância}).
///
/// Nada é alterado: é só leitura.
/// </summary>
public static class SaudePnp
{
    private const int DIGCF_ALLCLASSES = 0x00000004;
    private const uint CM_LOCATE_DEVICE = 0x00000000;

    /// <summary>CM_PROB_PHANTOM: o dispositivo está presente no registro mas não no hardware.</summary>
    private const int CM_PROB_PHANTOM = 8;

    [StructLayout(LayoutKind.Sequential)]
    private struct SP_DEVINFO_DATA
    {
        public int cbSize;
        public Guid ClassGuid;
        public uint DevInst;
        public IntPtr Reserved;
    }

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SetupDiGetClassDevs(IntPtr classInfoSet, string? enumerator, IntPtr hwndParent, int flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiEnumDeviceInfo(IntPtr deviceInfoSet, int memberIndex, ref SP_DEVINFO_DATA deviceInfoData);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetupDiGetDeviceInstanceId(IntPtr deviceInfoSet, ref SP_DEVINFO_DATA deviceInfoData,
                                                          StringBuilder deviceInstanceId, int deviceInstanceIdSize, out int requiredSize);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern int CM_Locate_DevNode(out uint devInst, string deviceId, uint flags);

    /// <summary>
    /// Dispositivos órfãos de armazenamento (NVMe, SSD, RAID, controlador de disco).
    /// Devolve lista vazia se a API não estiver disponível.
    /// </summary>
    public static List<DispositivoOrfao> OrfaosArmazenamento()
    {
        var lista = new List<DispositivoOrfao>();
        try
        {
            foreach (var (id, nome) in Instancias())
            {
                if (!EhFantasma(id)) continue;
                if (!EhArmazenamento(nome, id)) continue;
                lista.Add(new DispositivoOrfao { Nome = nome, CodigoErro = CM_PROB_PHANTOM });
            }
        }
        catch { }
        return lista;
    }

    /// <summary>Nome amigável de um dispositivo pelo id de instância.</summary>
    public static string NomeAmigavel(string instanciaId)
    {
        try
        {
            using var chave = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Enum\{instanciaId}");
            if (chave == null) return "";

            if (chave.GetValue("FriendlyName") is string amigavel && !string.IsNullOrWhiteSpace(amigavel))
                return amigavel.Trim();

            // DeviceDesc vem como "@arquivo.inf,%recurso%;Nome legível" — fica o nome legível.
            if (chave.GetValue("DeviceDesc") is string desc && !string.IsNullOrWhiteSpace(desc))
            {
                var separador = desc.LastIndexOf(';');
                return (separador >= 0 ? desc[(separador + 1)..] : desc).Trim();
            }
        }
        catch { }
        return "";
    }

    /// <summary>True quando a instância não existe mais na árvore de dispositivos.</summary>
    private static bool EhFantasma(string instanciaId)
    {
        try
        {
            return CM_Locate_DevNode(out _, instanciaId, CM_LOCATE_DEVICE) != 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Armazenamento: NVMe, SSD, RAID ou controlador de disco (pelo nome ou pelo id PnP).</summary>
    private static bool EhArmazenamento(string nome, string instanciaId) =>
        Contem(nome, "NVMe") || Contem(nome, "NVM Express") || Contem(nome, "SSD") ||
        Contem(nome, "RAID") || Contem(nome, "Storage") ||
        Contem(instanciaId, "VEN_NVME") || Contem(instanciaId, "VEN_ATA") ||
        Contem(instanciaId, "RAID");

    private static bool Contem(string texto, string termo) =>
        !string.IsNullOrEmpty(texto) && texto.Contains(termo, StringComparison.OrdinalIgnoreCase);

    /// <summary>Enumera todas as instâncias de dispositivo do sistema (id + nome amigável).</summary>
    private static List<(string Id, string Nome)> Instancias()
    {
        var lista = new List<(string, string)>();

        var set = SetupDiGetClassDevs(IntPtr.Zero, null, IntPtr.Zero, DIGCF_ALLCLASSES);
        if (set == IntPtr.Zero || set == new IntPtr(-1)) return lista;

        try
        {
            for (int i = 0; ; i++)
            {
                var dados = new SP_DEVINFO_DATA { cbSize = Marshal.SizeOf<SP_DEVINFO_DATA>() };
                if (!SetupDiEnumDeviceInfo(set, i, ref dados)) break;

                var id = new StringBuilder(1024);
                if (!SetupDiGetDeviceInstanceId(set, ref dados, id, id.Capacity, out _)) continue;

                var instanciaId = id.ToString();
                if (instanciaId.Length == 0) continue;

                lista.Add((instanciaId, NomeAmigavel(instanciaId)));
            }
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(set);
        }

        return lista;
    }
}
