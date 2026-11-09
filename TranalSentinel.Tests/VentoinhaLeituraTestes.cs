using System.IO;
using LibreHardwareMonitor.Hardware;
using Xunit;

namespace TranalSentinel.Tests;

/// <summary>
/// Testes da leitura das ventoinhas: o que o app faz quando o sensor da
/// ventoinha para de responder.
///
/// <para>
/// A regra da casa: <b>número errado é pior que número ausente</b>. Uma
/// ventoinha que parou de dar leitura não pode continuar valendo com o RPM
/// velho — nem no cartão da tela, nem na contagem do dia, nem no alerta. O
/// app tem que dizer "sem leitura" e dizer <b>quando</b> foi a última
/// medida boa.
///
/// <para>
/// A classe fica na coleção "Banco": o monitor das ventoinhas lê o banco
/// (o recorde guardado e la base do dia).
/// </para>
/// </summary>
[Collection("Banco")]
public class VentoinhaLeituraTestes : IDisposable
{
    private string? _pastaBanco;

    public void Dispose()
    {
        BancoDeTeste.Restaurar();

        if (_pastaBanco == null) return;
        try
        {
            if (Directory.Exists(_pastaBanco)) Directory.Delete(_pastaBanco, true);
        }
        catch
        {
            // A pasta é temporária: se o Windows ainda estiver segurando o
            // arquivo, ela some sozinha depois.
        }
    }

    private void UsarPastasTemporarias()
    {
        (_pastaBanco, _) = BancoDeTeste.Isolar();
    }

    /// <summary>
    /// O instante base é o de verdade: o cartão mede a duração da queda
    /// contra o relógio do Windows, e um instante fictício faria a queda
    /// parecer velha (e já confirmada) sem ser.
    /// </summary>
    private static DateTime Agora => DateTime.Now;

    /// <summary>A linha do sensor da ventoinha da placa-mãe (null = sem leitura).</summary>
    private static SensorRow LinhaDaVentoinha(float? rpm)
    {
        var linha = new SensorRow
        {
            Id = "ITE IT8655E|Fan #1",
            Hardware = "ITE IT8655E",
            Name = "Fan #1",
            Type = SensorType.Fan,
            HardwareType = HardwareType.SuperIO,
        };

        linha.Update(rpm, null, null);
        return linha;
    }

    // ------------------------------------------- a ventoinha que parou de ler

    [Fact]
    public void AVentoinhaQueParouDeDarLeitura_NaoContinuaValendoComORpmVelho()
    {
        UsarPastasTemporarias();

        var monitor = new MonitorVentoinhas();

        // A primeira leva traz a leitura.
        DateTime primeira = Agora;
        monitor.Registrar(new[] { LinhaDaVentoinha(3000f) }, 85f, null, primeira, primeira);

        // A segunda leva não traz a ventoinha: o sensor parou de responder.
        DateTime segunda = Agora;
        monitor.Registrar(Array.Empty<SensorRow>(), 85f, null, segunda, segunda);

        var cartao = Assert.Single(monitor.Cartoes());

        // O RPM velho não pode aparecer como "RPM AGORA": o número mostrado
        // é de outro instante, e a tela não mostra a hora dele.
        Assert.Null(cartao.RpmAtual);
        Assert.Equal(VentoinhaEstado.SemLeitura, cartao.Estado);
        Assert.Equal("SEM LEITURA", cartao.Titulo);
    }

    [Fact]
    public void AVentoinhaQueParouDeDarLeitura_DizQuandoFoiAMedidaBoa()
    {
        UsarPastasTemporarias();

        var monitor = new MonitorVentoinhas();

        DateTime primeira = Agora;
        monitor.Registrar(new[] { LinhaDaVentoinha(3000f) }, 85f, null, primeira, primeira);

        DateTime segunda = Agora;
        monitor.Registrar(Array.Empty<SensorRow>(), 85f, null, segunda, segunda);

        var cartao = Assert.Single(monitor.Cartoes());

        // A frase que o app já tinha no núcleo ("a última medida dela foi
        // em ...") precisa aparecer: sem a hora, o usuário não sabe se a
        // ventoinha parou agora ou há uma hora.
        Assert.Contains("não está dando leitura", cartao.Texto);
        Assert.Contains(NumeroTexto.DataHora(primeira), cartao.Texto);
    }

    [Fact]
    public void AVentoinhaQueParouDeDarLeitura_NaoContaMedicaoNoDia()
    {
        UsarPastasTemporarias();

        var monitor = new MonitorVentoinhas();

        DateTime primeira = Agora;
        monitor.Registrar(new[] { LinhaDaVentoinha(3000f) }, 85f, null, primeira, primeira);

        DateTime segunda = Agora;
        monitor.Registrar(Array.Empty<SensorRow>(), 85f, null, segunda, segunda);

        var cartao = Assert.Single(monitor.Cartoes());

        // O dia conta medição, não tique: a medição que não existiu não
        // pode entrar no histórico que o usuário vai comparar.
        Assert.Equal(1, cartao.AmostrasDoDia);
    }

    [Fact]
    public void AVentoinhaQueParouDeDarLeitura_NaoFicaObservandoAQueda()
    {
        UsarPastasTemporarias();

        var monitor = new MonitorVentoinhas();

        // O recorde nasce alto.
        DateTime primeira = Agora;
        monitor.Registrar(new[] { LinhaDaVentoinha(3392f) }, 85f, null, primeira, primeira);

        // Depois a ventoinha cai para menos de 80% do recorde: queda
        // valendo, mas ainda sem os 5 minutos que confirmam o desgaste.
        DateTime segunda = Agora;
        monitor.Registrar(new[] { LinhaDaVentoinha(2000f) }, 85f, null, segunda, segunda);

        var observando = Assert.Single(monitor.Cartoes());
        Assert.Equal(VentoinhaEstado.Observando, observando.Estado);

        // Agora o sensor para. A queda não está mais sendo observada: o
        // app não pode continuar contando os 5 minutos de uma condição
        // que ele não está vendo.
        DateTime terceira = Agora;
        monitor.Registrar(Array.Empty<SensorRow>(), 85f, null, terceira, terceira);

        var cartao = Assert.Single(monitor.Cartoes());
        Assert.Equal(VentoinhaEstado.SemLeitura, cartao.Estado);
        Assert.NotEqual(VentoinhaEstado.Observando, cartao.Estado);
    }

    [Fact]
    public void AVentoinhaQueVoltouADarLeitura_VoltaAoVereditoNormal()
    {
        UsarPastasTemporarias();

        var monitor = new MonitorVentoinhas();

        DateTime primeira = Agora;
        monitor.Registrar(new[] { LinhaDaVentoinha(3000f) }, 85f, null, primeira, primeira);

        DateTime segunda = Agora;
        monitor.Registrar(Array.Empty<SensorRow>(), 85f, null, segunda, segunda);

        var semLeitura = Assert.Single(monitor.Cartoes());
        Assert.Equal(VentoinhaEstado.SemLeitura, semLeitura.Estado);

        // O sensor voltou: a ventoinha volta a ter RPM e a contar medição.
        DateTime terceira = Agora;
        monitor.Registrar(new[] { LinhaDaVentoinha(3100f) }, 85f, null, terceira, terceira);

        var cartao = Assert.Single(monitor.Cartoes());
        Assert.Equal(3100f, cartao.RpmAtual);
        Assert.Equal(VentoinhaEstado.Saudavel, cartao.Estado);
        Assert.Equal(2, cartao.AmostrasDoDia);
    }

    [Fact]
    public void AVentoinhaQueVeioSemValorNaColeta_NaoVoltaComORpmVelho()
    {
        UsarPastasTemporarias();

        var monitor = new MonitorVentoinhas();

        DateTime primeira = Agora;
        monitor.Registrar(new[] { LinhaDaVentoinha(3000f) }, 85f, null, primeira, primeira);

        // A linha veio, mas sem valor: o sensor não sabe a rotação
        // (o LibreHardwareMonitor devolve infinito nesses casos). Isso é
        // ausência de leitura, não "0 RPM" — e não é o RPM velho.
        DateTime segunda = Agora;
        monitor.Registrar(new[] { LinhaDaVentoinha(null) }, 85f, null, segunda, segunda);

        var cartao = Assert.Single(monitor.Cartoes());
        Assert.Null(cartao.RpmAtual);
        Assert.Equal(VentoinhaEstado.SemLeitura, cartao.Estado);
    }

    // ------------------------------------------- a contagem do dia

    [Fact]
    public void AColetaQueNaoMudou_NaoContaUmaMedicaoNova()
    {
        UsarPastasTemporarias();

        var monitor = new MonitorVentoinhas();

        // Duas voltas do relógio da janela com a mesma leva da coleta: o
        // sensor não foi lido de novo, e o dia não pode contar uma
        // medição que não aconteceu.
        DateTime momento = Agora;
        monitor.Registrar(new[] { LinhaDaVentoinha(3000f) }, 85f, null, momento, momento);
        monitor.Registrar(new[] { LinhaDaVentoinha(3000f) }, 85f, null, Agora, momento);

        var cartao = Assert.Single(monitor.Cartoes());
        Assert.Equal(1, cartao.AmostrasDoDia);
    }

    [Fact]
    public void AColetaQueMudou_ContaUmaMedicaoNova()
    {
        UsarPastasTemporarias();

        var monitor = new MonitorVentoinhas();

        DateTime primeira = Agora;
        monitor.Registrar(new[] { LinhaDaVentoinha(3000f) }, 85f, null, primeira, primeira);

        DateTime segunda = Agora;
        monitor.Registrar(new[] { LinhaDaVentoinha(3000f) }, 85f, null, segunda, segunda);

        var cartao = Assert.Single(monitor.Cartoes());
        Assert.Equal(2, cartao.AmostrasDoDia);
    }
}
