using TranalSetup.Dominio;
using Xunit;

namespace TranalSetup.Tests;

/// <summary>
/// Testes da comparação entre o payload e o que já está instalado.
/// É a decisão que faz a instalação ser idempotente.
/// </summary>
public class ComparadorPayloadTestes
{
    private static readonly DateTime Momento = new(2026, 11, 5, 12, 0, 0);

    [Fact]
    public void SemArquivoNoPayload_ImpedeAInstalacao()
    {
        SituacaoArquivo situacao = ComparadorPayload.Comparar(
            origemExiste: false, 0, Momento,
            destinoExiste: false, 0, Momento);

        Assert.Equal(SituacaoArquivo.AusenteNoPayload, situacao);
    }

    [Fact]
    public void SemArquivoInstalado_Copia()
    {
        SituacaoArquivo situacao = ComparadorPayload.Comparar(
            origemExiste: true, 100, Momento,
            destinoExiste: false, 0, Momento);

        Assert.Equal(SituacaoArquivo.Copiar, situacao);
    }

    [Fact]
    public void MesmoTamanhoEMesmaData_NaoFazNada()
    {
        SituacaoArquivo situacao = ComparadorPayload.Comparar(
            origemExiste: true, 100, Momento,
            destinoExiste: true, 100, Momento);

        Assert.Equal(SituacaoArquivo.Igual, situacao);
    }

    [Fact]
    public void TamanhoDiferente_Substitui()
    {
        SituacaoArquivo situacao = ComparadorPayload.Comparar(
            origemExiste: true, 100, Momento,
            destinoExiste: true, 90, Momento);

        Assert.Equal(SituacaoArquivo.Substituir, situacao);
    }

    [Fact]
    public void DataDiferente_Substitui()
    {
        SituacaoArquivo situacao = ComparadorPayload.Comparar(
            origemExiste: true, 100, Momento,
            destinoExiste: true, 100, Momento.AddMinutes(1));

        Assert.Equal(SituacaoArquivo.Substituir, situacao);
    }

    [Fact]
    public void PayloadAusente_ComArquivoInstalado_ContinuaAusente()
    {
        SituacaoArquivo situacao = ComparadorPayload.Comparar(
            origemExiste: false, 0, Momento,
            destinoExiste: true, 100, Momento);

        Assert.Equal(SituacaoArquivo.AusenteNoPayload, situacao);
    }
}
