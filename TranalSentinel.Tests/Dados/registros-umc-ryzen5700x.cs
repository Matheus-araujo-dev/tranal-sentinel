namespace TranalSentinel.Tests;

/// <summary>
/// Registradores do UMC lidos de verdade no Ryzen 7 5700X (Zen 3, família 19h,
/// modelo 21h) da máquina de desenvolvimento, pelo módulo PawnIO do SMN
/// (AMDFamily17) — só a função de leitura.
///
/// <para>
/// A máquina: 2×16 GB Asgard 3200 (XMP 2.0, 18-18-18-42) com o clock da BIOS
/// em 3466 MT/s e os tempos em 18-22-22-42. Os dois canais do UMC têm os
/// mesmos valores: os dois módulos são idênticos e estão nos dois canais.
/// </para>
/// </summary>
internal static class RegistrosUmcRyzen5700X
{
    /// <summary>Valores do canal 0 (endereços absolutos do SMN).</summary>
    public static Dictionary<uint, uint> Canal0() => new()
    {
        { 0x50050, 0x87654321 }, // troca de banco/grupo de bancos (marcador: desligada)
        { 0x50058, 0x87654321 }, // idem
        { 0x50100, 0x80000200 }, // tipo de memória: bits 1-0 = 00 = DDR4
        { 0x50200, 0x00001934 }, // clock, taxa de comando, gear down
        { 0x50204, 0x16162A12 }, // tCL, tRAS, tRCD leitura, tRCD escrita
        { 0x50208, 0x00160050 }, // tRC, tRP
        { 0x5020C, 0x0E000906 }, // tRRDS, tRRDL, tRTP
        { 0x50210, 0x00000025 }, // tFAW
        { 0x50214, 0x000D0512 }, // tCWL, tWTRS, tWTRL
        { 0x50218, 0x0000001A }, // tWR
        { 0x50220, 0x45010404 }, // tRDRD por rank
        { 0x50224, 0x45010606 }, // tWRWR por rank
        { 0x50228, 0x00001207 }, // tWRRD, tRDWR
        { 0x50230, 0x000034B7 }, // tREFI
        { 0x50250, 0x00FF0000 }, // tSTAG
        { 0x50254, 0x0004000B } // tCKE
    };

    /// <summary>
    /// Valores do canal 1: os mesmos, no bloco +0x100000 (0x50000 → 0x150000).
    /// </summary>
    public static Dictionary<uint, uint> Canal1()
    {
        var canal = new Dictionary<uint, uint>();
        foreach ((uint endereco, uint valor) in Canal0())
        {
            canal[endereco + 0x100000] = valor;
        }

        return canal;
    }

    /// <summary>Os dois canais juntos, como a coleta entrega ao decodificador.</summary>
    public static Dictionary<uint, uint> DoisCanais()
    {
        var todos = Canal0();
        foreach ((uint endereco, uint valor) in Canal1())
        {
            todos[endereco] = valor;
        }

        return todos;
    }
}
