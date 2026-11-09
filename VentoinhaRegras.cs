using System.Globalization;

namespace TranalSentinel;

/// <summary>
/// De onde vem a temperatura que serve de referência para uma ventoinha.
///
/// <para>
/// A ventoinha não tem temperatura própria: o que importa é a temperatura do
/// que ela está esfriando. A ventoinha da caixa esfria a CPU; a ventoinha da
/// placa de vídeo esfria a GPU. Sem essa referência, um RPM baixo não diz
/// nada — a ventoinha pode estar parada porque o PC está frio.
/// </para>
/// </summary>
public enum FonteTemperatura
{
    /// <summary>Nenhuma temperatura serve de referência para esta ventoinha.</summary>
    Nenhuma,

    /// <summary>A temperatura da CPU (ventoinha da placa-mãe, do cooler ou do Super I/O).</summary>
    Cpu,

    /// <summary>A temperatura da GPU (ventoinha da placa de vídeo).</summary>
    Gpu,
}

/// <summary>Uma ventoinha como o app a mediu num instante.</summary>
/// <param name="Id">A identidade estável da ventoinha (<see cref="VentoinhaRegras.Identidade"/>).</param>
/// <param name="Hardware">O hardware dono da ventoinha (o chip Super I/O, a placa de vídeo).</param>
/// <param name="Nome">O nome do sensor ("Fan #1", "GPU Fan").</param>
/// <param name="Rpm">As rotações por minuto medidas agora.</param>
/// <param name="Temperatura">A temperatura de referência no mesmo instante (null se não houver).</param>
public sealed record VentoinhaLeitura(
    string Id,
    string Hardware,
    string Nome,
    double Rpm,
    double? Temperatura);

/// <summary>
/// O que o app guardou desta ventoinha: o recorde, que não é apagado.
///
/// <para>
/// O recorde é o maior RPM já visto desta ventoinha e a temperatura que
/// estava no mesmo instante. A temperatura do recorde é o que permite
/// comparar: só dá para dizer que a ventoinha "está pior" do que estava se
/// a condição térmica for a mesma ou pior.
/// </para>
/// </summary>
/// <param name="Id">A identidade estável da ventoinha.</param>
/// <param name="Hardware">O hardware dono da ventoinha.</param>
/// <param name="Nome">O nome do sensor.</param>
/// <param name="RpmRecorde">O maior RPM já medido nesta ventoinha.</param>
/// <param name="TemperaturaDoRecorde">A temperatura de referência no instante do recorde.</param>
/// <param name="QuandoRecorde">Quando o recorde foi medido.</param>
/// <param name="QuandoUltimo">Quando a ventoinha foi medida pela última vez.</param>
/// <param name="TemperaturaMaxima">A maior temperatura já vista nesta ventoinha.</param>
public sealed record VentoinhaRegistro(
    string Id,
    string Hardware,
    string Nome,
    double RpmRecorde,
    double? TemperaturaDoRecorde,
    DateTime QuandoRecorde,
    DateTime QuandoUltimo,
    double? TemperaturaMaxima);

/// <summary>O agregado de um dia para uma ventoinha (o "RPM com o tempo").</summary>
/// <param name="Id">A identidade estável da ventoinha.</param>
/// <param name="RpmMaximo">O maior RPM do dia.</param>
/// <param name="TemperaturaNoMaximo">A temperatura de referência no instante do maior RPM do dia.</param>
/// <param name="TemperaturaMaxima">A maior temperatura de referência vista no dia.</param>
/// <param name="RpmUltimo">O último RPM medido no dia.</param>
/// <param name="TemperaturaUltimo">A temperatura de referência no último RPM medido.</param>
/// <param name="Amostras">Quantas medições entraram no dia.</param>
public sealed record VentoinhaDoDia(
    string Id,
    double RpmMaximo,
    double? TemperaturaNoMaximo,
    double? TemperaturaMaxima,
    double RpmUltimo,
    double? TemperaturaUltimo,
    int Amostras);

/// <summary>
/// Uma linha do histórico das ventoinhas (o que a tela mostra no "RPM com o
/// tempo").
/// </summary>
/// <param name="Dia">O dia do agregado.</param>
/// <param name="Id">A identidade estável da ventoinha.</param>
/// <param name="Hardware">O hardware dono da ventoinha.</param>
/// <param name="Nome">O nome do sensor.</param>
/// <param name="RpmMaximo">O maior RPM do dia.</param>
/// <param name="TemperaturaNoMaximo">A temperatura de referência no instante do maior RPM do dia.</param>
/// <param name="TemperaturaMaxima">A maior temperatura de referência vista no dia.</param>
/// <param name="Amostras">Quantas medições entraram no dia.</param>
public sealed record VentoinhaHistorico(
    DateTime Dia,
    string Id,
    string Hardware,
    string Nome,
    double RpmMaximo,
    double? TemperaturaNoMaximo,
    double? TemperaturaMaxima,
    int Amostras);

/// <summary>O estado da ventoinha agora, do ponto de vista do desgaste.</summary>
public enum VentoinhaEstado
{
    /// <summary>O sensor não está dando leitura agora.</summary>
    SemLeitura,

    /// <summary>Não há histórico suficiente para dizer se a ventoinha está gasta.</summary>
    SemHistorico,

    /// <summary>
    /// Há histórico, mas falta a temperatura de referência para comparar: o
    /// app tem o número e não tem a outra metade da conta.
    /// </summary>
    SemComparacao,

    /// <summary>A queda apareceu há pouco: o app ainda está observando.</summary>
    Observando,

    /// <summary>A ventoinha está dentro do que ela já fez.</summary>
    Saudavel,

    /// <summary>A ventoinha está bem abaixo do próprio recorde, em condição térmica igual ou pior.</summary>
    SinalDeDegradacao,
}

/// <summary>
/// O veredito sobre uma ventoinha, com a frase que a tela mostra.
///
/// <para>
/// O veredito é <b>fato contra fato</b>: o RPM de agora comparado com o maior
/// RPM já medido, em condição térmica igual ou pior. Não há curva esperada
/// inventada e não há número de tabela de fabricante: o app compara a
/// ventoinha com ela mesma.
/// </para>
/// </summary>
/// <param name="Estado">O estado.</param>
/// <param name="Titulo">O rótulo curto (o selo do cartão).</param>
/// <param name="Texto">A frase que explica o veredito, com os números.</param>
/// <param name="PercentualAbaixoDoRecorde">Quanto o RPM de agora está abaixo do recorde (null quando não dá para comparar).</param>
public sealed record VentoinhaVeredito(
    VentoinhaEstado Estado,
    string Titulo,
    string Texto,
    double? PercentualAbaixoDoRecorde);

/// <summary>
/// O cartão de uma ventoinha — é isto que a tela consome.
///
/// <para>
/// O cartão junta três coisas: o que está acontecendo agora, o recorde (que
/// não é apagado) e o agregado de hoje. Os números saem todos do núcleo, e
/// a tela não monta frase nenhuma.
/// </para>
/// </summary>
public sealed record VentoinhaCartao(
    string Id,
    string Hardware,
    string Nome,
    string Rotulo,
    FonteTemperatura Fonte,
    double? RpmAtual,
    double? TemperaturaAtual,
    double? RpmRecorde,
    double? TemperaturaDoRecorde,
    DateTime? QuandoRecorde,
    double? RpmMaximoDoDia,
    double? TemperaturaNoMaximoDoDia,
    int AmostrasDoDia,
    VentoinhaEstado Estado,
    string Titulo,
    string Texto,
    string TextoDoRecorde,
    string TextoDeHoje);

/// <summary>
/// O cartão de uma ventoinha no formato que a tela consome (o mesmo formato
/// do cartão do tráfego e da energia).
///
/// <para>
/// Os nomes das propriedades são os que o cartão do app usa (<c>Title</c>,
/// <c>Meta</c>, <c>VerdictText</c>, <c>IsAlert</c>, <c>Reasons</c>,
/// <c>Metrics</c>): a tela não conhece outro formato.
/// </para>
/// </summary>
/// <param name="Title">O nome da ventoinha.</param>
/// <param name="Meta">A linha de contexto (a temperatura de referência).</param>
/// <param name="VerdictText">O selo do veredito ("SAUDÁVEL", "SINAL DE DEGRADAÇÃO"...).</param>
/// <param name="IsAlert">O selo é de alerta (a ventoinha está com sinal de degradação).</param>
/// <param name="Reasons">A frase que explica o veredito, com os números.</param>
/// <param name="Metrics">As linhas de número (o RPM de agora, o recorde, o dia).</param>
public sealed record VentoinhaCartaoTela(
    string Title,
    string Meta,
    string VerdictText,
    bool IsAlert,
    string Reasons,
    IReadOnlyList<KeyValuePair<string, string>> Metrics);

/// <summary>Uma linha do histórico das ventoinhas, como a tela mostra.</summary>
/// <param name="Dia">O dia (09/11/2026).</param>
/// <param name="Nome">O nome da ventoinha.</param>
/// <param name="RpmMaximo">O maior RPM do dia.</param>
/// <param name="Temperatura">A temperatura de referência no instante do maior RPM.</param>
/// <param name="Amostras">Quantas medições entraram no dia.</param>
public sealed record VentoinhaHistoricoLinha(
    string Dia,
    string Nome,
    string RpmMaximo,
    string Temperatura,
    string Amostras);

/// <summary>
/// A regra da degradação de ventoinhas — toda ela mora aqui.
///
/// <para>
/// <b>O que o app afirma</b>: a ventoinha está bem abaixo do maior RPM que
/// ela já deu, em condição térmica igual ou pior. As duas pontas da
/// comparação são fatos medidos: o RPM de agora e o RPM do recorde, cada um
/// com a temperatura do mesmo instante.
///
/// <para>
/// <b>O que o app não afirma</b>: que a ventoinha "deveria" girar a X rotações
/// com a CPU a Y graus. Essa curva seria um número inventado — não há tabela
/// de fabricante para o cooler da placa, e o app não tem como saber o que a
/// ventoinha faria se estivesse nova. O app compara a ventoinha com ela mesma.
///
/// <para>
/// Por isso há duas travas que evitam o alarme falso:
/// <list type="bullet">
/// <item>o comparação só vale quando a temperatura de agora é <b>igual ou
/// maior</b> que a do recorde (ventoinha que não precisa girar ainda não
/// conta);</item>
/// <item>a condição precisa valer por <see cref="PersistenciaMinima"/> seguidos
/// (uma oscilação de um segundo não é desgaste).</item>
/// </list>
///
/// <para>
/// E há um piso: um recorde abaixo de <see cref="RpmMinimoParaRecorde"/> não
/// diz nada sobre desgaste — a ventoinha nunca chegou a girar de verdade.
/// </para>
/// </summary>
public static class VentoinhaRegras
{
    /// <summary>
    /// Quanto o RPM pode ficar abaixo do recorde antes de o app falar em
    /// desgaste: 20%.
    ///
    /// <para>
    /// Este é um limite <b>do app</b>, não do hardware: ele diz o quanto a
    /// queda precisa ser grande para merecer a palavra "desgaste". Um valor
    /// pequeno acusaria toda ventoinha (a oscilação normal de um fan já passa
    /// de 10%); um valor grande deixaria passar a ventoinha que já perdeu
    /// metade da rotação.
    /// </para>
    /// </summary>
    public const double PerdaMinima = 0.20;

    /// <summary>
    /// O RPM abaixo do qual a ventoinha conta como parada: 300.
    ///
    /// <para>
    /// Uma ventoinha que nunca passou de 300 RPM não tem recorde que fale de
    /// desgaste — ela simplesmente não girou (cabeçalho livre, ventoinha de
    /// reposição que o dono não usa, modo "fan stop"). Sem este piso, uma
    /// ventoinha ociosa de 150 RPM com o recorde de 200 RPM acusaria desgaste
    /// o tempo todo.
    /// </para>
    /// </summary>
    public const double RpmMinimoParaRecorde = 300;

    /// <summary>
    /// Quanto tempo a condição de queda precisa valer antes de virar aviso:
    /// cinco minutos.
    ///
    /// <para>
    /// O RPM de uma ventoinha oscila o tempo todo (a curva da temperatura, o
    /// momentinho em que o boost da CPU termina). Um aviso que dispara com
    /// uma leitura vira alarme falso em minutos.
    /// </para>
    /// </summary>
    public static readonly TimeSpan PersistenciaMinima = TimeSpan.FromMinutes(5);

    /// <summary>
    /// A tolerância para considerar que o recorde foi atingido de novo: 2%.
    ///
    /// <para>
    /// O RPM do sensor nunca é exatamente igual entre uma leitura e outra
    /// (os últimos dígitos mudam). Sem esta tolerância, o recorde só poderia
    /// ser reavaliado por um valor idêntico — e a temperatura do recorde
    /// ficaria presa no primeiro instante em que ele apareceu.
    /// </para>
    /// </summary>
    public const double ToleranciaDoRecorde = 0.02;

    /// <summary>
    /// A identidade estável da ventoinha: o hardware dono mais o nome do
    /// sensor.
    ///
    /// <para>
    /// O id é o que amarra a leitura de hoje com o recorde de ontem. Por
    /// isso ele não pode depender da ordem em que a coleta devolveu as
    /// coisas.
    /// </para>
    /// </summary>
    public static string Identidade(string hardware, string nome) =>
        (hardware ?? "").Trim() + "|" + (nome ?? "").Trim();

    /// <summary>
    /// De onde vem a temperatura de referência desta ventoinha, pelo tipo do
    /// hardware dono dela.
    ///
    /// <para>
    /// Os tipos são os do LibreHardwareMonitor (medidos na biblioteca 0.9.6):
    /// <c>SuperIO</c> (o chip da placa-mãe — o ITE IT8655E), <c>Motherboard</c>,
    /// <c>Cooler</c> e <c>EmbeddedController</c> alimentam ventoinhas da
    /// CPU; os <c>Gpu*</c> alimentam a ventoinha da placa de vídeo.
    /// </para>
    /// </summary>
    public static FonteTemperatura FonteDe(string tipoHardware)
    {
        if (string.IsNullOrWhiteSpace(tipoHardware)) return FonteTemperatura.Nenhuma;

        if (tipoHardware is "Motherboard" or "SuperIO" or "Cooler" or "EmbeddedController")
            return FonteTemperatura.Cpu;

        if (tipoHardware is "GpuNvidia" or "GpuAmd" or "GpuIntel")
            return FonteTemperatura.Gpu;

        // Um tipo novo na biblioteca: o prefixo decide (uma GPU é uma GPU).
        if (tipoHardware.Contains("Gpu", StringComparison.OrdinalIgnoreCase))
            return FonteTemperatura.Gpu;

        return FonteTemperatura.Nenhuma;
    }

    /// <summary>O nome curto da fonte de temperatura (o que a tela mostra).</summary>
    public static string NomeDaFonte(FonteTemperatura fonte) => fonte switch
    {
        FonteTemperatura.Cpu => "CPU",
        FonteTemperatura.Gpu => "GPU",
        _ => "",
    };

    /// <summary>
    /// Atualiza o recorde com uma leitura nova.
    ///
    /// <para>
    /// O recorde só sobe: uma ventoinha que já chegou a 3.400 RPM não perde o
    /// recorde porque hoje ela girou a 2.000. E a temperatura do recorde só
    /// sobe quando o recorde é atingido de novo — assim a comparação passa a
    /// exigir uma condição térmica mais dura, e não mais fácil.
    /// </para>
    /// </summary>
    /// <param name="registro">O registro guardado (null = ainda não há nada).</param>
    /// <param name="leitura">A leitura de agora.</param>
    /// <param name="agora">O instante da leitura (o que fica no recorde).</param>
    public static VentoinhaRegistro AtualizarRecorde(
        VentoinhaRegistro? registro,
        VentoinhaLeitura leitura,
        DateTime agora)
    {
        if (registro is null)
        {
            return new VentoinhaRegistro(
                leitura.Id, leitura.Hardware, leitura.Nome,
                leitura.Rpm, leitura.Temperatura, agora, agora, leitura.Temperatura);
        }

        double rpmRecorde = registro.RpmRecorde;
        double? tempRecorde = registro.TemperaturaDoRecorde;
        DateTime quandoRecorde = registro.QuandoRecorde;
        double? tempMaxima = registro.TemperaturaMaxima;

        if (leitura.Temperatura is double temperatura)
            tempMaxima = tempMaxima is null ? temperatura : Math.Max(tempMaxima.Value, temperatura);

        if (leitura.Rpm > rpmRecorde)
        {
            // Recorde novo: ele muda a referência inteira.
            rpmRecorde = leitura.Rpm;

            // A temperatura do recorde só SOBE, e só com medida: um recorde
            // novo lido sem temperatura não apaga a temperatura que já foi
            // medida com a ventoinha neste patamar. Perder um fato medido
            // seria pior do que uma temperatura um pouco mais antiga — e o
            // app continua dizendo qual é ela.
            if (leitura.Temperatura is double temperaturaDoRecorde)
                tempRecorde = temperaturaDoRecorde;

            quandoRecorde = agora;
        }
        else if (leitura.Temperatura is double temperatura2
                 && (tempRecorde is null || temperatura2 > tempRecorde.Value)
                 && leitura.Rpm >= rpmRecorde * (1.0 - ToleranciaDoRecorde))
        {
            // O mesmo recorde, agora com a temperatura mais alta: a comparação
            // passa a exigir mais calor para acusar desgaste. O instante do
            // recorde não muda — o recorde continua sendo o mesmo RPM.
            tempRecorde = temperatura2;
        }

        return new VentoinhaRegistro(
            leitura.Id, leitura.Hardware, leitura.Nome,
            rpmRecorde, tempRecorde, quandoRecorde, agora, tempMaxima);
    }

    /// <summary>
    /// Atualiza o agregado do dia com uma leitura nova.
    /// </summary>
    public static VentoinhaDoDia AtualizarDia(VentoinhaDoDia? dia, VentoinhaLeitura leitura)
    {
        if (dia is null)
        {
            return new VentoinhaDoDia(
                leitura.Id, leitura.Rpm, leitura.Temperatura, leitura.Temperatura,
                leitura.Rpm, leitura.Temperatura, 1);
        }

        double rpmMaximo = dia.RpmMaximo;
        double? tempNoMaximo = dia.TemperaturaNoMaximo;
        double? tempMaxima = dia.TemperaturaMaxima;

        if (leitura.Rpm > rpmMaximo)
        {
            rpmMaximo = leitura.Rpm;
            tempNoMaximo = leitura.Temperatura;
        }

        if (leitura.Temperatura is double temperatura)
            tempMaxima = tempMaxima is null ? temperatura : Math.Max(tempMaxima.Value, temperatura);

        return new VentoinhaDoDia(
            leitura.Id, rpmMaximo, tempNoMaximo, tempMaxima,
            leitura.Rpm, leitura.Temperatura, dia.Amostras + 1);
    }

    /// <summary>
    /// Junta a base do banco com o que já foi medido nesta sessão.
    ///
    /// <para>
    /// A base é o que já estava salvo do dia; o medido é o que o app acumulou
    /// desde que subiu. Juntos, eles formam o dia inteiro: o máximo é o maior
    /// dos dois, a temperatura máxima é a maior das duas e a contagem é a
    /// soma das contagens.
    /// </para>
    /// </summary>
    /// <param name="baseDia">O que estava salvo no banco (null = nada).</param>
    /// <param name="medido">O que foi medido nesta sessão (null = nada).</param>
    public static VentoinhaDoDia? JuntarDia(VentoinhaDoDia? baseDia, VentoinhaDoDia? medido)
    {
        if (baseDia is null) return medido;
        if (medido is null) return baseDia;

        double rpmMaximo = baseDia.RpmMaximo;
        double? tempNoMaximo = baseDia.TemperaturaNoMaximo;

        if (medido.RpmMaximo > rpmMaximo)
        {
            rpmMaximo = medido.RpmMaximo;
            tempNoMaximo = medido.TemperaturaNoMaximo;
        }

        double? tempMaxima = baseDia.TemperaturaMaxima;
        if (medido.TemperaturaMaxima is double temperatura)
            tempMaxima = tempMaxima is null ? temperatura : Math.Max(tempMaxima.Value, temperatura);

        return new VentoinhaDoDia(
            medido.Id,
            rpmMaximo,
            tempNoMaximo,
            tempMaxima,
            medido.RpmUltimo,
            medido.TemperaturaUltimo,
            baseDia.Amostras + medido.Amostras);
    }

    /// <summary>
    /// O veredito sobre uma ventoinha, comparando a leitura de agora com o
    /// recorde guardado.
    ///
    /// <para>
    /// O veredito aqui é o <b>bruto</b>: ele diz que a condição de queda está
    /// valendo agora. Se ela persistir o bastante (<see cref="PersistenciaMinima"/>),
    /// ela vira sinal de degradação — e essa parte é do
    /// <see cref="EstadoFinal"/>.
    /// </para>
    /// </summary>
    public static VentoinhaVeredito Avaliar(VentoinhaRegistro? registro, VentoinhaLeitura leitura)
    {
        if (registro is null)
        {
            return new VentoinhaVeredito(
                VentoinhaEstado.SemHistorico,
                "SEM HISTÓRICO",
                "Ainda não há histórico desta ventoinha. O recorde começa a ser guardado a partir de agora.",
                null);
        }

        double rpmRecorde = registro.RpmRecorde;

        // Um recorde abaixo do piso não diz nada sobre desgaste: a ventoinha
        // nunca chegou a girar de verdade.
        if (rpmRecorde < RpmMinimoParaRecorde)
        {
            return new VentoinhaVeredito(
                VentoinhaEstado.SemHistorico,
                "SEM HISTÓRICO",
                $"O maior RPM guardado desta ventoinha é {TextoDoRpm(rpmRecorde)} — abaixo de " +
                $"{TextoDoRpm(RpmMinimoParaRecorde)}, o patamar em que ela conta como parada. " +
                "Ainda não dá para dizer se está gasta.",
                null);
        }

        // Sem temperatura de referência dos dois lados, não há comparação
        // possível: um RPM baixo pode ser só a ventoinha que não precisa
        // girar agora.
        if (leitura.Temperatura is null || registro.TemperaturaDoRecorde is null)
        {
            return new VentoinhaVeredito(
                VentoinhaEstado.SemComparacao,
                "SEM COMPARAÇÃO",
                "O app não tem a temperatura de referência para comparar com o recorde desta ventoinha " +
                $"(o recorde é {TextoDoRpm(rpmRecorde)}).",
                null);
        }

        double temperaturaAtual = leitura.Temperatura.Value;
        double temperaturaRecorde = registro.TemperaturaDoRecorde.Value;

        if (temperaturaAtual < temperaturaRecorde)
        {
            // A temperatura ainda não chegou à do recorde: a ventoinha não
            // precisa do mesmo giro ainda, e comparar os RPMs agora seria
            // comparar coisas diferentes.
            return new VentoinhaVeredito(
                VentoinhaEstado.Saudavel,
                "SAUDÁVEL",
                $"Agora {TextoDoRpm(leitura.Rpm)} com {TextoDaTemperatura(temperaturaAtual)}. " +
                $"O recorde é {TextoDoRpm(rpmRecorde)}, a {TextoDaTemperatura(temperaturaRecorde)} — " +
                "a temperatura ainda não chegou à do recorde.",
                null);
        }

        // A perda é a diferença dividida pelo recorde: subtrair depois
        // ("1 - rpm/recorde") arredonda o limite de 20% para baixo, e o app
        // calava exatamente no limite que ele promete acusar.
        double perda = (rpmRecorde - leitura.Rpm) / rpmRecorde;

        if (perda >= PerdaMinima)
        {
            return new VentoinhaVeredito(
                VentoinhaEstado.SinalDeDegradacao,
                "SINAL DE DEGRADAÇÃO",
                $"Agora {TextoDoRpm(leitura.Rpm)} com {TextoDaTemperatura(temperaturaAtual)}. " +
                $"O recorde é {TextoDoRpm(rpmRecorde)}, a {TextoDaTemperatura(temperaturaRecorde)} — " +
                $"{NumeroTexto.Percentual(perda * 100)} abaixo do recorde, com a temperatura no mesmo patamar ou mais alta.",
                perda * 100);
        }

        return new VentoinhaVeredito(
            VentoinhaEstado.Saudavel,
            "SAUDÁVEL",
            $"Agora {TextoDoRpm(leitura.Rpm)} com {TextoDaTemperatura(temperaturaAtual)}. " +
            $"O recorde é {TextoDoRpm(rpmRecorde)}, a {TextoDaTemperatura(temperaturaRecorde)}.",
            null);
    }

    /// <summary>
    /// O nome da ventoinha como o usuário lê: o nome do sensor e o hardware
    /// dono ("Fan #1 (ITE IT8655E)").
    ///
    /// <para>
    /// A frase mora aqui porque ela aparece em dois lugares (o cartão e a
    /// regra de alerta) e não pode ser diferente em um deles.
    /// </para>
    /// </summary>
    public static string Rotulo(string nome, string hardware) =>
        string.IsNullOrWhiteSpace(hardware) ? nome : nome + " (" + hardware + ")";

    /// <summary>
    /// O estado que a tela mostra, já com a persistência levada em conta.
    ///
    /// <para>
    /// A queda só vira sinal de degradação depois de
    /// <see cref="PersistenciaMinima"/> valendo sem parar. Antes disso, o
    /// cartão diz que está observando — e diz há quanto tempo, para o usuário
    /// não ficar no escuro esperando o selo mudar.
    /// </para>
    /// </summary>
    /// <param name="bruto">O veredito do momento.</param>
    /// <param name="duracao">Há quanto tempo a condição vem valendo (null = não está valendo).</param>
    public static VentoinhaEstado EstadoFinal(VentoinhaVeredito bruto, TimeSpan? duracao)
    {
        if (bruto.Estado != VentoinhaEstado.SinalDeDegradacao) return bruto.Estado;

        // Sem duração não há persistência provada — a condição pode ter
        // começado agora. O selo de degradação exige os 5 minutos, e essa
        // trava mora aqui, no núcleo, e não no consumidor.
        if (duracao is null) return VentoinhaEstado.Observando;

        return duracao.Value >= PersistenciaMinima
            ? VentoinhaEstado.SinalDeDegradacao
            : VentoinhaEstado.Observando;
    }

    /// <summary>
    /// O veredito de uma ventoinha que não está dando leitura agora.
    ///
    /// <para>
    /// A última medida guardada entra na frase: sem ela, o usuário fica sem
    /// saber se a ventoinha está desligada ou se o sensor sumiu.
    /// </para>
    /// </summary>
    public static VentoinhaVeredito VereditoSemLeitura(VentoinhaRegistro? registro) =>
        new(
            VentoinhaEstado.SemLeitura,
            "SEM LEITURA",
            registro is not null && registro.QuandoUltimo > DateTime.MinValue
                ? "O sensor desta ventoinha não está dando leitura agora. A última medida dela foi em "
                  + NumeroTexto.DataHora(registro.QuandoUltimo) + "."
                : "O sensor desta ventoinha não está dando leitura agora.",
            null);

    /// <summary>
    /// Monta o cartão da ventoinha.
    ///
    /// <para>
    /// Todas as frases saem daqui: a tela não escreve frase nenhuma sobre
    /// ventoinha.
    /// </para>
    /// </summary>
    /// <param name="registro">O recorde guardado (null = ainda não há).</param>
    /// <param name="leitura">A leitura de agora (null = o sensor não está dando leitura).</param>
    /// <param name="dia">O agregado de hoje (null = ainda sem amostras hoje).</param>
    /// <param name="bruto">O veredito do momento.</param>
    /// <param name="duracao">Há quanto tempo a condição de queda vem valendo (null = não está valendo).</param>
    /// <param name="fonte">De onde vem a temperatura de referência.</param>
    public static VentoinhaCartao Cartao(
        VentoinhaRegistro? registro,
        VentoinhaLeitura? leitura,
        VentoinhaDoDia? dia,
        VentoinhaVeredito bruto,
        TimeSpan? duracao,
        FonteTemperatura fonte)
    {

        var estado = EstadoFinal(bruto, duracao);

        string titulo = estado switch
        {
            VentoinhaEstado.SemLeitura => "SEM LEITURA",
            VentoinhaEstado.SemHistorico => bruto.Titulo,
            VentoinhaEstado.SemComparacao => bruto.Titulo,
            VentoinhaEstado.Observando => "OBSERVANDO",
            VentoinhaEstado.SinalDeDegradacao => "SINAL DE DEGRADAÇÃO",
            _ => "SAUDÁVEL",
        };

        string texto = estado switch
        {
            VentoinhaEstado.Observando =>
                duracao is null
                    ? "A queda de RPM apareceu agora. O app só confirma desgaste depois de "
                      + TextoDaDuracao(PersistenciaMinima) + " seguidos."
                    : $"A queda apareceu há {TextoDaDuracao(duracao.Value)} e ainda está valendo. " +
                      $"O app só confirma desgaste depois de {TextoDaDuracao(PersistenciaMinima)} seguidos.",
            _ => bruto.Texto,
        };

        string textoRecorde = TextoDoRecorde(registro, fonte);
        string textoHoje = TextoDeHoje(dia, fonte);

        return new VentoinhaCartao(
            registro?.Id ?? leitura?.Id ?? "",
            registro?.Hardware ?? leitura?.Hardware ?? "",
            registro?.Nome ?? leitura?.Nome ?? "",
            Rotulo(registro?.Nome ?? leitura?.Nome ?? "", registro?.Hardware ?? leitura?.Hardware ?? ""),
            fonte,
            leitura?.Rpm,
            leitura?.Temperatura,
            registro?.RpmRecorde,
            registro?.TemperaturaDoRecorde,
            registro?.QuandoRecorde,
            dia?.RpmMaximo,
            dia?.TemperaturaNoMaximo,
            dia?.Amostras ?? 0,
            estado,
            titulo,
            texto,
            textoRecorde,
            textoHoje);
    }

    /// <summary>A linha do recorde (a que fica no cartão).</summary>
    public static string TextoDoRecorde(VentoinhaRegistro? registro, FonteTemperatura fonte)
    {
        if (registro is null) return "RECORDE: — (a ventoinha ainda não tem histórico)";

        // A fonte da temperatura é resolvida na primeira leitura do ciclo: um
        // registro restaurado do banco ainda não sabe de onde veio a
        // temperatura. O número é mostrado de qualquer jeito — o que ainda
        // não se sabe é de qual sensor ele veio.
        string temperatura = registro.TemperaturaDoRecorde is double t
            ? fonte == FonteTemperatura.Nenhuma
                ? $" · {TextoDaTemperatura(t)} (fonte ainda não resolvida)"
                : $" · {NomeDaFonte(fonte)} a {TextoDaTemperatura(t)}"
            : "";

        return $"RECORDE: {TextoDoRpm(registro.RpmRecorde)}{temperatura} · " +
               NumeroTexto.DataHora(registro.QuandoRecorde);
    }

    /// <summary>A linha do dia (a que fica no cartão).</summary>
    public static string TextoDeHoje(VentoinhaDoDia? dia, FonteTemperatura fonte)
    {
        if (dia is null || dia.Amostras == 0) return "HOJE: ainda sem medições";

        string temperatura = dia.TemperaturaNoMaximo is double t
            ? $" a {TextoDaTemperatura(t)}"
            : "";

        return $"HOJE: máximo {TextoDoRpm(dia.RpmMaximo)}{temperatura} · {dia.Amostras} medições";
    }

    /// <summary>
    /// O cartão de uma ventoinha no formato da tela.
    /// </summary>
    public static VentoinhaCartaoTela CartaoParaTela(VentoinhaCartao cartao)
    {
        var metricas = new List<KeyValuePair<string, string>>();

        metricas.Add(new KeyValuePair<string, string>(
            "RPM AGORA",
            cartao.RpmAtual is double rpm ? TextoDoRpm(rpm) : "—"));

        metricas.Add(new KeyValuePair<string, string>(
            "TEMPERATURA AGORA",
            cartao.TemperaturaAtual is double temperatura
                ? TextoDaTemperatura(temperatura) +
                  (NomeDaFonte(cartao.Fonte).Length > 0 ? " (" + NomeDaFonte(cartao.Fonte) + ")" : "")
                : "—"));

        metricas.Add(new KeyValuePair<string, string>("RECORDE", TextoCurtoDoRecorde(cartao)));
        metricas.Add(new KeyValuePair<string, string>("HOJE", TextoCurtoDeHoje(cartao)));

        // "Não tem" e "não deu para resolver" são frases diferentes: antes da
        // primeira leitura o app ainda não sabe de onde vem a temperatura de
        // referência — ele não sabe que a ventoinha não tem referência.
        string fonte = cartao.Fonte != FonteTemperatura.Nenhuma
            ? "Temperatura de referência: " + NomeDaFonte(cartao.Fonte)
            : cartao.RpmAtual is null
                ? "Fonte da temperatura de referência ainda não resolvida"
                : "Sem temperatura de referência";

        return new VentoinhaCartaoTela(
            cartao.Rotulo,
            fonte,
            cartao.Titulo,
            cartao.Estado == VentoinhaEstado.SinalDeDegradacao,
            cartao.Texto,
            metricas);
    }

    /// <summary>O recorde em uma linha curta (a métrica do cartão).</summary>
    public static string TextoCurtoDoRecorde(VentoinhaCartao cartao)
    {
        if (cartao.RpmRecorde is not double rpm) return "—";

        string texto = TextoDoRpm(rpm);
        if (cartao.TemperaturaDoRecorde is double temperatura)
            texto += " a " + TextoDaTemperatura(temperatura);
        if (cartao.QuandoRecorde is DateTime quando && quando > DateTime.MinValue)
            texto += " (" + NumeroTexto.DataHora(quando) + ")";

        return texto;
    }

    /// <summary>O dia em uma linha curta (a métrica do cartão).</summary>
    public static string TextoCurtoDeHoje(VentoinhaCartao cartao)
    {
        if (cartao.RpmMaximoDoDia is not double rpm) return "ainda sem medições";

        string texto = "máx. " + TextoDoRpm(rpm);
        if (cartao.AmostrasDoDia > 0)
            texto += " · " + cartao.AmostrasDoDia + " medições";

        return texto;
    }

    /// <summary>
    /// As linhas do histórico como a tela mostra (o "RPM com o tempo").
    /// </summary>
    public static List<VentoinhaHistoricoLinha> LinhasDoHistorico(IReadOnlyList<VentoinhaHistorico> itens)
    {
        var linhas = new List<VentoinhaHistoricoLinha>(itens.Count);

        foreach (var item in itens)
        {
            linhas.Add(new VentoinhaHistoricoLinha(
                NumeroTexto.Data(item.Dia),
                NomeLegivel(item.Nome),
                TextoDoRpm(item.RpmMaximo),
                item.TemperaturaNoMaximo is double temperatura ? TextoDaTemperatura(temperatura) : "—",
                item.Amostras.ToString(CultureInfo.InvariantCulture)));
        }

        return linhas;
    }

    /// <summary>
    /// O nome da ventoinha como a pessoa lê.
    ///
    /// <para>
    /// Quando o registro da ventoinha se perde, o histórico cai no id
    /// ("ITE IT8655E|Fan #1"). O id serve para amarrar as linhas, não para
    /// ser lido: o que interessa é o nome do sensor.
    /// </para>
    /// </summary>
    public static string NomeLegivel(string nome)
    {
        if (string.IsNullOrWhiteSpace(nome)) return "ventoinha sem nome";

        int separador = nome.IndexOf('|');
        if (separador < 0) return nome;

        string sensor = nome[(separador + 1)..].Trim();
        return sensor.Length > 0 ? sensor : nome;
    }

    /// <summary>Um RPM no padrão da tela ("3.392 RPM").</summary>
    /// guarda, um infinito viraria o maior número que cabe em um long — e a
    /// tela mostraria uma mentira com a cara de medida.
    /// </para>
    /// </summary>
    public static string TextoDoRpm(double rpm) =>
        double.IsFinite(rpm)
            ? NumeroTexto.Inteiro((long)Math.Round(rpm, MidpointRounding.AwayFromZero)) + " RPM"
            : "—";

    /// <summary>
    /// Uma temperatura no padrão da tela ("85,4 °C").
    ///
    /// <para>
    /// Uma casa decimal só: o sensor devolve um float cheio de ruído
    /// (52,37500762939453), e mostrar isso na tela é mostrar ruído.
    /// </para>
    /// </summary>
    public static string TextoDaTemperatura(double temperatura) =>
        NumeroTexto.Numero(Math.Round(temperatura, 1)) + " °C";

    /// <summary>Uma duração no padrão da tela ("4 min", "1 h 12 min").</summary>
    public static string TextoDaDuracao(TimeSpan duracao)
    {
        int minutos = (int)Math.Floor(duracao.TotalMinutes);
        if (minutos < 1) return "poucos segundos";
        if (minutos < 60) return minutos + " min";

        int horas = minutos / 60;
        int resto = minutos % 60;
        return resto == 0 ? horas + " h" : $"{horas} h {resto} min";
    }
}
