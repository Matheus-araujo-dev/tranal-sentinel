namespace TranalSetup.Dominio;

/// <summary>Situação de um arquivo do payload em relação ao que já está instalado.</summary>
public enum SituacaoArquivo
{
    /// <summary>O arquivo existe no payload e ainda não está instalado: vai ser copiado.</summary>
    Copiar,

    /// <summary>O arquivo existe no payload e no destino, mas é diferente: vai ser substituído.</summary>
    Substituir,

    /// <summary>O arquivo do payload é idêntico ao instalado: não precisa ser copiado de novo.</summary>
    Igual,

    /// <summary>O arquivo não existe no payload: a instalação não pode seguir.</summary>
    AusenteNoPayload,
}

/// <summary>Um arquivo do payload e o que vai acontecer com ele.</summary>
public sealed record ArquivoPlano(
    /// <summary>Nome do arquivo (só o nome, sem pasta).</summary>
    string Nome,
    /// <summary>Arquivo de origem (no payload).</summary>
    string Origem,
    /// <summary>Arquivo de destino (na pasta de instalação).</summary>
    string Destino,
    /// <summary>O que vai acontecer com o arquivo.</summary>
    SituacaoArquivo Situacao,
    /// <summary>Tamanho do arquivo de origem em bytes (0 quando não existe).</summary>
    long TamanhoOrigem,
    /// <summary>Data da última modificação da origem.</summary>
    DateTime ModificadoOrigem,
    /// <summary>Tamanho do arquivo instalado em bytes (0 quando não existe).</summary>
    long TamanhoInstalado,
    /// <summary>Data da última modificação do arquivo instalado.</summary>
    DateTime ModificadoInstalado)
{
    /// <summary>Texto curto da situação, usado no plano e no log.</summary>
    public string RotuloSituacao => Situacao switch
    {
        SituacaoArquivo.Copiar => "copiar",
        SituacaoArquivo.Substituir => "substituir",
        SituacaoArquivo.Igual => "já está igual",
        SituacaoArquivo.AusenteNoPayload => "ausente no payload",
        _ => Situacao.ToString(),
    };
}
