using LibreHardwareMonitor.Hardware;

namespace TranalSentinel;

/// <summary>
/// O acompanhamento das ventoinhas: a leitura de cada uma, o recorde que o
/// app guarda e o veredito sobre o desgaste.
///
/// <para>
/// A coleta é simples: a cada volta dos sensores o monitor olha as linhas de
/// ventoinha, resolve a temperatura de referência (a da CPU para a ventoinha
/// da placa-mãe, a da GPU para a da placa de vídeo) e atualiza o recorde e o
/// agregado do dia. A regra do que é desgaste mora em
/// <see cref="VentoinhaRegras"/> — aqui mora só o estado.
///
/// <para>
/// O app grava o que sabe de tempos em tempos (e quando fecha): a escrita é
/// uma transação do banco, e fazer isso a cada segundo só gastaria I/O.
///
/// <para>
/// O recorde <b>sobe</b> e nunca desce: é por isso que ele fica no banco,
/// e não só na memória. Um app que abre e fecha várias vezes por dia
/// precisa levar o recorde do dia anterior junto — sem ele, a comparação de
/// desgaste não teria com o que comparar.
/// </para>
/// </summary>
public sealed class MonitorVentoinhas
{
    /// <summary>De quanto em quanto tempo o app grava o que sabe das ventoinhas.</summary>
    public static TimeSpan IntervaloGravacao => TimeSpan.FromSeconds(30);

    /// <summary>O estado de uma ventoinha dentro do app.</summary>
    private sealed class Estado
    {
        /// <summary>A identidade estável (o hardware dono + o nome do sensor).</summary>
        public string Id = "";

        /// <summary>O hardware dono da ventoinha.</summary>
        public string Hardware = "";

        /// <summary>O nome do sensor.</summary>
        public string Nome = "";

        /// <summary>De onde vem a temperatura de referência.</summary>
        public FonteTemperatura Fonte = FonteTemperatura.Nenhuma;

        /// <summary>O recorde guardado (o maior RPM já visto e a temperatura dele).</summary>
        public VentoinhaRegistro? Registro;

        /// <summary>O agregado do dia de hoje.</summary>
        public VentoinhaDoDia? Dia;

        /// <summary>A última leitura (null quando o sensor não deu leitura).</summary>
        public VentoinhaLeitura? Leitura;

        /// <summary>O veredito da última leitura.</summary>
        public VentoinhaVeredito? Veredito;

        /// <summary>Desde quando a condição de queda está valendo sem parar.</summary>
        public DateTime? InicioDaQueda;

        /// <summary>Quando a ventoinha foi medida pela última vez.</summary>
        public DateTime UltimaMedida = DateTime.MinValue;
    }

    private readonly Dictionary<string, Estado> _estados = new();
    private readonly object _trava = new();

    /// <summary>O dia que o app está acumulando.</summary>
    private DateTime _dia = DateTime.MinValue;

    /// <summary>
    /// A base do dia foi lida do banco?
    ///
    /// <para>
    /// A base é o que já estava salvo do dia quando o app subiu. Sem ela,
    /// gravar o agregado de agora apagaria o que já estava salvo — e o dia
    /// perderia as horas em que o app não estava rodando. Enquanto não dá
    /// para ler, o app <b>espera</b>: ele não grava o dia em vez de destruir
    /// o que já estava lá.
    /// </para>
    /// </summary>
    private bool _baseDoDiaPronta = true;

    /// <summary>
    /// O motivo da última falha de gravação (vazio quando deu tudo certo).
    ///
    /// <para>
    /// A tela mostra esse motivo: o histórico de ventoinha que não está
    /// sendo gravado precisa aparecer, senão o app cala sobre um dado que
    /// está se perdendo.
    /// </para>
    /// </summary>
    public string UltimoErro { get; private set; } = "";

    /// <summary>
    /// Carrega o que já está guardado (o recorde de ontem, por exemplo).
    ///
    /// <para>
    /// Sem isso, o app começaria do zero a cada abertura e o recorde seria
    /// sempre o do dia — e a comparação de desgaste não teria com o que
    /// comparar.
    /// </para>
    ///
    /// <para>
    /// A base do dia também vem do banco: o app pode ser fechado e aberto
    /// várias vezes no mesmo dia, e o agregado do dia não pode começar do
    /// zero (ele seria reescrito pela metade).
    /// </para>
    /// </summary>
    /// <returns>Verdadeiro quando o histórico foi lido.</returns>
    public bool Carregar()
    {
        try
        {
            var leitura = Banco.LerVentoinhas();

            if (!leitura.Completa)
            {
                // "Não deu para ler" não é "não tem nada": o app segue
                // funcionando e medindo, mas a tela diz que o histórico não
                // pôde ser lido.
                UltimoErro = "não deu para ler o histórico das ventoinhas";
                return false;
            }

            lock (_trava)
            {
                foreach (var registro in leitura.Linhas)
                {
                    var estado = EstadoDe(registro.Id);
                    estado.Hardware = registro.Hardware;
                    estado.Nome = registro.Nome;
                    estado.Registro = registro;
                }

                _dia = DateTime.Today;
            }

            CarregarDia(DateTime.Today, false);

            UltimoErro = "";
            return true;
        }
        catch (Exception ex)
        {
            UltimoErro = ex.Message;
            return false;
        }
    }

    /// <summary>
    /// Carrega o agregado de um dia do banco (a base do dia).
    ///
    /// <para>
    /// É a mesma regra do tráfego de internet: <b>a base do banco vem
    /// antes da medição</b>. Sem ela, cada abertura do app reescreveria o
    /// dia pela metade — e o histórico da manhã desapareceria sem aviso.
    /// </para>
    /// <param name="dia">O dia do agregado.</param>
    /// <param name="juntarComOQueJaFoiMedido">
    /// True quando o app já mediu alguma coisa deste dia (a base vem depois
    /// da medição, e as duas partes precisam ser somadas).
    /// </param>
    /// </summary>
    private void CarregarDia(DateTime dia, bool juntarComOQueJaFoiMedido)
    {
        try
        {
            var leitura = Banco.LerVentoinhasDoDia(dia);

            // Uma leitura pela metade não vale como base: sem esta
            // conferência, o app gravaria por cima de um agregado que não
            // leu inteiro — e o dia perderia o que já estava salvo.
            if (!leitura.Completa)
            {
                UltimoErro = "não deu para ler o histórico de hoje das ventoinhas";
                _baseDoDiaPronta = false;
                return;
            }

            lock (_trava)
            {
                foreach (var item in leitura.Linhas)
                {
                    var estado = EstadoDe(item.Id);
                    estado.Dia = juntarComOQueJaFoiMedido
                        ? VentoinhaRegras.JuntarDia(item, estado.Dia)
                        : item;
                }
            }

            _baseDoDiaPronta = true;
        }
        catch (Exception ex)
        {
            _baseDoDiaPronta = false;
            UltimoErro = ex.Message;
        }
    }

    /// <summary>
    /// Registra a leitura de agora das ventoinhas.
    ///
    /// <para>
    /// Uma linha de sensor sem valor não entra: um sensor que não sabe o
    /// valor (o LibreHardwareMonitor devolve infinito nesses casos) não é
    /// "0 RPM" — é ausência de leitura.
    /// </para>
    /// </summary>
    /// <param name="linhas">As linhas de sensor da volta.</param>
    /// <param name="tempCpu">A temperatura da CPU agora (null se não deu para ler).</param>
    /// <param name="tempGpu">A temperatura da GPU agora (null se não deu para ler).</param>
    /// <param name="agora">O instante da leitura.</param>
    public void Registrar(
        IReadOnlyList<SensorRow> linhas,
        float? tempCpu,
        float? tempGpu,
        DateTime agora)
    {
        try
        {
            VirarDia(agora);

            lock (_trava)
            {
                foreach (var linha in linhas)
                {
                    if (linha.Type != SensorType.Fan) continue;
                    if (linha.RawValue is not float rpm) continue;

                    var fonte = VentoinhaRegras.FonteDe(linha.HardwareType.ToString());

                    var leitura = new VentoinhaLeitura(
                        VentoinhaRegras.Identidade(linha.Hardware, linha.Name),
                        linha.Hardware,
                        linha.Name,
                        rpm,
                        TemperaturaDe(fonte, tempCpu, tempGpu));

                    var estado = EstadoDe(leitura.Id);
                    estado.Hardware = leitura.Hardware;
                    estado.Nome = leitura.Nome;
                    estado.Fonte = fonte;
                    estado.Leitura = leitura;
                    estado.UltimaMedida = agora;

                    estado.Registro = VentoinhaRegras.AtualizarRecorde(estado.Registro, leitura, agora);
                    estado.Dia = VentoinhaRegras.AtualizarDia(estado.Dia, leitura);

                    var veredito = VentoinhaRegras.Avaliar(estado.Registro, leitura);
                    estado.Veredito = veredito;

                    // A queda só vira sinal depois de um tempo sem parar: o
                    // app guarda desde quando a condição está valendo, e o
                    // núcleo decide se ela já durou o bastante.
                    if (veredito.Estado == VentoinhaEstado.SinalDeDegradacao)
                    {
                        if (estado.InicioDaQueda is null) estado.InicioDaQueda = agora;
                    }
                    else
                    {
                        estado.InicioDaQueda = null;
                    }
                }
            }
        }
        catch
        {
            // A coleta das ventoinhas não pode derrubar o app: a ventoinha é
            // um extra, e o histórico dos sensores continua funcionando.
        }
    }

    /// <summary>
    /// A temperatura de referência da ventoinha, conforme a fonte do seu
    /// hardware.
    /// </summary>
    private static double? TemperaturaDe(FonteTemperatura fonte, float? tempCpu, float? tempGpu) => fonte switch
    {
        FonteTemperatura.Cpu => tempCpu,
        FonteTemperatura.Gpu => tempGpu,
        _ => null,
    };

    /// <summary>
    /// Os cartões das ventoinhas — é isto que a tela consome.
    /// </summary>
    public IReadOnlyList<VentoinhaCartao> Cartoes()
    {
        var agora = DateTime.Now;

        lock (_trava)
        {
            var cartoes = new List<VentoinhaCartao>(_estados.Count);

            foreach (var estado in _estados.Values)
            {
                var veredito = estado.Veredito ?? VentoinhaRegras.VereditoSemLeitura(estado.Registro);

                TimeSpan? duracao = estado.InicioDaQueda is DateTime inicio
                    ? agora - inicio
                    : (TimeSpan?)null;

                cartoes.Add(VentoinhaRegras.Cartao(
                    estado.Registro, estado.Leitura, estado.Dia, veredito, duracao, estado.Fonte));
            }

            return cartoes;
        }
    }

    /// <summary>
    /// O histórico das ventoinhas (o "RPM com o tempo"), a partir de um dia.
    /// </summary>
    public Leitura<VentoinhaHistorico> Historico(DateTime desde) => Banco.LerHistoricoVentoinhas(desde);

    /// <summary>
    /// Grava o que o app sabe: o recorde de cada ventoinha e o agregado do
    /// dia de hoje.
    /// </summary>
    /// <returns>Verdadeiro quando tudo foi gravado.</returns>
    public bool Gravar()
    {
        try
        {
            DateTime dia;

            lock (_trava) { dia = _dia; }

            // A base do dia vem antes da gravação: se ela ainda não pôde ser
            // lida, o app tenta de novo (a base pode ter voltado). Sem a
            // base, gravar o agregado de agora apagaria o que já estava
            // salvo do dia.
            if (dia != DateTime.MinValue && !_baseDoDiaPronta)
                CarregarDia(dia, true);

            List<VentoinhaRegistro> registros;
            List<VentoinhaDoDia> doDia;

            lock (_trava)
            {
                registros = _estados.Values
                    .Where(e => e.Registro is not null)
                    .Select(e => e.Registro!)
                    .ToList();

                doDia = _estados.Values
                    .Where(e => e.Dia is not null)
                    .Select(e => e.Dia!)
                    .ToList();
            }

            bool ok = true;

            foreach (var registro in registros)
                ok &= Banco.GravarVentoinha(registro);

            if (dia != DateTime.MinValue)
            {
                if (!_baseDoDiaPronta)
                {
                    // O app espera a base aparecer: gravar agora apagaria o
                    // que já estava salvo do dia.
                    UltimoErro = "não deu para ler o histórico de hoje das ventoinhas — " +
                                 "o app não está gravando o dia para não apagar o que já estava salvo";
                    return false;
                }

                ok &= Banco.GravarVentoinhasDoDia(dia, doDia);
            }

            UltimoErro = ok ? "" : "não foi possível gravar o histórico das ventoinhas";
            return ok;
        }
        catch (Exception ex)
        {
            UltimoErro = ex.Message;
            return false;
        }
    }

    /// <summary>
    /// Vira o dia: o dia que acabou recebe o agregado final e o novo começa
    /// do zero.
    ///
    /// <para>
    /// Sem isso, a madrugada entraria na conta do dia anterior — a mesma
    /// regra que o tráfego de internet já segue.
    /// </para>
    /// </summary>
    private void VirarDia(DateTime agora)
    {
        if (_dia == agora.Date) return;

        DateTime diaAnterior = _dia;
        DateTime diaNovo = agora.Date;

        // O dia que acabou é gravado ANTES de o agregado ser zerado: gravar
        // depois zeraria o dia anterior no banco (ele entraria vazio).
        if (diaAnterior != DateTime.MinValue)
            GravarDia(diaAnterior);

        lock (_trava)
        {
            _dia = diaNovo;

            // O agregado do dia novo começa sem nada em memória — a base
            // dele vem do banco logo abaixo (o app pode ter rodado antes
            // neste mesmo dia).
            foreach (var estado in _estados.Values)
                estado.Dia = null;
        }

        CarregarDia(diaNovo, false);
    }

    /// <summary>Grava o agregado de um dia (usado na virada do dia).</summary>
    private void GravarDia(DateTime dia)
    {
        try
        {
            // A base do dia que acabou pode ter ficado de fora até agora:
            // tenta de novo antes de desistir.
            if (!_baseDoDiaPronta)
                CarregarDia(dia, true);

            List<VentoinhaDoDia> doDia;

            lock (_trava)
            {
                doDia = _estados.Values
                    .Where(e => e.Dia is not null)
                    .Select(e => e.Dia!)
                    .ToList();
            }

            if (doDia.Count == 0) return;

            if (!_baseDoDiaPronta)
            {
                // Sem a base, gravar o dia apagaria o que já estava salvo
                // dele. O app espera a base aparecer.
                UltimoErro = "não deu para ler o histórico do dia das ventoinhas — " +
                             "o app não gravou o dia para não apagar o que já estava salvo";
                return;
            }

            if (!Banco.GravarVentoinhasDoDia(dia, doDia))
                UltimoErro = "não foi possível gravar o histórico das ventoinhas";
        }
        catch (Exception ex)
        {
            UltimoErro = ex.Message;
        }
    }

    /// <summary>
    /// O estado de uma ventoinha (criado se ainda não existir).
    /// </summary>
    private Estado EstadoDe(string id)
    {
        if (!_estados.TryGetValue(id, out var estado))
        {
            estado = new Estado { Id = id };
            _estados[id] = estado;
        }

        return estado;
    }
}
