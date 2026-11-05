namespace TranalSetup.Dominio;

/// <summary>Um atalho que o instalador vai criar (ou que a desinstalação vai remover).</summary>
public sealed record PlanoAtalho(
    /// <summary>Caminho do arquivo .lnk.</summary>
    string Caminho,
    /// <summary>Descrição curta (aparece no plano e no log).</summary>
    string Descricao,
    /// <summary>Conteúdo do atalho.</summary>
    DadosAtalho Dados,
    /// <summary>Se o atalho vai ser criado (falso quando a opção está desligada).</summary>
    bool SeraCriado);

/// <summary>
/// Plano completo da instalação: tudo que o instalador faria, sem fazer.
///
/// O mesmo plano serve para instalar de verdade e para o modo --simular, então
/// o usuário vê exatamente o que vai acontecer antes de confirmar.
/// </summary>
public sealed class PlanoInstalacao
{
    /// <summary>Contexto (pastas do sistema) usado no plano.</summary>
    public required ContextoSistema Contexto { get; init; }

    /// <summary>Pasta de onde vêm os arquivos do app.</summary>
    public required string PastaPayload { get; init; }

    /// <summary>Opções escolhidas pelo usuário.</summary>
    public required OpcoesInstalacao Opcoes { get; init; }

    /// <summary>
    /// Onde o app será instalado. Vem das opções do usuário já resolvida: quem
    /// decide é o <see cref="DestinoInstalacao"/>.
    /// </summary>
    public required DestinoInstalacao Destino { get; init; }

    /// <summary>Executável do app depois de instalado.</summary>
    public required string CaminhoExecutavel { get; init; }

    /// <summary>Backup do executável anterior.</summary>
    public required string CaminhoBackup { get; init; }

    /// <summary>Instalador copiado para a pasta de instalação (é o que desinstala).</summary>
    public required string CaminhoInstaladorInstalado { get; init; }

    /// <summary>Arquivos do payload e o que acontece com cada um.</summary>
    public required IReadOnlyList<ArquivoPlano> Arquivos { get; init; }

    /// <summary>Atalhos a criar.</summary>
    public required IReadOnlyList<PlanoAtalho> Atalhos { get; init; }

    /// <summary>Nome da tarefa agendada de inicialização (null quando a opção está desligada).</summary>
    public required string? NomeTarefaIniciar { get; init; }

    /// <summary>Descrição da tarefa agendada, para o plano e o log (null quando desligada).</summary>
    public required string? DescricaoTarefaIniciar { get; init; }

    /// <summary>Entrada de desinstalação em "Apps e recursos".</summary>
    public required EntradaDesinstalacao EntradaDesinstalacao { get; init; }

    /// <summary>Se já existe uma instalação no destino.</summary>
    public required bool InstalacaoExistente { get; init; }

    /// <summary>
    /// Pasta de uma instalação anterior que está em outro lugar (null quando
    /// não há). Quando existe, trocar de pasta é um movimento: a instalação
    /// remove a anterior depois que a nova está no lugar.
    /// </summary>
    public required string? PastaInstalacaoAnterior { get; init; }

    /// <summary>Se o payload tem todos os arquivos necessários.</summary>
    public required bool PayloadCompleto { get; init; }

    /// <summary>Versão do app no payload (vem do executável).</summary>
    public required string VersaoPayload { get; init; }

    /// <summary>Arquivos que realmente precisam ser gravados no destino.</summary>
    public IReadOnlyList<ArquivoPlano> ArquivosParaGravar => Arquivos
        .Where(a => a.Situacao is SituacaoArquivo.Copiar or SituacaoArquivo.Substituir)
        .ToList();

    /// <summary>Arquivos que faltam no payload (impedem a instalação).</summary>
    public IReadOnlyList<string> ArquivosAusentes => Arquivos
        .Where(a => a.Situacao == SituacaoArquivo.AusenteNoPayload)
        .Select(a => a.Nome)
        .ToList();
}
