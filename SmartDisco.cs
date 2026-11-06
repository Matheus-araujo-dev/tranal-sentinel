using System.Globalization;

namespace TranalSentinel;

/// <summary>
/// Um atributo do S.M.A.R.T.: a linha da tabela que o CrystalDiskInfo mostra
/// (id, nome, flags, valor normalizado, pior valor, limiar e valor cru).
/// </summary>
public sealed class SmartAtributo
{
    /// <summary>O id do atributo (0x05 = Reallocated_Sector_Ct, 0xE8 = do fabricante...).</summary>
    public int Id { get; init; }

    /// <summary>O nome padrão do atributo (ou "Atributo 0x.." quando não há nome padrão).</summary>
    public string Nome { get; init; } = "";

    /// <summary>As 16 bits cruas das flags do atributo.</summary>
    public int Flags { get; init; }

    /// <summary>O valor normalizado (0-100), como o disco o publica.</summary>
    public int Valor { get; init; }

    /// <summary>O pior valor normalizado já registrado.</summary>
    public int Pior { get; init; }

    /// <summary>O limiar do SMART Read Thresholds (null = o disco não define limiar para este atributo).</summary>
    public int? Limiar { get; init; }

    /// <summary>Os 6 bytes crus do valor.</summary>
    public byte[] Cru { get; init; } = Array.Empty<byte>();

    /// <summary>
    /// O atributo é de pré-falha (quando o valor normalizado cair abaixo do
    /// limiar, o disco avisa que vai falhar).
    /// </summary>
    public bool PreFail { get; init; }

    /// <summary>O valor cru já formatado: os bytes em hexadecimal e o número que eles representam.</summary>
    public string CruTexto { get; init; } = "";

    /// <summary>
    /// O tipo do atributo pela flag: "pré-falha" (o disco avisa antes de
    /// falhar) ou "idade" (o valor só vai piorando com o tempo).
    /// </summary>
    public string TipoTexto => PreFail ? "Pré-falha" : "Idade";

    /// <summary>As flags em hexadecimal (ex.: "0x0032"), do jeito que a tela mostra.</summary>
    public string FlagsTexto => "0x" + Flags.ToString("X4", CultureInfo.InvariantCulture);

    /// <summary>O id em hexadecimal (ex.: "0x05"), do jeito que a tela mostra.</summary>
    public string IdTexto => "0x" + Id.ToString("X2", CultureInfo.InvariantCulture);

    /// <summary>O limiar como texto ("—" quando o disco não define limiar para o atributo).</summary>
    public string LimiarTexto => Limiar.HasValue
        ? Limiar.Value.ToString(CultureInfo.InvariantCulture)
        : "—";
}

/// <summary>
/// Um disco físico com o que o S.M.A.R.T. disse dele.
///
/// <para>
/// Quando o disco não expõe SMART, o objeto continua existindo (o usuário
/// precisa ver que o disco está lá) e a <see cref="Observacao"/> explica
/// por quê. Um disco sem observação e sem atributos é um disco que o app
/// não conseguiu ler por algum motivo — nunca um chute.
/// </para>
/// </summary>
public sealed class SmartDisco
{
    /// <summary>O número do disco físico (0 = PhysicalDrive0).</summary>
    public int Numero { get; init; }

    /// <summary>O modelo que o Windows mostra (ex.: "SanDisk SSD PLUS 240GB").</summary>
    public string Modelo { get; init; } = "";

    /// <summary>O número de série.</summary>
    public string Serial { get; init; } = "";

    /// <summary>A capacidade em bytes.</summary>
    public long CapacidadeBytes { get; init; }

    /// <summary>"SATA" (comando SMART pela ATA) ou "NVMe" (log SMART/Health).</summary>
    public string Tipo { get; init; } = "";

    /// <summary>O próprio disco avisa que deve falhar (SMART PredictFailure).</summary>
    public bool FalhaPrevista { get; init; }

    /// <summary>
    /// O aviso crítico do log SMART/Health do NVMe (reserva abaixo do
    /// limiar, temperatura alta, confiabilidade degradada...). Vazio quando
    /// não há aviso — ou quando o disco não expõe esse log.
    /// </summary>
    public string AvisoCritico { get; init; } = "";

    /// <summary>A tabela de atributos (discos ATA/SATA).</summary>
    public IReadOnlyList<SmartAtributo> Atributos { get; init; } = Array.Empty<SmartAtributo>();

    /// <summary>Os contadores do log SMART/Health (discos NVMe).</summary>
    public IReadOnlyList<SmartContador> Contadores { get; init; } = Array.Empty<SmartContador>();

    /// <summary>Uma frase com a situação do disco ("OK", "Atenção: 3 setores realocados"...).</summary>
    public string Resumo { get; init; } = "";

    /// <summary>Por que não há SMART (ex.: "este disco precisa de execução como administrador").</summary>
    public string Observacao { get; init; } = "";

    /// <summary>
    /// A vida útil que resta, em % (100 = novo). Vem do log SMART/Health do
    /// NVMe ou do atributo 232 (Remaining_Lifetime_Perc) dos discos ATA.
    /// </summary>
    public int? VidaRestantePercent { get; init; }

    /// <summary>O disco disse alguma coisa? (sem atributo e sem contador, não disse).</summary>
    public bool TemDados => Atributos.Count > 0 || Contadores.Count > 0;

    /// <summary>Tem tabela de atributos (discos ATA/SATA)?</summary>
    public bool TemAtributos => Atributos.Count > 0;

    /// <summary>Tem contadores (discos NVMe)?</summary>
    public bool TemContadores => Contadores.Count > 0;

    /// <summary>A linha de identificação do cartão (tipo, capacidade e serial).</summary>
    public string Meta => string.Join(" · ", new[]
    {
        Tipo,
        CapacidadeTexto,
        Serial.Length > 0 ? "Serial " + Serial : "",
    }.Where(x => !string.IsNullOrEmpty(x)));

    /// <summary>Um rótulo curto para o selo do cartão.</summary>
    public string Selo => Resumo switch
    {
        "" => "SEM DADOS",
        _ when Resumo.StartsWith("OK", StringComparison.Ordinal) => "OK",
        _ when Resumo.StartsWith("Falha prevista", StringComparison.OrdinalIgnoreCase) => "CRÍTICO",
        _ when Resumo.StartsWith("Atenção", StringComparison.OrdinalIgnoreCase) => "ATENÇÃO",
        _ => "SEM DADOS",
    };

    /// <summary>Tem algo errado? (o selo do cartão fica destacado).</summary>
    public bool Alerta => Selo is "ATENÇÃO" or "CRÍTICO";

    /// <summary>
    /// O valor cru (o número) de um atributo, pelo id.
    /// Null quando o disco não tem esse atributo.
    /// </summary>
    public long? ValorCru(int id)
    {
        foreach (var a in Atributos)
            if (a.Id == id)
                return a.Cru.Length == 0 ? 0 : NumeroCru(a.Cru);
        return null;
    }

    /// <summary>A capacidade já formatada ("240 GB"); vazio quando o Windows não informou o tamanho.</summary>
    public string CapacidadeTexto
    {
        get
        {
            if (CapacidadeBytes <= 0) return "";
            double v = CapacidadeBytes;
            string[] u = { "B", "KB", "MB", "GB", "TB" };
            int i = 0;
            while (v >= 1000 && i < u.Length - 1) { v /= 1000; i++; }
            return v.ToString("0.#", CultureInfo.InvariantCulture) + " " + u[i];
        }
    }

    /// <summary>O número que os bytes crus representam (little-endian, como a ATA guarda).</summary>
    internal static long NumeroCru(byte[] cru)
    {
        ulong n = 0;
        for (int i = cru.Length - 1; i >= 0; i--) n = (n << 8) | cru[i];
        return (long)n;
    }
}

/// <summary>Um contador do log SMART/Health do NVMe (temperatura, escrita, erros...).</summary>
public sealed class SmartContador
{
    /// <summary>O nome do contador (ex.: "Vida útil restante").</summary>
    public string Nome { get; init; } = "";

    /// <summary>O valor do contador, já formatado para a tela.</summary>
    public string Valor { get; init; } = "";
}
