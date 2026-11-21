using TranalSentinel;
using Xunit;

namespace TranalSentinel.Tests;

/// <summary>
/// O monitor unificado: um interruptor só, e a janela vira overlay sozinha
/// quando um jogo em tela cheia está na frente. Antes, painel e overlay eram
/// dois interruptores mandando na mesma janela, e um derrubava o outro.
/// </summary>
public class MonitorModoRegrasTestes
{
    private static readonly DateTime T0 = new(2026, 11, 20, 22, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void ComecaComoPainel()
    {
        var regra = new MonitorModoRegras();
        Assert.False(regra.EmJogo);
        Assert.False(regra.Observar(false, T0));
    }

    [Fact]
    public void JogoRapidoNaFrenteNaoTrocaOModo()
    {
        // Um alt-tab de meio segundo por cima de um jogo não vira overlay.
        var regra = new MonitorModoRegras();
        Assert.False(regra.Observar(true, T0));
        Assert.False(regra.Observar(true, T0.AddSeconds(0.5)));
        Assert.False(regra.Observar(false, T0.AddSeconds(1)));
        Assert.False(regra.Observar(true, T0.AddSeconds(1.2)));
        Assert.False(regra.Observar(true, T0.AddSeconds(2)));
    }

    [Fact]
    public void JogoQueFicaNaFrenteViraOverlay()
    {
        var regra = new MonitorModoRegras();
        regra.Observar(true, T0);
        Assert.True(regra.Observar(true, T0 + MonitorModoRegras.EntrarNoJogo));
        Assert.True(regra.EmJogo);
    }

    [Fact]
    public void TelaDeCarregamentoNaoFazOPainelPiscar()
    {
        var regra = new MonitorModoRegras();
        regra.Observar(true, T0);
        regra.Observar(true, T0.AddSeconds(2));
        Assert.True(regra.EmJogo);

        // O jogo parou de desenhar por 2 s (menor que SairDoJogo): continua overlay.
        Assert.True(regra.Observar(false, T0.AddSeconds(3)));
        Assert.True(regra.Observar(false, T0.AddSeconds(5)));
        Assert.True(regra.Observar(true, T0.AddSeconds(5.5)));

        // Saiu de verdade: depois de SairDoJogo sem jogo, volta a ser painel.
        regra.Observar(false, T0.AddSeconds(6));
        Assert.False(regra.Observar(false, T0.AddSeconds(6) + MonitorModoRegras.SairDoJogo));
    }

    [Fact]
    public void ReiniciarVoltaParaPainel()
    {
        var regra = new MonitorModoRegras();
        regra.Observar(true, T0);
        regra.Observar(true, T0.AddSeconds(5));
        Assert.True(regra.EmJogo);

        regra.Reiniciar();
        Assert.False(regra.EmJogo);
    }

    [Fact]
    public void LigarComOJogoNaFrenteComecaComoOverlay()
    {
        // Ctrl + Shift + O dentro da partida: sem passar pelo painel.
        var regra = new MonitorModoRegras();
        regra.ComecarEm(true);
        Assert.True(regra.EmJogo);
        Assert.True(regra.Observar(true, T0));

        // Se não era jogo (tela cheia parada), a histerese devolve o painel.
        regra.Observar(false, T0.AddSeconds(1));
        Assert.False(regra.Observar(false, T0.AddSeconds(1) + MonitorModoRegras.SairDoJogo));
    }

    [Fact]
    public void SoEJogoQuandoEntregaQuadrosEOcupaOMonitor()
    {
        Assert.True(MonitorModoRegras.PareceJogo(true, true));
        Assert.False(MonitorModoRegras.PareceJogo(true, false));   // vídeo ou jogo em janela
        Assert.False(MonitorModoRegras.PareceJogo(false, true));   // tela cheia parada (apresentação)
    }

    [Fact]
    public void TelaCheiaESemBordasOcupamOMonitorMaximizadaNao()
    {
        // Monitor 1920x1080 na origem.
        Assert.True(MonitorModoRegras.OcupaOMonitor(0, 0, 1920, 1080, 0, 0, 1920, 1080));
        // Sem bordas com 1 px de sobra ainda conta.
        Assert.True(MonitorModoRegras.OcupaOMonitor(-1, -1, 1921, 1081, 0, 0, 1920, 1080));
        // Maximizada comum: retângulo REAL, 8 px maior que a área de trabalho
        // em cada lado (a borda invisível do Windows), com a barra de tarefas.
        Assert.False(MonitorModoRegras.OcupaOMonitor(-8, -8, 1928, 1048, 0, 0, 1920, 1080));
        // Maximizada sem barra de tarefas (barra oculta ou 2º monitor): ainda não é tela cheia.
        Assert.False(MonitorModoRegras.OcupaOMonitor(-8, -8, 1928, 1088, 0, 0, 1920, 1080));
        // Segundo monitor à direita.
        Assert.True(MonitorModoRegras.OcupaOMonitor(1920, 0, 4480, 1440, 1920, 0, 4480, 1440));
        Assert.False(MonitorModoRegras.OcupaOMonitor(0, 0, 1920, 1080, 1920, 0, 4480, 1440));
    }
}
