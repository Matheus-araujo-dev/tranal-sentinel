namespace TranalSetup.Dominio;

/// <summary>
/// Conteúdo de um atalho (.lnk) do Windows, sem nenhuma referência ao disco.
/// A parte que grava o arquivo fica na infraestrutura: aqui é só o "o que" do atalho.
/// </summary>
public sealed record DadosAtalho(
    /// <summary>Executável para onde o atalho aponta.</summary>
    string Alvo,
    /// <summary>Pasta de trabalho aberta junto com o app.</summary>
    string DiretorioTrabalho,
    /// <summary>Descrição exibida na lista de atalhos.</summary>
    string Descricao,
    /// <summary>Ícone do atalho (o próprio executável do app).</summary>
    string Icone);
