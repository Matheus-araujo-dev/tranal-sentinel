namespace ResetExplode;

/// <summary>Gravidade de um achado do diagnóstico.</summary>
public enum Severidade { Informativo, Baixo, Medio, Alto, Critico }

/// <summary>Um problema encontrado na análise de saúde, com o que fazer a respeito.</summary>
public sealed class Achado
{
    public Severidade Severidade { get; init; }
    public string Area { get; init; } = "";
    public string Mensagem { get; init; } = "";
    public string Acao { get; init; } = "";

    /// <summary>Rótulo curto da severidade, usado nos cartões da interface.</summary>
    public string Selo => Severidade switch
    {
        Severidade.Critico => "CRÍTICO",
        Severidade.Alto => "ALTO",
        Severidade.Medio => "MÉDIO",
        Severidade.Baixo => "BAIXO",
        _ => "INFO"
    };

    /// <summary>Crítico e alto saem destacados; o resto fica em contorno.</summary>
    public bool IsAlerta => Severidade is Severidade.Critico or Severidade.Alto;
}

/// <summary>Um travamento: boot após desligamento não limpo (Kernel-Power 41).</summary>
public sealed class TravamentoInfo
{
    public DateTime Data { get; init; }

    /// <summary>Código de bugcheck. Zero significa travamento total, sem tela azul.</summary>
    public string Bugcheck { get; init; } = "0";

    /// <summary>Texto legível do que o evento registrou.</summary>
    public string Detalhe { get; init; } = "";

    /// <summary>Data já formatada para a lista da interface.</summary>
    public string DataFormatada => Data == DateTime.MinValue ? "—" : Data.ToString("dd/MM/yyyy HH:mm");
}

/// <summary>Resultado da análise de saúde do sistema.</summary>
public sealed class RelatorioSaude
{
    public DateTime GeradoEm { get; init; }
    public List<Achado> Achados { get; } = new();
    public List<TravamentoInfo> Travamentos { get; } = new();

    /// <summary>Apps que falharam na janela de cada travamento (contagem).</summary>
    public Dictionary<string, int> AppsNoBoot { get; } = new();

    /// <summary>Dados brutos coletados, para a interface mostrar o contexto.</summary>
    public ColetaSaude Dados { get; init; } = new();

    public int Criticos => Achados.Count(a => a.Severidade == Severidade.Critico);
    public int Altos => Achados.Count(a => a.Severidade == Severidade.Alto);
    public int Total => Achados.Count;

    public string Resumo =>
        Total == 0
            ? "Nenhum problema encontrado."
            : $"{Total} achado(s): {Criticos} crítico(s), {Altos} alto(s), {Total - Criticos - Altos} restante(s).";
}

/// <summary>Disco mapeado pelo Windows.</summary>
public sealed class DiscoInfo
{
    public string Modelo { get; init; } = "";
    public string Status { get; init; } = "";
    public string PnpDeviceId { get; init; } = "";
    public long TamanhoBytes { get; init; }

    /// <summary>True quando o disco é NVMe (pela descrição do modelo ou do PnP).</summary>
    public bool EhNvme =>
        Modelo.Contains("NVMe", StringComparison.OrdinalIgnoreCase) ||
        PnpDeviceId.Contains("NVME", StringComparison.OrdinalIgnoreCase);

    /// <summary>True quando o Windows não está vendo o drive com prontidão normal.</summary>
    public bool EstaComProblema =>
        !string.IsNullOrWhiteSpace(Status) &&
        !Status.Equals("OK", StringComparison.OrdinalIgnoreCase);
}

/// <summary>Módulo de memória RAM instalado.</summary>
public sealed class ModuloMemoria
{
    public string Slot { get; init; } = "";
    public string Fabricante { get; init; } = "";
    public long CapacidadeBytes { get; init; }
    public int VelocidadeMHz { get; init; }
    public int VelocidadeConfigMHz { get; init; }

    public double Gigabytes => Math.Round(CapacidadeBytes / 1024.0 / 1024.0 / 1024.0, 1);
}

/// <summary>Serviço do Windows que inicia automaticamente.</summary>
public sealed class ServicoInfo
{
    public string Nome { get; init; } = "";
    public string ModoInicializacao { get; init; } = "";
    public string Caminho { get; init; } = "";

    /// <summary>Serviços do próprio Windows moram em system32/SysWOW64.</summary>
    public bool EhDoSistema =>
        Caminho.Contains("system32", StringComparison.OrdinalIgnoreCase) ||
        Caminho.Contains("SysWOW64", StringComparison.OrdinalIgnoreCase);
}

/// <summary>Dispositivo de armazenamento que sobrou de troca de hardware.</summary>
public sealed class DispositivoOrfao
{
    public string Nome { get; init; } = "";
    public int CodigoErro { get; init; }
}

/// <summary>
/// Fotografia do estado do sistema, somente leitura.
/// É o insumo das regras: coleta e análise ficam separadas para que as
/// regras possam ser testadas sem tocar no Windows.
/// </summary>
public sealed class ColetaSaude
{
    // travamentos e histórico
    public List<TravamentoInfo> Travamentos { get; } = new();
    public Dictionary<string, int> AppsQueFalharam { get; } = new();
    public int EventosWhea { get; set; }
    public int Minidumps { get; set; }
    public int ResetsDriverVideo { get; set; }

    // PCIe / energia
    public int? AspmAc { get; set; }
    public int? AspmDc { get; set; }

    // discos
    public List<DiscoInfo> Discos { get; } = new();
    public List<DispositivoOrfao> Orfaos { get; } = new();
    public bool NvmeEmAdaptador { get; set; }

    // segurança
    public string? ModoDefender { get; set; }
    public string? DriverAntivirus { get; set; }
    public string? EstadoDriverAntivirus { get; set; }

    /// <summary>Antivírus de terceiros registrado no Security Center (null se só o Defender).</summary>
    public string? AntivirusRegistrado { get; set; }

    public int AntivirusMemoriaMb { get; set; }
    public int? DefinicoesAntivirusDias { get; set; }
    public string DefinicoesAntivirusData { get; set; } = "";

    // memória
    public int MemoriaCompressaoMb { get; set; }
    public List<ModuloMemoria> ModulosMemoria { get; } = new();

    // lentidão
    public int AutostartAtivos { get; set; }
    public int AutostartTotal { get; set; }
    public List<ServicoInfo> ServicosAuto { get; } = new();
    public bool HypervisorAtivo { get; set; }
}
