using Xunit;

namespace TranalSentinel.Tests;

/// <summary>
/// O motivo de o driver PawnIO não abrir, e a linha de timings das
/// informações do sistema. Medido no PC do dono em 22/11/2026: driver
/// instalado, serviço rodando, e o dispositivo sumido (erro 2). O app dizia
/// "não está instalado".
/// </summary>
public class PawnIoEstadoTestes
{
    [Fact]
    public void Instalado_SemDispositivo_EhTravado_ENaoNaoInstalado()
    {
        string motivo = PawnIoEstado.MotivoDeNaoAbrir(instalado: true, PawnIoEstado.ErroSemDispositivo);

        Assert.True(PawnIoEstado.Travado(true, PawnIoEstado.ErroSemDispositivo));
        Assert.Contains("travado", motivo);
        Assert.DoesNotContain("não está instalado", motivo);
        Assert.Contains("CORRIGIR", motivo);
    }

    [Fact]
    public void NaoInstalado_DizQueNaoEstaInstalado()
    {
        Assert.Equal("O driver PawnIO não está instalado neste computador.",
            PawnIoEstado.MotivoDeNaoAbrir(instalado: false, PawnIoEstado.ErroSemDispositivo));
        Assert.False(PawnIoEstado.Travado(false, PawnIoEstado.ErroSemDispositivo));
    }

    [Fact]
    public void AcessoNegado_FalaDePermissao_ENaoEhTravado()
    {
        Assert.Contains("administrador", PawnIoEstado.MotivoDeNaoAbrir(true, PawnIoEstado.ErroAcessoNegado));
        Assert.False(PawnIoEstado.Travado(true, PawnIoEstado.ErroAcessoNegado));
    }

    [Fact]
    public void OutroErro_MostraOCodigoDoWindows()
    {
        Assert.Contains("erro 31", PawnIoEstado.MotivoDeNaoAbrir(true, 31));
    }

    private static readonly DateTime Agora = new(2026, 11, 22, 23, 50, 0, DateTimeKind.Utc);

    [Fact]
    public void Reabertura_DriverVoltou_Reabre()
    {
        var (reabrir, pendente) = PawnIoEstado.DecidirReabertura(true, false, true, false, Agora, DateTime.MinValue);
        Assert.True(reabrir);
        Assert.False(pendente);
    }

    [Fact]
    public void Reabertura_DriverTravado_MarcaPendenteENaoReabre()
    {
        Assert.Equal((false, true), PawnIoEstado.DecidirReabertura(false, true, false, false, Agora, DateTime.MinValue));
    }

    [Fact]
    public void Reabertura_DuranteOReparo_Espera()
    {
        // O próprio reparo reabre: duas reaberturas seguidas podiam deixar um coletor órfão.
        Assert.Equal((false, true), PawnIoEstado.DecidirReabertura(true, false, true, true, Agora, DateTime.MinValue));
    }

    [Fact]
    public void Reabertura_ReabriuHaPouco_EsperaOIntervalo()
    {
        var (reabrir, pendente) = PawnIoEstado.DecidirReabertura(true, false, true, false, Agora, Agora.AddMinutes(-1));
        Assert.False(reabrir);
        Assert.True(pendente);
    }

    [Fact]
    public void Reabertura_SemTerVistoODriverTravado_NaoReabre()
    {
        Assert.Equal((false, false), PawnIoEstado.DecidirReabertura(false, false, true, false, Agora, DateTime.MinValue));
    }

    [Fact]
    public void LinhaDosTimings_ComTemposAtivos_MostraOCl()
    {
        var t = new TimingsAtivos
        {
            VelocidadeMts = 3466, ClockMhz = 1733, CommandRate = 1,
            TCl = 18, TRcdLeitura = 22, TRp = 22, TRas = 42,
        };

        Assert.Equal(
            "CL 18 · 18-22-22-42 · 1T a 3466 MT/s (tempos ativos, lidos do controlador de memória). O detalhe está em TRANAL MEMÓRIA.",
            MemoriaAtivosTexto.LinhaDasInformacoes(t, null));
    }

    [Fact]
    public void LinhaDosTimings_SemTempos_DizOMotivo_ENaoMandaParaOCpuZ()
    {
        string linha = MemoriaAtivosTexto.LinhaDasInformacoes(null, "O driver PawnIO está instalado, mas travado.");

        Assert.StartsWith("Não deu para ler: O driver PawnIO está instalado, mas travado.", linha);
        Assert.DoesNotContain("CPU-Z", linha);
    }
}
