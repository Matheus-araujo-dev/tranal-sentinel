using System.IO;
using TranalSetup.Dominio;

namespace TranalSetup.Infra;

/// <summary>
/// Criação de atalhos pelo Windows Script Host (o mesmo mecanismo usado pelo
/// "Criar atalho" do Explorer). Não precisa de nenhuma biblioteca extra.
/// </summary>
public sealed class AtalhoWindows : IAtalho
{
    /// <inheritdoc />
    public void Criar(string caminho, DadosAtalho dados)
    {
        string? pasta = Path.GetDirectoryName(caminho);
        if (!string.IsNullOrEmpty(pasta)) Directory.CreateDirectory(pasta);

        Type? tipo = Type.GetTypeFromProgID("WScript.Shell")
            ?? throw new InvalidOperationException(
                "O Windows Script Host não está disponível neste computador: não foi possível criar o atalho.");

        dynamic shell = Activator.CreateInstance(tipo)!;
        dynamic atalho = shell.CreateShortcut(caminho);
        atalho.TargetPath = dados.Alvo;
        atalho.WorkingDirectory = dados.DiretorioTrabalho;
        atalho.Description = dados.Descricao;
        atalho.IconLocation = dados.Icone;
        atalho.Save();
    }

    /// <inheritdoc />
    public bool Existe(string caminho) => File.Exists(caminho);

    /// <inheritdoc />
    public void Remover(string caminho)
    {
        if (File.Exists(caminho)) File.Delete(caminho);
    }
}
