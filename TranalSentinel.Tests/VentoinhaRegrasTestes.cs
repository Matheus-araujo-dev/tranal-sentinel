using System.Globalization;
using Xunit;

namespace TranalSentinel.Tests;

/// <summary>
/// Testes da regra da degradação de ventoinhas.
///
/// <para>
/// Cada teste aqui prende uma decisão que, errada, faz o app acusar uma
/// ventoinha sadia de gasta (o alarme falso que ensina o usuário a ignorar
/// o aviso) ou esconder uma ventoinha que está acabando (o número errado é
/// pior que o número ausente).
///
/// <para>
/// A regra toda é comparada com a ventoinha contra ela mesma: o maior RPM
/// que ela já deu e a temperatura do mesmo instante. Não há curva esperada
/// nem tabela de fabricante — e os testes garantem que nenhum número
/// inventado entra no caminho.
/// </para>
/// </summary>
public class VentoinhaRegrasTestes
{
    private static readonly DateTime Agora = new(2026, 11, 9, 14, 0, 0);

    private static VentoinhaLeitura Leitura(double rpm, double? temperatura = 85) =>
        new("ITE IT8655E|Fan #1", "ITE IT8655E", "Fan #1", rpm, temperatura);

    // ------------------------------------------------------------------ identidade

    [Fact]
    public void Identidade_EDoHardwareEDoSensor()
    {
        string a = VentoinhaRegras.Identidade("ITE IT8655E", "Fan #1");
        string b = VentoinhaRegras.Identidade("ITE IT8655E", "Fan #1");
        string c = VentoinhaRegras.Identidade("AMD Radeon RX 9060 XT", "Fan #1");

        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
    }

    [Fact]
    public void FonteDe_AVentoinhaDaPlacaMaeUsaACpu()
    {
        // Os tipos foram medidos na biblioteca 0.9.6 do LibreHardwareMonitor:
        // o chip Super I/O (o ITE IT8655E desta placa) é "SuperIO".
        Assert.Equal(FonteTemperatura.Cpu, VentoinhaRegras.FonteDe("SuperIO"));
        Assert.Equal(FonteTemperatura.Cpu, VentoinhaRegras.FonteDe("Motherboard"));
        Assert.Equal(FonteTemperatura.Cpu, VentoinhaRegras.FonteDe("Cooler"));
    }

    [Fact]
    public void FonteDe_AVentoinhaDaPlacaDeVideoUsaAGpu()
    {
        Assert.Equal(FonteTemperatura.Gpu, VentoinhaRegras.FonteDe("GpuAmd"));
        Assert.Equal(FonteTemperatura.Gpu, VentoinhaRegras.FonteDe("GpuNvidia"));
        Assert.Equal(FonteTemperatura.Gpu, VentoinhaRegras.FonteDe("GpuIntel"));
    }

    [Fact]
    public void FonteDe_DeOutroTipoNaoTemTemperaturaDeReferencia()
    {
        // Sem referência, o app não compara: uma ventoinha de disco girando
        // pouco não diz nada sobre desgaste.
        Assert.Equal(FonteTemperatura.Nenhuma, VentoinhaRegras.FonteDe("Storage"));
        Assert.Equal(FonteTemperatura.Nenhuma, VentoinhaRegras.FonteDe(""));
    }

    // ------------------------------------------------------------------ o recorde

    [Fact]
    public void AtualizarRecorde_PrimeiraLeitura_CriaORecorde()
    {
        var registro = VentoinhaRegras.AtualizarRecorde(null, Leitura(3400, 85), Agora);

        Assert.Equal(3400, registro.RpmRecorde);
        Assert.Equal(85, registro.TemperaturaDoRecorde);
        Assert.Equal(Agora, registro.QuandoRecorde);
    }

    [Fact]
    public void AtualizarRecorde_RpmMenor_NaoBaixaORecorde()
    {
        var registro = VentoinhaRegras.AtualizarRecorde(null, Leitura(3400, 85), Agora);
        registro = VentoinhaRegras.AtualizarRecorde(registro, Leitura(2000, 90), Agora.AddMinutes(1));

        // O recorde é o maior RPM já visto: uma ventoinha que hoje gira
        // menos não perde o que já fez.
        Assert.Equal(3400, registro.RpmRecorde);
        Assert.Equal(85, registro.TemperaturaDoRecorde);
    }

    [Fact]
    public void AtualizarRecorde_RpmMaior_SobeORecorde()
    {
        var registro = VentoinhaRegras.AtualizarRecorde(null, Leitura(3400, 85), Agora);
        registro = VentoinhaRegras.AtualizarRecorde(registro, Leitura(3900, 88), Agora.AddMinutes(1));

        Assert.Equal(3900, registro.RpmRecorde);
        Assert.Equal(88, registro.TemperaturaDoRecorde);
    }

    [Fact]
    public void AtualizarRecorde_RecordeEmTemperaturaMaior_ExigeMaisCalorParaAcusar()
    {
        var registro = VentoinhaRegras.AtualizarRecorde(null, Leitura(3400, 85), Agora);

        // O mesmo recorde, agora com a CPU mais quente: a comparação passa a
        // exigir uma condição mais dura.
        registro = VentoinhaRegras.AtualizarRecorde(registro, Leitura(3400, 90), Agora.AddMinutes(1));
        Assert.Equal(90, registro.TemperaturaDoRecorde);

        // E uma leitura mais fria não abaixa a exigência.
        registro = VentoinhaRegras.AtualizarRecorde(registro, Leitura(3400, 80), Agora.AddMinutes(2));
        Assert.Equal(90, registro.TemperaturaDoRecorde);
    }

    [Fact]
    public void AtualizarRecorde_RecordeNovoSemTemperatura_NaoApagaATemperaturaMedida()
    {
        var registro = VentoinhaRegras.AtualizarRecorde(null, Leitura(3400, 85), Agora);

        // Um recorde novo lido SEM temperatura (a referência da CPU não foi
        // lida naquele instante) não pode apagar a temperatura que já foi
        // medida: perder um fato medido é pior do que uma temperatura um
        // pouco mais antiga.
        registro = VentoinhaRegras.AtualizarRecorde(registro, Leitura(3900, null), Agora.AddMinutes(1));

        Assert.Equal(3900, registro.RpmRecorde);
        Assert.Equal(85, registro.TemperaturaDoRecorde);
    }

    [Fact]
    public void AtualizarRecorde_RegistraAMaiorTemperaturaVista()
    {
        var registro = VentoinhaRegras.AtualizarRecorde(null, Leitura(1000, 60), Agora);
        registro = VentoinhaRegras.AtualizarRecorde(registro, Leitura(1000, 75), Agora.AddMinutes(1));

        Assert.Equal(75, registro.TemperaturaMaxima);
    }

    // ------------------------------------------------------------------ o veredito

    [Fact]
    public void Avaliar_SemHistorico_NaoAcusaNada()
    {
        var veredito = VentoinhaRegras.Avaliar(null, Leitura(100));

        Assert.Equal(VentoinhaEstado.SemHistorico, veredito.Estado);
        Assert.Null(veredito.PercentualAbaixoDoRecorde);
    }

    [Fact]
    public void Avaliar_RecordeAbaixoDoPiso_NaoAcusaNada()
    {
        // Uma ventoinha que nunca passou de 200 RPM não tem recorde que
        // fale de desgaste: ela simplesmente não girou.
        var registro = VentoinhaRegras.AtualizarRecorde(null, Leitura(200, 40), Agora);
        var veredito = VentoinhaRegras.Avaliar(registro, Leitura(150, 45));

        Assert.Equal(VentoinhaEstado.SemHistorico, veredito.Estado);
        Assert.Contains("300 RPM", veredito.Texto);
    }

    [Fact]
    public void Avaliar_TemperaturaAbaixoDoRecorde_NaoAcusa()
    {
        // A CPU está mais fria que quando o recorde aconteceu: a ventoinha
        // não precisa do mesmo giro ainda, e comparar os RPMs agora seria
        // comparar coisas diferentes.
        var registro = VentoinhaRegras.AtualizarRecorde(null, Leitura(3400, 85), Agora);
        var veredito = VentoinhaRegras.Avaliar(registro, Leitura(1200, 60));

        Assert.Equal(VentoinhaEstado.Saudavel, veredito.Estado);
        Assert.Contains("ainda não chegou", veredito.Texto);
    }

    [Fact]
    public void Avaliar_QuedaPequena_NaoAcusa()
    {
        // 2.800 contra um recorde de 3.400 é uma queda de 17,6% — abaixo do
        // limite de 20% do app.
        var registro = VentoinhaRegras.AtualizarRecorde(null, Leitura(3400, 85), Agora);
        var veredito = VentoinhaRegras.Avaliar(registro, Leitura(2800, 90));

        Assert.Equal(VentoinhaEstado.Saudavel, veredito.Estado);
        Assert.Null(veredito.PercentualAbaixoDoRecorde);
    }

    [Fact]
    public void Avaliar_QuedaGrande_AcusaComOsNumeros()
    {
        var registro = VentoinhaRegras.AtualizarRecorde(null, Leitura(3400, 85), Agora);
        var veredito = VentoinhaRegras.Avaliar(registro, Leitura(2000, 90));

        Assert.Equal(VentoinhaEstado.SinalDeDegradacao, veredito.Estado);
        Assert.NotNull(veredito.PercentualAbaixoDoRecorde);
        Assert.Equal(41.18, veredito.PercentualAbaixoDoRecorde!.Value, 2);

        // A frase traz os dois números: o de agora e o do recorde.
        Assert.Contains("2.000 RPM", veredito.Texto);
        Assert.Contains("3.400 RPM", veredito.Texto);
        Assert.Contains("41,18%", veredito.Texto);
    }

    [Fact]
    public void Avaliar_ExatamenteVintePorCentoAbaixo_Acusa()
    {
        // O contrato diz "pelo menos 20% abaixo". No limite exato, o app
        // acusa: a conta antiga ("1 - rpm/recorde") arredondava o limite
        // para baixo e o app calava exatamente onde prometia acusar.
        var registro = VentoinhaRegras.AtualizarRecorde(null, Leitura(3000, 85), Agora);
        var veredito = VentoinhaRegras.Avaliar(registro, Leitura(2400, 85));

        Assert.Equal(VentoinhaEstado.SinalDeDegradacao, veredito.Estado);
        Assert.Equal(20, veredito.PercentualAbaixoDoRecorde!.Value, 2);
    }

    [Fact]
    public void Avaliar_SemTemperaturaDeReferencia_NaoAcusa()
    {
        // Sem a temperatura dos dois lados, não há comparação possível: um
        // RPM baixo pode ser só a ventoinha que não precisa girar agora.
        var registro = VentoinhaRegras.AtualizarRecorde(null, Leitura(3400, 85), Agora);
        var veredito = VentoinhaRegras.Avaliar(registro, new VentoinhaLeitura(
            "ITE IT8655E|Fan #1", "ITE IT8655E", "Fan #1", 1000, null));

        // "Sem histórico" (não há o que comparar) e "sem comparação" (há o
        // número, falta a temperatura) são estados diferentes: o app sabe
        // dizer qual dos dois é o caso.
        Assert.Equal(VentoinhaEstado.SemComparacao, veredito.Estado);
        Assert.Contains("SEM COMPARAÇÃO", veredito.Titulo);
    }

    // ------------------------------------------------------------------ a base do dia

    [Fact]
    public void JuntarDia_ComABaseEOQueFoiMedido_SomaAsContagensEGuardaOMaior()
    {
        var baseDia = new VentoinhaDoDia("id", 3392, 85, 91, 3000, 80, 143);
        var medido = new VentoinhaDoDia("id", 3200, 70, 88, 3200, 70, 30);

        var juntado = VentoinhaRegras.JuntarDia(baseDia, medido);

        // A base é o que já estava salvo do dia; o medido é o que o app
        // acumulou desde que subiu. Juntos, formam o dia inteiro.
        Assert.NotNull(juntado);
        Assert.Equal(3392, juntado!.RpmMaximo);
        Assert.Equal(85, juntado.TemperaturaNoMaximo);
        Assert.Equal(91, juntado.TemperaturaMaxima);
        Assert.Equal(173, juntado.Amostras);
    }

    [Fact]
    public void JuntarDia_ComOMedidoMaior_GuardaOMedido()
    {
        var baseDia = new VentoinhaDoDia("id", 3392, 85, 91, 3000, 80, 143);
        var medido = new VentoinhaDoDia("id", 3500, 95, 95, 3500, 95, 5);

        var juntado = VentoinhaRegras.JuntarDia(baseDia, medido);

        Assert.NotNull(juntado);
        Assert.Equal(3500, juntado!.RpmMaximo);
        Assert.Equal(95, juntado.TemperaturaNoMaximo);
        Assert.Equal(148, juntado.Amostras);
    }

    [Fact]
    public void JuntarDia_SemUmaDasPartes_DevolveAOutra()
    {
        var medido = new VentoinhaDoDia("id", 3200, 70, 88, 3200, 70, 30);

        Assert.Equal(medido, VentoinhaRegras.JuntarDia(null, medido));
        Assert.Equal(medido, VentoinhaRegras.JuntarDia(medido, null));
    }

    // ------------------------------------------------------------------ a persistência

    [Fact]
    public void EstadoFinal_CondicaoRecente_FicaObservando()
    {
        var bruto = VentoinhaRegras.Avaliar(
            VentoinhaRegras.AtualizarRecorde(null, Leitura(3400, 85), Agora), Leitura(2000, 90));

        var estado = VentoinhaRegras.EstadoFinal(bruto, TimeSpan.FromMinutes(2));

        Assert.Equal(VentoinhaEstado.Observando, estado);
    }

    [Fact]
    public void EstadoFinal_CondicaoPersistente_ConfirmaADegradacao()
    {
        var bruto = VentoinhaRegras.Avaliar(
            VentoinhaRegras.AtualizarRecorde(null, Leitura(3400, 85), Agora), Leitura(2000, 90));

        var estado = VentoinhaRegras.EstadoFinal(bruto, VentoinhaRegras.PersistenciaMinima);

        Assert.Equal(VentoinhaEstado.SinalDeDegradacao, estado);
    }

    [Fact]
    public void EstadoFinal_SemCondicaoValendo_NaoFicaObservando()
    {
        var bruto = VentoinhaRegras.Avaliar(
            VentoinhaRegras.AtualizarRecorde(null, Leitura(3400, 85), Agora), Leitura(3400, 90));

        Assert.Equal(VentoinhaEstado.Saudavel, VentoinhaRegras.EstadoFinal(bruto, null));
    }

    [Fact]
    public void EstadoFinal_SemDuracao_NaoFicaComOseloDeDegradacao()
    {
        var bruto = VentoinhaRegras.Avaliar(
            VentoinhaRegras.AtualizarRecorde(null, Leitura(3400, 85), Agora), Leitura(2000, 90));

        Assert.Equal(VentoinhaEstado.SinalDeDegradacao, bruto.Estado);

        // Sem tempo medido não há persistência provada. A trava dos 5
        // minutos mora no núcleo — e não em quem consume o veredito.
        Assert.Equal(VentoinhaEstado.Observando, VentoinhaRegras.EstadoFinal(bruto, null));
    }

    // ------------------------------------------------------------------ os textos

    [Fact]
    public void CartaoParaTela_AntesDaPrimeiraLeitura_NaoAfirmaQueNaoTemTemperaturaDeReferencia()
    {
        // O registro vem do banco; a fonte da temperatura só é resolvida na
        // primeira leitura do ciclo. "Não tem" e "ainda não sei" são frases
        // diferentes — e a segunda é a verdadeira.
        var registro = VentoinhaRegras.AtualizarRecorde(null, Leitura(3400, 85), Agora);

        var cartao = VentoinhaRegras.Cartao(
            registro, null, null,
            VentoinhaRegras.VereditoSemLeitura(registro), null,
            FonteTemperatura.Nenhuma);

        string meta = VentoinhaRegras.CartaoParaTela(cartao).Meta;

        Assert.Contains("ainda não resolvida", meta);
        Assert.DoesNotContain("Sem temperatura de referência", meta);
    }

    [Fact]
    public void Cartao_ComQuedaQueJaEstavaAoSubir_DizQueNaoSabeQuandoComecou()
    {
        // A queda já estava valendo quando o app subiu: o app não sabe
        // quando ela começou, e o cartão não pode afirmar que ela
        // "apareceu" agora.
        var registro = VentoinhaRegras.AtualizarRecorde(null, Leitura(3400, 85), Agora);
        var bruto = VentoinhaRegras.Avaliar(registro, Leitura(2000, 90));

        var cartao = VentoinhaRegras.Cartao(
            registro, Leitura(2000, 90), null, bruto, TimeSpan.FromMinutes(2),
            FonteTemperatura.Cpu, quedaJaEstavaAoSubir: true);

        Assert.Equal(VentoinhaEstado.Observando, cartao.Estado);
        Assert.Contains("já estava valendo quando o app subiu", cartao.Texto);
        Assert.Contains("não sabe", cartao.Texto);
    }

    [Fact]
    public void Cartao_ComQuedaVistaPeloApp_DizQueApareceuHaQuantoTempo()
    {
        var registro = VentoinhaRegras.AtualizarRecorde(null, Leitura(3400, 85), Agora);
        var bruto = VentoinhaRegras.Avaliar(registro, Leitura(2000, 90));

        var cartao = VentoinhaRegras.Cartao(
            registro, Leitura(2000, 90), null, bruto, TimeSpan.FromMinutes(2),
            FonteTemperatura.Cpu, quedaJaEstavaAoSubir: false);

        Assert.Contains("A queda apareceu há 2 min", cartao.Texto);
    }

    [Fact]
    public void TextoCurtoDeHoje_MostraATemperaturaDoMaximoDoDia()
    {
        var registro = VentoinhaRegras.AtualizarRecorde(null, Leitura(3400, 85), Agora);
        var dia = new VentoinhaDoDia("ITE IT8655E|Fan #1", 3400, 85, 91, 3000, 80, 143);

        var cartao = VentoinhaRegras.Cartao(
            registro, Leitura(3000, 80), dia, VentoinhaRegras.VereditoSemLeitura(registro),
            null, FonteTemperatura.Cpu);

        string texto = VentoinhaRegras.TextoCurtoDeHoje(cartao);

        // O RPM do dia sem o calor que o explicou seria metade da história.
        Assert.Contains("3.400 RPM", texto);
        Assert.Contains("85 °C", texto);
        Assert.Contains("143 medições", texto);
    }

    [Fact]
    public void RpmSaiComOSeparadorDeMilhar()
    {
        Assert.Equal("3.392 RPM", VentoinhaRegras.TextoDoRpm(3392));
        Assert.Equal("0 RPM", VentoinhaRegras.TextoDoRpm(0));
    }

    [Fact]
    public void RpmNaoFinitoNaoViraNumero()
    {
        // Um infinito viraria o maior número que cabe em um long: a tela
        // mostraria uma mentira com a cara de medida.
        Assert.Equal("—", VentoinhaRegras.TextoDoRpm(double.NaN));
        Assert.Equal("—", VentoinhaRegras.TextoDoRpm(double.PositiveInfinity));
    }

    [Fact]
    public void NomeLegivel_ComIdCru_MostraONomeDoSensor()
    {
        // Sem o registro da ventoinha, o histórico cai no id: o que a pessoa
        // lê é o nome do sensor.
        Assert.Equal("Fan #1", VentoinhaRegras.NomeLegivel("ITE IT8655E|Fan #1"));
        Assert.Equal("Fan #1", VentoinhaRegras.NomeLegivel("Fan #1"));
        Assert.Equal("ventoinha sem nome", VentoinhaRegras.NomeLegivel(""));
    }

    [Fact]
    public void OsNumerosNaoDependemDaCulturaDoWindows()
    {
        CultureInfo anterior = CultureInfo.CurrentCulture;
        try
        {
            // A máquina de integração roda em en-US: o mesmo texto tem que
            // sair em pt-BR nos dois lugares.
            CultureInfo.CurrentCulture = new CultureInfo("en-US");
            Assert.Equal("41,18%", NumeroTexto.Percentual(41.176));
            Assert.Equal("3.392 RPM", VentoinhaRegras.TextoDoRpm(3392));
            Assert.Equal("85,5 °C", VentoinhaRegras.TextoDaTemperatura(85.5));

            CultureInfo.CurrentCulture = new CultureInfo("pt-BR");
            Assert.Equal("41,18%", NumeroTexto.Percentual(41.176));
            Assert.Equal("85,5 °C", VentoinhaRegras.TextoDaTemperatura(85.5));
        }
        finally
        {
            CultureInfo.CurrentCulture = anterior;
        }
    }

    [Fact]
    public void NumeroTexto_NumeroQueNaoEFinitoNaoViraTextoDeNumero()
    {
        Assert.Equal("—", NumeroTexto.Numero(double.NaN));
        Assert.Equal("—", NumeroTexto.Numero(double.PositiveInfinity));
        Assert.Equal("—", NumeroTexto.Numero(double.NegativeInfinity));
    }

    [Fact]
    public void DataSaiNoPadraoBrasileiro()
    {
        Assert.Equal("09/11/2026", NumeroTexto.Data(new DateTime(2026, 11, 9)));
        Assert.Equal("09/11/2026 14:00", NumeroTexto.DataHora(new DateTime(2026, 11, 9, 14, 0, 0)));
    }

    // ------------------------------------------------------------------ o cartão

    [Fact]
    public void Cartao_ComDegradacao_MostraOSeloEosNumeros()
    {
        var registro = VentoinhaRegras.AtualizarRecorde(null, Leitura(3400, 85), Agora);
        var dia = VentoinhaRegras.AtualizarDia(null, Leitura(2000, 90));

        var cartao = VentoinhaRegras.Cartao(
            registro, Leitura(2000, 90), dia,
            VentoinhaRegras.Avaliar(registro, Leitura(2000, 90)),
            VentoinhaRegras.PersistenciaMinima,
            FonteTemperatura.Cpu);

        Assert.Equal(VentoinhaEstado.SinalDeDegradacao, cartao.Estado);
        Assert.Equal("SINAL DE DEGRADAÇÃO", cartao.Titulo);
        Assert.Contains("2.000 RPM", cartao.Texto);
        Assert.Contains("3.400 RPM", cartao.TextoDoRecorde);
        Assert.Contains("HOJE", cartao.TextoDeHoje);
    }

    [Fact]
    public void CartaoParaTela_SeloDeAlertaSoNaDegradacao()
    {
        var registro = VentoinhaRegras.AtualizarRecorde(null, Leitura(3400, 85), Agora);

        var saudavel = VentoinhaRegras.CartaoParaTela(VentoinhaRegras.Cartao(
            registro, Leitura(3400, 90), null,
            VentoinhaRegras.Avaliar(registro, Leitura(3400, 90)),
            null, FonteTemperatura.Cpu));

        Assert.False(saudavel.IsAlert);
        Assert.Equal("SAUDÁVEL", saudavel.VerdictText);
        Assert.Equal("Fan #1 (ITE IT8655E)", saudavel.Title);

        var degradada = VentoinhaRegras.CartaoParaTela(VentoinhaRegras.Cartao(
            registro, Leitura(2000, 90), null,
            VentoinhaRegras.Avaliar(registro, Leitura(2000, 90)),
            VentoinhaRegras.PersistenciaMinima, FonteTemperatura.Cpu));

        Assert.True(degradada.IsAlert);
        Assert.Equal("SINAL DE DEGRADAÇÃO", degradada.VerdictText);
        Assert.Equal(4, degradada.Metrics.Count);
    }

    [Fact]
    public void Cartao_SemLeitura_DizQuandoFoiAMedidaPelaUltimaVez()
    {
        var registro = VentoinhaRegras.AtualizarRecorde(null, Leitura(3400, 85), Agora);

        var cartao = VentoinhaRegras.Cartao(
            registro, null, null,
            VentoinhaRegras.VereditoSemLeitura(registro),
            null, FonteTemperatura.Cpu);

        Assert.Equal(VentoinhaEstado.SemLeitura, cartao.Estado);
        Assert.Contains("09/11/2026 14:00", cartao.Texto);
    }

    [Fact]
    public void LinhasDoHistorico_FormataODiaEOsNumeros()
    {
        var linhas = VentoinhaRegras.LinhasDoHistorico(new[]
        {
            new VentoinhaHistorico(new DateTime(2026, 11, 9), "id", "ITE IT8655E", "Fan #1",
                3392, 85.5, 91.2, 143),
        });

        Assert.Single(linhas);
        Assert.Equal("09/11/2026", linhas[0].Dia);
        Assert.Equal("3.392 RPM", linhas[0].RpmMaximo);
        Assert.Equal("85,5 °C", linhas[0].Temperatura);
        Assert.Equal("143", linhas[0].Amostras);
    }
}
