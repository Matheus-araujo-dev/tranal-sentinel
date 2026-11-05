using System.Diagnostics;
using System.IO;
using TranalSetup.Dominio;

namespace TranalSetup.Tests;

/// <summary>
/// Ambiente temporário dos testes: uma pasta naTemp com as pastas do Windows
/// apontando para dentro dela. Nada do teste toca o sistema de verdade.
/// </summary>
public sealed class AmbienteTeste : IDisposable
{
    /// <summary>Cria o ambiente (as pastas são criadas sob demanda).</summary>
    public AmbienteTeste()
    {
        Raiz = Path.Combine(Path.GetTempPath(), "TranalSetupTestes", Guid.NewGuid().ToString("N"));
        Contexto = new ContextoSistema
        {
            PastaProgramFiles = Path.Combine(Raiz, "Program Files"),
            PastaDadosAplicacao = Path.Combine(Raiz, "AppData", "Roaming"),
            PastaAreaTrabalho = Path.Combine(Raiz, "Desktop"),
            PastaDadosComuns = Path.Combine(Raiz, "ProgramData"),
            PastaInstalador = Path.Combine(Raiz, "Payload"),
        };
        PastaPayload = Contexto.PastaInstalador;
        Directory.CreateDirectory(PastaPayload);
    }

    /// <summary>Raiz da pasta temporária do teste.</summary>
    public string Raiz { get; }

    /// <summary>Contexto do sistema apontando para dentro da pasta temporária.</summary>
    public ContextoSistema Contexto { get; }

    /// <summary>Pasta do payload do teste.</summary>
    public string PastaPayload { get; }

    /// <summary>Pasta de destino da instalação (a de verdade, dentro da temp).</summary>
    public string PastaDestino => CaminhosInstalacao.PastaPadrao(Contexto);

    /// <summary>Executável instalado (dentro da temp).</summary>
    public string CaminhoExecutavelInstalado => CaminhosInstalacao.ExecutavelInstalado(PastaDestino);

    /// <summary>Opções de instalação padrão (destino padrão, tudo ligado).</summary>
    public OpcoesInstalacao Opcoes() => new() { Destino = DestinoInstalacao.Padrao(Contexto) };

    /// <summary>Opções de instalação em uma pasta escolhida pelo usuário.</summary>
    public OpcoesInstalacao Opcoes(string pastaDestino) => new()
    {
        Destino = DestinoInstalacao.DaEscolha(pastaDestino),
    };

    /// <summary>Instalador que o serviço copia para a pasta de instalação.</summary>
    public string CaminhoInstaladorOrigem => Path.Combine(PastaPayload, "TranalSetup.exe");

    /// <summary>Cria o payload (o app e o LEIA-ME) dentro da pasta temporária.</summary>
    public string CriarPayload(bool comExecutavel = true, bool comLeiaMe = true)
    {
        if (comExecutavel) File.WriteAllText(Path.Combine(PastaPayload, "TranalSentinel.exe"), "conteudo do app");
        if (comLeiaMe) File.WriteAllText(Path.Combine(PastaPayload, "LEIA-ME.txt"), "leia me");
        File.WriteAllText(CaminhoInstaladorOrigem, "instalador de teste");
        return PastaPayload;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Raiz)) Directory.Delete(Raiz, recursive: true);
        }
        catch (Exception ex)
        {
            // A pasta de teste é temporária: se não der para apagar, o Windows
            // limpa depois. Não vale a pena falhar o teste por causa disso.
            Debug.WriteLine($"[teste] não foi possível apagar {Raiz}: {ex.Message}");
        }
    }
}
