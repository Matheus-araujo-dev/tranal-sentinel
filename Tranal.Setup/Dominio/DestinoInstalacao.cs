using System.IO;

namespace TranalSetup.Dominio;

/// <summary>
/// Onde o TRANAL SENTINEL será instalado.
///
/// Este é o único lugar do instalador que interpreta "onde instalar". O
/// executável, o backup, o instalador copiado, os atalhos e a entrada de
/// desinstalação derivam todos desta pasta: assim não existem duas respostas
/// diferentes para a mesma pergunta em partes diferentes do instalador.
///
/// O construtor é privado de propósito: toda instância deste tipo já passou
/// por uma das fábricas (padrão ou escolha do usuário), ou seja, o caminho é
/// sempre válido e sempre completo.
/// </summary>
public sealed record DestinoInstalacao
{
    private DestinoInstalacao(string pasta) => Pasta = pasta;

    /// <summary>Pasta final (absoluta) da instalação.</summary>
    public string Pasta { get; }

    /// <summary>Destino padrão: %ProgramFiles%\Tranal Sentinel.</summary>
    public static DestinoInstalacao Padrao(ContextoSistema contexto)
        => new(CaminhosInstalacao.PastaPadrao(contexto));

    /// <summary>
    /// Destino a partir da escolha do usuário.
    ///
    /// O caminho informado é a pasta FINAL da instalação: o que está escrito
    /// na tela é exatamente o que será criado.
    /// </summary>
    /// <exception cref="ArgumentException">O caminho informado não serve.</exception>
    public static DestinoInstalacao DaEscolha(string caminho)
    {
        string? erro = Erro(caminho);
        if (erro is not null) throw new ArgumentException(erro, nameof(caminho));

        return new DestinoInstalacao(Normalizar(caminho));
    }

    /// <summary>
    /// Erro no caminho escolhido (null quando ele serve). Serve para a tela
    /// avisar antes de instalar, sem precisar de try/catch.
    /// </summary>
    public static string? Erro(string? caminho)
    {
        if (string.IsNullOrWhiteSpace(caminho))
            return "Escolha a pasta de instalação.";

        string texto = caminho.Trim().Trim('"');

        if (texto.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
            return "O caminho tem caracteres que o Windows não aceita.";

        if (!Path.IsPathFullyQualified(texto))
            return "O caminho precisa ser completo, por exemplo C:\\Program Files.";

        string pasta;
        try
        {
            pasta = Normalizar(texto);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or IOException)
        {
            // IOException cobre o PathTooLongException: um caminho de 32 mil
            // caracteres estouraria aqui e derrubaria a tela.
            return "O caminho informado não é válido.";
        }

        string? raiz = Path.GetPathRoot(pasta);
        if (!string.IsNullOrEmpty(raiz) && raiz.StartsWith(@"\\", StringComparison.Ordinal))
        {
            // "\\?\UNC\server\pasta" e "\\.\UNC\..." também são rede.
            if (raiz.Contains("UNC", StringComparison.OrdinalIgnoreCase))
                return "Não dá para instalar numa pasta de rede. Escolha uma pasta deste computador.";

            // "\\?\C:\..." e "\\.\C:\..." são prefixos do Windows (caminho
            // estendido e device), não pastas de rede — mas também não são
            // pastas onde se instala um app.
            if (raiz.StartsWith(@"\\?", StringComparison.Ordinal) || raiz.StartsWith(@"\\.\", StringComparison.Ordinal))
                return "Caminho do Windows (\\\\? ou \\\\) não é aceito como pasta de instalação.";

            // A rede é conferida DEPOIS de normalizar: "//servidor\pasta"
            // escaparia se a checagem olhasse só o texto digitado.
            return "Não dá para instalar numa pasta de rede. Escolha uma pasta deste computador.";
        }

        if (!string.IsNullOrEmpty(raiz) && string.Equals(pasta, raiz, StringComparison.OrdinalIgnoreCase))
            return "Escolha uma pasta dentro da unidade, não a raiz dela.";

        // O Windows ainda tem o limite de 260 caracteres (MAX_PATH) para a
        // maioria das APIs: um caminho mais longo passa na tela e só falha na
        // hora de criar a pasta. O que conta é o caminho do executável, não o
        // da pasta.
        string caminhoCompleto = Path.Combine(pasta, CaminhosInstalacao.NomeExecutavel);
        if (caminhoCompleto.Length > 259)
            return "O caminho é longo demais para o Windows: o executável passaria de 259 caracteres.";

        // Cada parte do caminho tem que ser um nome de pasta válido. A raiz da
        // unidade fica de fora: o "C:" dela é nome de unidade, não nome de pasta.
        string resto = pasta[(raiz ?? string.Empty).Length..];
        foreach (string parte in resto.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        {
            if (parte.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                return "O caminho tem caracteres que o Windows não aceita.";
        }

        return null;
    }

    /// <summary>
    /// Regra do botão "escolher pasta": o TRANAL SENTINEL é instalado dentro da
    /// pasta escolhida, numa subpasta com o nome do produto. Se a pasta escolhida
    /// já for a pasta do produto, ela é usada como está (reinstalar no mesmo
    /// lugar não cria "Tranal Sentinel\Tranal Sentinel").
    /// </summary>
    public static string PastaDentroDe(string pastaEscolhida)
    {
        string pasta = Normalizar(pastaEscolhida);
        string nome = Path.GetFileName(pasta);
        if (string.Equals(nome, CaminhosInstalacao.NomePasta, StringComparison.OrdinalIgnoreCase))
            return pasta;

        return Path.Combine(pasta, CaminhosInstalacao.NomePasta);
    }

    /// <summary>Normaliza o caminho: completo, sem barra no fim, sem aspas em volta.</summary>
    private static string Normalizar(string caminho)
    {
        string completo = Path.GetFullPath(caminho.Trim().Trim('"'));
        string raiz = Path.GetPathRoot(completo) ?? completo;
        string semBarra = completo.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return semBarra.Length < raiz.Length ? raiz : semBarra;
    }
}
