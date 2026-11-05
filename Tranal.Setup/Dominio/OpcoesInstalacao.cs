namespace TranalSetup.Dominio;

/// <summary>
/// Opções escolhidas pelo usuário na janela de instalação.
/// São as mesmas opções que o modo silencioso usa, para que os dois caminhos
/// produzam exatamente o mesmo resultado.
/// </summary>
public sealed record OpcoesInstalacao
{
    /// <summary>Grava o valor na chave Run para o app abrir junto com o Windows.</summary>
    public bool IniciarComWindows { get; init; } = true;

    /// <summary>Cria o atalho na Área de Trabalho (o do menu Iniciar é sempre criado).</summary>
    public bool AtalhoAreaTrabalho { get; init; } = true;

    /// <summary>
    /// Substitui uma instalação existente. Desligado, o instalador recusa
    /// e avisa que o TRANAL SENTINEL já está instalado.
    /// </summary>
    public bool ReinstalarSobreInstalacaoExistente { get; init; } = true;
}
