using System.IO;

namespace TranalSetup.Dominio;

/// <summary>
/// Nomes e caminhos fixos da instalação do TRANAL SENTINEL.
///
/// Tudo aqui é função pura: dado um <see cref="ContextoSistema"/>, devolve o caminho.
/// Assim o mesmo cálculo serve para a instalação real, para o plano do modo
/// --simular e para os testes, sem risco de um lugar calcular diferente do outro.
/// </summary>
public static class CaminhosInstalacao
{
    /// <summary>Nome da pasta de instalação dentro de %ProgramFiles%.</summary>
    public const string NomePasta = "Tranal Sentinel";

    /// <summary>Nome do executável do app instalado.</summary>
    public const string NomeExecutavel = "TranalSentinel.exe";

    /// <summary>Nome do arquivo de instruções que vai junto do app.</summary>
    public const string NomeLeiaMe = "LEIA-ME.txt";

    /// <summary>Nome do instalador copiado para a pasta de instalação (é o que desinstala).</summary>
    public const string NomeInstalador = "TranalSetup.exe";

    /// <summary>Nome do atalho criado no menu Iniciar e na Área de Trabalho.</summary>
    public const string NomeAtalho = "TRANAL SENTINEL.lnk";

    /// <summary>
    /// Chave Run do usuário.
    /// </summary>
    /// <remarks>
    /// O TRANAL SENTINEL <b>não</b> usa a chave Run para iniciar junto com o
    /// Windows: o app exige administrador e um <c>requireAdministrator</c> na
    /// Run pediria UAC a cada login. Quem faz isso é a tarefa agendada
    /// (<see cref="TarefaInicializacao"/>). Aqui a chave é usada só para
    /// apagar um valor antigo que tenha sobrado de uma instalação manual.
    /// </remarks>
    public const string ChaveIniciar = @"Software\Microsoft\Windows\CurrentVersion\Run";

    /// <summary>Nome do valor na chave Run que o instalador limparia se sobrar.</summary>
    public const string NomeValorIniciar = "TranalSentinel";

    /// <summary>Argumento que o app principal entende para abrir direto na bandeja.</summary>
    public const string ArgumentoIniciarComoSistema = "--iniciar-como-sistema";

    /// <summary>Chave de desinstalação em HKLM (é o que faz o TRANAL SENTINEL aparecer em "Apps e recursos").</summary>
    public const string ChaveDesinstalacao = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\Tranal Sentinel";

    /// <summary>Nome do arquivo de log do instalador.</summary>
    public const string NomeLog = "instalador.log";

    /// <summary>Extensão do backup do executável instalado.</summary>
    public const string ExtensaoBackup = ".bak";

    /// <summary>Pasta de destino da instalação (%ProgramFiles%\Tranal Sentinel).</summary>
    public static string PastaDestino(ContextoSistema contexto)
        => Path.Combine(contexto.PastaProgramFiles, NomePasta);

    /// <summary>Caminho do executável do app dentro da pasta de instalação.</summary>
    public static string ExecutavelInstalado(ContextoSistema contexto)
        => Path.Combine(PastaDestino(contexto), NomeExecutavel);

    /// <summary>Caminho do instalador dentro da pasta de instalação (é o alvo do botão Desinstalar).</summary>
    public static string InstaladorInstalado(ContextoSistema contexto)
        => Path.Combine(PastaDestino(contexto), NomeInstalador);

    /// <summary>Caminho do backup do executável anterior.</summary>
    public static string BackupExecutavel(ContextoSistema contexto)
        => ExecutavelInstalado(contexto) + ExtensaoBackup;

    /// <summary>Caminho do atalho do menu Iniciar.</summary>
    public static string AtalhoMenuIniciar(ContextoSistema contexto)
        => Path.Combine(contexto.PastaDadosAplicacao, @"Microsoft\Windows\Start Menu\Programs", NomeAtalho);

    /// <summary>Caminho do atalho da Área de Trabalho.</summary>
    public static string AtalhoAreaTrabalho(ContextoSistema contexto)
        => Path.Combine(contexto.PastaAreaTrabalho, NomeAtalho);

    /// <summary>Caminho do log do instalador (%ProgramData%\Tranal Sentinel\instalador.log).</summary>
    public static string ArquivoLog(ContextoSistema contexto)
        => Path.Combine(contexto.PastaDadosComuns, NomePasta, NomeLog);

    /// <summary>
    /// Pasta de configurações do usuário (%AppData%\Tranal Sentinel). A desinstalação
    /// NÃO pode apagar esta pasta: são as preferências e o histórico do usuário.
    /// </summary>
    public static string PastaConfiguracoes(ContextoSistema contexto)
        => Path.Combine(contexto.PastaDadosAplicacao, "TranalSentinel");

    /// <summary>
    /// Dados do atalho (alvo, pasta de trabalho, descrição e ícone). O ícone é o
    /// próprio executável: o atalho nasce com a arte do app, sem arquivo extra.
    /// </summary>
    public static DadosAtalho DadosDoAtalho(string caminhoExecutavel) => new(
        Alvo: caminhoExecutavel,
        DiretorioTrabalho: Path.GetDirectoryName(caminhoExecutavel) ?? "",
        Descricao: "TRANAL SENTINEL",
        Icone: caminhoExecutavel);

    /// <summary>
    /// Entrada de desinstalação que aparece em "Apps e recursos". O UninstallString
    /// aponta para o instalador copiado na pasta de instalação, porque só ele sabe
    /// desfazer tudo que o instalador fez.
    /// </summary>
    public static EntradaDesinstalacao CriarEntradaDesinstalacao(
        string caminhoExecutavel, string caminhoInstalador, string versao) => new(
        DisplayName: "TRANAL SENTINEL",
        DisplayVersion: string.IsNullOrWhiteSpace(versao) ? "0.0.0" : versao,
        Publisher: "TRANAL",
        InstallLocation: Path.GetDirectoryName(caminhoExecutavel) ?? "",
        DisplayIcon: caminhoExecutavel,
        UninstallString: $"\"{caminhoInstalador}\" --desinstalar");
}
