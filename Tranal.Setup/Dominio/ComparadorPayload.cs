namespace TranalSetup.Dominio;

/// <summary>
/// Compara o arquivo do payload com o arquivo já instalado.
///
/// É a decisão que faz a instalação ser idempotente: instalar duas vezes não
/// precisa reescrever o que já é igual. Recebe só números e datas (nada de
/// disco), então é uma função pura e testável.
/// </summary>
public static class ComparadorPayload
{
    /// <summary>
    /// Decide o que fazer com um arquivo do payload.
    /// A comparação usa tamanho e data de modificação: é o mesmo critério do
    /// instalador em disco, e é barato (não precisa ler o conteúdo inteiro).
    /// </summary>
    /// <param name="origemExiste">Se o arquivo existe no payload.</param>
    /// <param name="tamanhoOrigem">Tamanho do arquivo do payload.</param>
    /// <param name="modificadoOrigem">Data de modificação do arquivo do payload.</param>
    /// <param name="destinoExiste">Se o arquivo já está instalado.</param>
    /// <param name="tamanhoDestino">Tamanho do arquivo instalado.</param>
    /// <param name="modificadoDestino">Data de modificação do arquivo instalado.</param>
    public static SituacaoArquivo Comparar(
        bool origemExiste,
        long tamanhoOrigem,
        DateTime modificadoOrigem,
        bool destinoExiste,
        long tamanhoDestino,
        DateTime modificadoDestino)
    {
        if (!origemExiste) return SituacaoArquivo.AusenteNoPayload;
        if (!destinoExiste) return SituacaoArquivo.Copiar;
        if (tamanhoOrigem == tamanhoDestino && modificadoOrigem == modificadoDestino)
            return SituacaoArquivo.Igual;
        return SituacaoArquivo.Substituir;
    }
}
