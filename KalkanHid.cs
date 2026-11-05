using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace ResetExplode;

/// <summary>Transporte HID nativo do Windows para o visor Kalkan (linha Alpha Rev.1).</summary>
/// <remarks>
/// Não depende do <c>hidapi.dll</c> do app oficial nem de nenhuma biblioteca de
/// terceiros: só SetupAPI (para achar o dispositivo), hid.pd (para ler a página
/// de uso) e o kernel32 (para o I/O). Assim o RESET EXPLODE fala com o visor
/// mesmo com o software da Kalkan desinstalado.
///
/// O identificador do visor é fixo no firmware 1.2 (VID 0416 / PID 1100), mas a
/// instância do Windows muda de máquina para máquina — por isso o caminho é
/// descoberto por enumeração, nunca chumbado.
/// </remarks>
public sealed class KalkanHid : IDisposable
{
    private const uint FileShareRead = 0x1;
    private const uint FileShareWrite = 0x2;
    private const uint OpenExisting = 3;
    private const uint FileFlagOverlapped = 0x40000000;
    private const uint GenericRead = 0x80000000;
    private const uint GenericWrite = 0x40000000;

    private IntPtr _handle = IntPtr.Zero;
    private IntPtr _preparsed = IntPtr.Zero;
    private bool _descartado;

    /// <summary>Caminho da interface HID aberta (o <c>\\?\HID#…</c> do SetupAPI).</summary>
    public string Caminho { get; }

    /// <summary>Interface USB da coleção (COL01, COL02…).</summary>
    public string Colecao { get; }

    /// <summary>Página de uso do descritor (0xFF = uso do fabricante).</summary>
    public ushort PaginaDeUso { get; }

    /// <summary>Tamanho do relatório de saída declarado pelo descritor.</summary>
    public ushort TamanhoRelatorioSaida { get; }

    private KalkanHid(string caminho, string colecao, ushort paginaDeUso, ushort tamanhoRelatorio)
    {
        Caminho = caminho;
        Colecao = colecao;
        PaginaDeUso = paginaDeUso;
        TamanhoRelatorioSaida = tamanhoRelatorio;
    }

    /// <summary>Fecha o handle e libera a memória do descritor.</summary>
    public void Dispose()
    {
        if (_descartado) return;
        _descartado = true;
        if (_preparsed != IntPtr.Zero)
        {
            KalkanNative.HidD_FreePreparsedData(_preparsed);
            _preparsed = IntPtr.Zero;
        }
        if (_handle != IntPtr.Zero)
        {
            KalkanNative.CloseHandle(_handle);
            _handle = IntPtr.Zero;
        }
    }

    /// <summary>
    /// Procura as coleções HID do visor Kalkan (VID 0416 / PID 1100) que aceitam
    /// escrita de 64 bytes, da mais provável para a menos.
    /// </summary>
    public static List<KalkanHid> EnumerarCandidatos()
    {
        var achados = new List<KalkanHid>();
        KalkanNative.HidD_GetHidGuid(out Guid guidHid);
        IntPtr conjunto = KalkanNative.SetupDiGetClassDevsW(ref guidHid, IntPtr.Zero, IntPtr.Zero, KalkanNative.DigcfInterface);
        if (conjunto == IntPtr.Zero || conjunto == new IntPtr(-1)) return achados;

        try
        {
            var dados = new KalkanNative.SP_DEVICE_INTERFACE_DATA { cbSize = Marshal.SizeOf<KalkanNative.SP_DEVICE_INTERFACE_DATA>() };
            for (uint i = 0; KalkanNative.SetupDiEnumDeviceInterfaces(conjunto, 0, ref guidHid, i, ref dados); i++)
            {
                string caminho = KalkanNative.ObterCaminhoInterface(conjunto, ref dados);
                if (string.IsNullOrEmpty(caminho)) continue;
                if (!caminho.Contains("VID_0416&PID_1100", StringComparison.OrdinalIgnoreCase)) continue;

                KalkanHid? dispositivo = TentarAbrir(caminho, ExtrairColecao(caminho));
                if (dispositivo == null) continue;
                achados.Add(dispositivo);
            }
        }
        finally
        {
            KalkanNative.SetupDiDestroyDeviceInfoList(conjunto);
        }

        // Coleção do fabricante primeiro (a de toque não serve).
        achados.Sort((a, b) => Ordem(a).CompareTo(Ordem(b)));
        return achados;
    }

    private static int Ordem(KalkanHid d)
    {
        // 0 = página de uso do fabricante com relatório de 64 bytes (a ideal).
        bool fabricante = d.PaginaDeUso == 0x00FF || d.PaginaDeUso >= 0xFF00;
        if (fabricante && d.TamanhoRelatorioSaida == KalkanProtocol.ReportLen) return 0;
        if (fabricante) return 1;
        return 2;
    }

    private static string ExtrairColecao(string caminho)
    {
        int i = caminho.IndexOf("&COL", StringComparison.OrdinalIgnoreCase);
        if (i < 0) return "";
        int fim = caminho.IndexOf('#', i);
        if (fim < 0) fim = caminho.Length;
        return caminho.Substring(i + 1, fim - i - 1);
    }

    private static KalkanHid? TentarAbrir(string caminho, string colecao)
    {
        IntPtr handle = KalkanNative.CreateFileW(caminho, GenericRead | GenericWrite,
            FileShareRead | FileShareWrite, IntPtr.Zero, OpenExisting, FileFlagOverlapped, IntPtr.Zero);
        if (handle == IntPtr.Zero || handle == new IntPtr(-1)) return null;

        ushort paginaDeUso = 0, tamanhoSaida = 0;
        IntPtr preparsed = IntPtr.Zero;
        try
        {
            if (KalkanNative.HidD_GetPreparsedData(handle, out preparsed) && preparsed != IntPtr.Zero)
                KalkanNative.LerCaps(preparsed, out paginaDeUso, out tamanhoSaida);
        }
        catch
        {
            // Sem caps: seguimos só com o filtro do caminho.
        }

        // Interface padrão (uso do fabricante) com relatório de 64 bytes.
        if (paginaDeUso != 0 && paginaDeUso != 0x00FF && paginaDeUso < 0xFF00)
        {
            if (preparsed != IntPtr.Zero) KalkanNative.HidD_FreePreparsedData(preparsed);
            KalkanNative.CloseHandle(handle);
            return null;
        }
        if (tamanhoSaida != 0 && tamanhoSaida != KalkanProtocol.ReportLen)
        {
            if (preparsed != IntPtr.Zero) KalkanNative.HidD_FreePreparsedData(preparsed);
            KalkanNative.CloseHandle(handle);
            return null;
        }

        return new KalkanHid(caminho, colecao, paginaDeUso, tamanhoSaida) { _handle = handle, _preparsed = preparsed };
    }

    /// <summary>Escreve um relatório de saída (o byte 0 é o report ID).</summary>
    public bool Escrever(byte[] dados, int timeoutMs, out string erro)
    {
        erro = "";
        if (_handle == IntPtr.Zero) { erro = "dispositivo fechado"; return false; }

        IntPtr ev = KalkanNative.CreateEvent(IntPtr.Zero, true, false, null);
        if (ev == IntPtr.Zero) { erro = "não foi possível criar o evento de I/O"; return false; }

        try
        {
            var ov = new KalkanNative.OVERLAPPED { hEvent = ev };
            uint escritos;
            bool ok = KalkanNative.WriteFile(_handle, dados, (uint)dados.Length, out escritos, ref ov);
            if (!ok)
            {
                int erroWin = Marshal.GetLastWin32Error();
                if (erroWin != KalkanNative.ErrorIoPending)
                {
                    erro = "escrita no visor falhou (Windows " + erroWin + ")";
                    return false;
                }
                if (KalkanNative.WaitForSingleObject(ev, (uint)timeoutMs) != KalkanNative.WaitObject0)
                {
                    KalkanNative.CancelIoEx(_handle, IntPtr.Zero);
                    erro = "tempo esgotado escrevendo no visor";
                    return false;
                }
                if (!KalkanNative.GetOverlappedResult(_handle, ref ov, out escritos, false))
                {
                    erro = "escrita no visor falhou ao concluir";
                    return false;
                }
            }
            if (escritos != dados.Length)
            {
                erro = "escrita parcial no visor (" + escritos + " de " + dados.Length + " bytes)";
                return false;
            }
            return true;
        }
        finally
        {
            KalkanNative.CloseHandle(ev);
        }
    }

    /// <summary>
    /// Lê um relatório de entrada. Devolve a quantidade de bytes lidos, 0 se o
    /// visor não respondeu dentro do tempo e -1 se deu erro.
    /// </summary>
    public int Ler(int timeoutMs, out string erro)
    {
        erro = "";
        if (_handle == IntPtr.Zero) { erro = "dispositivo fechado"; return -1; }

        var buffer = new byte[KalkanProtocol.ReadLen];
        IntPtr ev = KalkanNative.CreateEvent(IntPtr.Zero, true, false, null);
        if (ev == IntPtr.Zero) { erro = "não foi possível criar o evento de I/O"; return -1; }

        try
        {
            var ov = new KalkanNative.OVERLAPPED { hEvent = ev };
            uint lidos;
            bool ok = KalkanNative.ReadFile(_handle, buffer, (uint)buffer.Length, out lidos, ref ov);
            if (!ok)
            {
                int erroWin = Marshal.GetLastWin32Error();
                if (erroWin != KalkanNative.ErrorIoPending)
                {
                    erro = "leitura do visor falhou (Windows " + erroWin + ")";
                    return -1;
                }
                uint espera = KalkanNative.WaitForSingleObject(ev, (uint)timeoutMs);
                if (espera == KalkanNative.WaitTimeout)
                {
                    // O visor pode não responder nada: cancela e segue.
                    KalkanNative.CancelIoEx(_handle, IntPtr.Zero);
                    KalkanNative.GetOverlappedResult(_handle, ref ov, out lidos, true);
                    return 0;
                }
                if (espera != KalkanNative.WaitObject0)
                {
                    erro = "erro de espera na leitura do visor";
                    return -1;
                }
                if (!KalkanNative.GetOverlappedResult(_handle, ref ov, out lidos, false))
                {
                    erro = "leitura do visor falhou ao concluir";
                    return -1;
                }
            }
            if (lidos == 0) return 0;
            Array.Copy(buffer, UltimaLeitura, (int)Math.Min(lidos, (uint)UltimaLeitura.Length));
            return (int)lidos;
        }
        finally
        {
            KalkanNative.CloseHandle(ev);
        }
    }

    /// <summary>Conteúdo da última leitura bem-sucedida.</summary>
    public byte[] UltimaLeitura { get; } = new byte[KalkanProtocol.ReadLen];

    /// <summary>Relatório bruto que o visor devolveu, em hexadecimal.</summary>
    public string UltimaLeituraHex => KalkanProtocol.ToHex(UltimaLeitura);
}
