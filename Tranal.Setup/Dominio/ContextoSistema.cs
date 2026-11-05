using System.IO;

namespace TranalSetup.Dominio;

/// <summary>
/// Caminhos do Windows usados pelo instalador, guardados num objeto só.
///
/// O instalador não deve ler as pastas do sistema espalhado pelo código: assim
/// os testes conseguem apontar para uma pasta temporária e provar o
/// comportamento sem tocar no Windows de verdade.
/// </summary>
public sealed record ContextoSistema
{
    /// <summary>Raiz dos programas de 64 bits (%ProgramFiles%).</summary>
    public required string PastaProgramFiles { get; init; }

    /// <summary>Dados de roaming do usuário (%APPDATA%).</summary>
    public required string PastaDadosAplicacao { get; init; }

    /// <summary>Área de trabalho do usuário.</summary>
    public required string PastaAreaTrabalho { get; init; }

    /// <summary>Dados comuns da máquina (%ProgramData%), onde fica o log do instalador.</summary>
    public required string PastaDadosComuns { get; init; }

    /// <summary>Pasta onde está o próprio instalador (e, na distribuição, o payload ao lado dele).</summary>
    public required string PastaInstalador { get; init; }

    /// <summary>
    /// Contexto com as pastas reais da máquina. <see cref="Environment.ProcessPath"/> é usado
    /// para achar a pasta do instalador: o app é publicado como arquivo único, então o
    /// payload distribution fica na mesma pasta do .exe.
    /// </summary>
    public static ContextoSistema DoSistema() => new()
    {
        PastaProgramFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        PastaDadosAplicacao = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        PastaAreaTrabalho = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
        PastaDadosComuns = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        PastaInstalador = PastaDoExecutavel(),
    };

    private static string PastaDoExecutavel()
    {
        string? caminho = Environment.ProcessPath;
        string pasta = caminho is null
            ? AppContext.BaseDirectory
            : Path.GetDirectoryName(caminho) ?? AppContext.BaseDirectory;
        return pasta.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }
}
