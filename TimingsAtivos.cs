namespace TranalSentinel;

/// <summary>Tipo de memória DDR que o controlador está usando.</summary>
public enum TipoMemoriaDdr
{
    /// <summary>Não deu para saber (o registro não foi lido ou o valor é novo).</summary>
    Desconhecido,

    /// <summary>DDR4 — o caso do Ryzen 5000 e do Ryzen 7000 com módulo DDR4.</summary>
    Ddr4,

    /// <summary>DDR5 — Ryzen 7000 em AM5 e Ryzen 9000.</summary>
    Ddr5,

    /// <summary>LPDDR4.</summary>
    Lpddr4,

    /// <summary>LPDDR5.</summary>
    Lpddr5
}

/// <summary>
/// Os tempos que estão <b>ativos agora</b> no controlador de memória: o que a
/// BIOS programou de verdade, lido dos registradores do UMC do AMD.
///
/// <para>
/// Isso é diferente do que o módulo de memória <i>oferece</i> (que vem do
/// SPD: padrão JEDEC e perfis XMP/EXPO). O SPD é o papel; os registradores do
/// UMC são o que está rodando. A diferença entre os dois é o que explica
/// travamento: tempo mais apertado do que o perfil é ajuste manual, e é
/// suspeito de instabilidade.
/// </para>
/// <para>
/// Os valores são ciclos do clock de memória. Para comparar com o perfil do
/// SPD (que vem em nanossegundos) use <see cref="CicloNs"/>.
/// </para>
/// </summary>
public sealed class TimingsAtivos
{
    /// <summary>Tipo de memória DDR em uso.</summary>
    public TipoMemoriaDdr Tipo { get; init; } = TipoMemoriaDdr.Desconhecido;

    /// <summary>Clock de memória, em MHz (1733 = 3466 MT/s em DDR4).</summary>
    public int ClockMhz { get; init; }

    /// <summary>Velocidade de transferência, em megatransferências por segundo.</summary>
    public int VelocidadeMts { get; init; }

    /// <summary>Modo de redução (gear down mode): o clock roda com divisor.</summary>
    public bool GearDownMode { get; init; }

    /// <summary>Taxa de comando: 1 ou 2 comandos por ciclo.</summary>
    public int CommandRate { get; init; }

    /// <summary>Troca de banco/grupo de bancos ligada.</summary>
    public bool BankGroupSwap { get; init; }

    /// <summary>tCL — latência CAS, em ciclos.</summary>
    public int TCl { get; init; }

    /// <summary>tRAS — de ativo a pré-carga, em ciclos.</summary>
    public int TRas { get; init; }

    /// <summary>tRCD de leitura, em ciclos.</summary>
    public int TRcdLeitura { get; init; }

    /// <summary>tRCD de escrita, em ciclos.</summary>
    public int TRcdEscrita { get; init; }

    /// <summary>tRP — pré-carga de linha, em ciclos.</summary>
    public int TRp { get; init; }

    /// <summary>tRC — ciclo de linha, em ciclos.</summary>
    public int TRc { get; init; }

    /// <summary>tRRDS — intervalo entre dois ativamentos de banco, em ciclos.</summary>
    public int TRrds { get; init; }

    /// <summary>tRRDL — intervalo entre ativações de bancos diferentes, em ciclos.</summary>
    public int TRrdl { get; init; }

    /// <summary>tRTP — de leitura a pré-carga, em ciclos.</summary>
    public int TRtp { get; init; }

    /// <summary>tFAW — janela de quatro ativamentos, em ciclos.</summary>
    public int TFaw { get; init; }

    /// <summary>tCWL — latência de escrita, em ciclos.</summary>
    public int TCwl { get; init; }

    /// <summary>tWTRS — espera de leitura para escrita (mesmo rank), em ciclos.</summary>
    public int TWtrs { get; init; }

    /// <summary>tWTRL — espera de leitura para escrita (ranks diferentes), em ciclos.</summary>
    public int TWtrl { get; init; }

    /// <summary>tWR — de escrita a pré-carga, em ciclos.</summary>
    public int TWr { get; init; }

    /// <summary>tCKE — tempo mínimo de CKE, em ciclos.</summary>
    public int TCke { get; init; }

    /// <summary>tREFI — intervalo de refresh, em ciclos.</summary>
    public int TRefi { get; init; }

    /// <summary>tREFI em nanossegundos.</summary>
    public double TRefiNs { get; init; }

    /// <summary>tSTAG — tempo de stalled refresh, em ciclos.</summary>
    public int TStag { get; init; }

    /// <summary>
    /// Endereço base do bloco de registradores lido (0x50200 no canal 0,
    /// 0x150200 no canal 1). Serve para o diagnóstico saber de onde veio.
    /// </summary>
    public int EnderecoBloco { get; init; }

    /// <summary>Duração de um ciclo do clock de memória, em nanossegundos.</summary>
    public double CicloNs => ClockMhz > 0 ? 1000.0 / ClockMhz : 0;

    /// <summary>tCL em nanossegundos.</summary>
    public double TClNs => TCl * CicloNs;

    /// <summary>tRAS em nanossegundos.</summary>
    public double TRasNs => TRas * CicloNs;

    /// <summary>tRP em nanossegundos.</summary>
    public double TRpNs => TRp * CicloNs;

    /// <summary>tRC em nanossegundos.</summary>
    public double TRcNs => TRc * CicloNs;

    /// <summary>tWR em nanossegundos.</summary>
    public double TWrNs => TWr * CicloNs;

    /// <summary>Resumo no formato do CPU-Z: tCL-tRCD-tRP-tRAS, em ciclos.</summary>
    public string ResumoCiclos => $"{TCl}-{TRcdLeitura}-{TRp}-{TRas}";

    /// <summary>Texto curto dos principais tempos, em nanossegundos.</summary>
    public string ResumoNs =>
        $"CL {TCl} · tRCD {TRcdLeitura} · tRP {TRp} · tRAS {TRas}";
}

/// <summary>
/// Os registradores do UMC lidos, sem interpretação: endereço → valor.
///
/// <para>
/// Existe para que o decodificador seja uma função pura, testável sem
/// driver e sem hardware: o teste entrega os valores reais de uma máquina e
/// confere o que o decodificador tirou deles.
/// </para>
/// </summary>
public sealed class RegistrosSmn
{
    /// <summary>Valor que o hardware devolve quando o endereço não existe.</summary>
    public const uint SemDado = 0xFFFFFFFF;

    private readonly Dictionary<uint, uint> _valores;

    /// <summary>Cria o conjunto de registradores lidos.</summary>
    public RegistrosSmn(IReadOnlyDictionary<uint, uint> valores)
    {
        ArgumentNullException.ThrowIfNull(valores);
        _valores = new Dictionary<uint, uint>(valores);
    }

    /// <summary>True quando o endereço foi lido e tem valor.</summary>
    public bool Valido(uint endereco) =>
        _valores.TryGetValue(endereco, out uint valor) && valor != SemDado;

    /// <summary>
    /// Valor do registrador. Quando o endereço não foi lido, devolve
    /// <see cref="SemDado"/> — que é o que o hardware devolve para um
    /// endereço inexistente.
    /// </summary>
    public uint Valor(uint endereco) =>
        _valores.TryGetValue(endereco, out uint valor) ? valor : SemDado;
}
