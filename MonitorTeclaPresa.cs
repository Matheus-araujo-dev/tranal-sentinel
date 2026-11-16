namespace TranalSentinel;

/// <summary>
/// A vigia da tecla presa: acompanha as modificadoras e manda soltar as que
/// ficarem presas.
///
/// <para>
/// A vigia é a orquestradora: ela pergunta o estado da tecla, guarda desde
/// quando ela está caída, e quando a regra pura
/// (<see cref="TeclaPresaRegras"/>) diz que a tecla está presa, manda
/// soltar. Quem mede é o Windows (<see cref="TeclasWindows"/>) e quem decide
/// é a regra — a vigia não tem regra própria.
/// </para>
///
/// <para>
/// O relógio da vigia é <b>monotônico</b> (milissegundos que só andam para
/// frente). O relógio do Windows pode pular quando a hora é corrigida ou o
/// fuso muda: com ele, um salto de um minuto faria a vigia achar que a tecla
/// está caída muito mais tempo do que está, e soltaria a tecla que o dono
/// está segurando.
/// </para>
///
/// <para>
/// Tudo que ela recebe entra por função: o relógio, a leitura do teclado, o
/// silêncio, a repetição e o "soltar". É o que deixa a vigia testável sem
/// Windows em cima.
/// </para>
/// </summary>
public sealed class MonitorTeclaPresa
{
    private readonly Func<IReadOnlyList<Modificadora>> _lerEstado;
    private readonly Func<long> _agora;
    private readonly Func<TimeSpan?> _silencioDoTeclado;
    private readonly Func<bool> _repeticaoLigada;
    private readonly Action<IReadOnlyList<int>> _soltar;
    private readonly Func<DateTime> _relogio;
    private readonly Func<bool> _ganchoEstaVivo;
    private readonly Func<bool> _podeAgir;

    /// <summary>
    /// Desde quando cada tecla está caída (em milissegundos do relógio
    /// monotônico). A trava existe porque a tela e a vigia rodam em threads
    /// diferentes e o dicionário é lido e escrito ao mesmo tempo.
    /// </summary>
    private readonly Dictionary<int, long> _caidaEm = new();

    /// <summary>Quando cada família foi corrigida pela última vez (espera de 5 min).</summary>
    private readonly Dictionary<string, long> _ultimaCorrecao = new();

    /// <summary>Quantas vezes o "soltar" não resolveu, por família.</summary>
    private readonly Dictionary<string, int> _falhas = new();

    /// <summary>Quantas correções a vigia tentou nesta sessão.</summary>
    private int _correcoes;

    private readonly object _tranco = new();

    /// <summary>Disparado quando uma tecla presa é solta (e o estado confirma).</summary>
    public event Action<TeclaPresa>? Corrigida;

    /// <summary>
    /// Disparado quando o app tentou soltar a tecla e ela continuou presa —
    /// o app não pode dizer que soltou o que não soltou.
    /// </summary>
    public event Action<TeclaPresa>? NaoConseguiu;

    /// <summary>
    /// Cria a vigia.
    /// </summary>
    /// <param name="lerEstado">Devolve as modificadoras que o Windows diz que estão pressionadas.</param>
    /// <param name="agora">Relógio monotônico em milissegundos. Sem ele, o do Windows.</param>
    /// <param name="silencioDoTeclado">
    /// Há quanto tempo o teclado não manda evento. Null quando não dá para
    /// saber (gancho fora) — e aí a vigia se cala.
    /// </param>
    /// <param name="repeticaoLigada">A repetição automática do teclado está ligada?</param>
    /// <param name="soltar">Recebe as teclas a soltar.</param>
    /// <param name="relogio">Relógio de parede, só para carimbar o diário.</param>
    /// <param name="ganchoEstaVivo">
    /// O gancho de teclado está funcionando? O Windows remove um gancho de
    /// baixo nível em silêncio quando o PC trava, e aí o "silêncio" do
    /// teclado é falso. Sem essa prova, o app não solta nada.
    /// </param>
    /// <param name="podeAgir">
    /// O dono ainda quer a correção automática? A preferência é conferida
    /// aqui, **na hora de soltar**: o dono pode desligar enquanto a vigia
    /// espera a prova do gancho, e o app tem que obedecer.
    /// </param>
    public MonitorTeclaPresa(
        Func<IReadOnlyList<Modificadora>> lerEstado,
        Func<long>? agora = null,
        Func<TimeSpan?>? silencioDoTeclado = null,
        Func<bool>? repeticaoLigada = null,
        Action<IReadOnlyList<int>>? soltar = null,
        Func<DateTime>? relogio = null,
        Func<bool>? ganchoEstaVivo = null,
        Func<bool>? podeAgir = null)
    {
        _lerEstado = lerEstado;
        _agora = agora ?? (() => Environment.TickCount64);
        _silencioDoTeclado = silencioDoTeclado ?? (() => null);
        _repeticaoLigada = repeticaoLigada ?? (() => false);
        _soltar = soltar ?? (_ => { });
        _relogio = relogio ?? (() => DateTime.UtcNow);
        // Sem a prova, o app não solta: soltar a tecla que o dono está
        // segurando é pior do que a tecla presa.
        _ganchoEstaVivo = ganchoEstaVivo ?? (() => false);
        _podeAgir = podeAgir ?? (() => true);
    }

    /// <summary>
    /// Roda uma verificação: mede, decide pela regra e corrige o que estiver
    /// preso.
    /// </summary>
    public void Verificar()
    {
        IReadOnlyList<Modificadora> estado;
        try
        {
            estado = _lerEstado() ?? Array.Empty<Modificadora>();
        }
        catch
        {
            // Falhar em ler o teclado não pode derrubar a vigia: no ciclo
            // seguinte ela tenta de novo.
            return;
        }

        long agora = _agora();

        // Quem caiu e quando. A trava é só desta conta: a correção (que fala
        // com o Windows) acontece fora dela.
        var caidas = new List<(Modificadora Modificadora, long Desde, TimeSpan HaQuanto)>();
        var ids = new HashSet<int>();
        lock (_tranco)
        {
            foreach (var modificadora in estado)
            {
                ids.Add(modificadora.Vk);
                if (!_caidaEm.ContainsKey(modificadora.Vk)) _caidaEm[modificadora.Vk] = agora;
                long desde = _caidaEm[modificadora.Vk];
                caidas.Add((modificadora, desde, TimeSpan.FromMilliseconds(agora - desde)));
            }

            // A tecla que subiu não está mais caída: some do histórico.
            foreach (var vk in _caidaEm.Keys.ToList())
            {
                if (!ids.Contains(vk)) _caidaEm.Remove(vk);
            }
        }

        if (caidas.Count == 0) return;

        TimeSpan? silencio;
        try { silencio = _silencioDoTeclado(); }
        catch
        {
            // Sem o gancho não há como saber se o teclado está em silêncio: a
            // vigia se cala (e tenta de novo no ciclo seguinte).
            return;
        }

        bool repeticao;
        try { repeticao = _repeticaoLigada(); }
        catch
        {
            // Sem a resposta do Windows sobre a repetição automática, o
            // silêncio não prova nada: a vigia se cala.
            return;
        }

        var presas = caidas
            .Where(c => TeclaPresaRegras.EstaPresa(
                pressionada: true,
                repeticaoDoTecladoLigada: repeticao,
                atividadeConhecida: silencio.HasValue,
                haQuantoEstaCaiada: c.HaQuanto,
                semAtividadeDeTeclado: silencio ?? TimeSpan.Zero))
            .Where(c => PodeCorrigir(c.Modificadora.Familia, agora))
            .ToList();

        if (presas.Count == 0) return;

        var familias = TeclaPresaRegras.ListaDeFamilias(presas.Select(p => p.Modificadora));
        var nomes = presas.Select(p => p.Modificadora.Nome).Distinct().ToList();

        // O "soltar" precisa chegar em todas as teclas da família (a genérica
        // e as duas laterais): o Windows marca a genérica junto com a lateral.
        var vks = new List<int>();
        foreach (var familia in familias) vks.AddRange(TeclaPresaRegras.VksDaFamilia(familia));

        // Com mais de uma família no mesmo aviso, o tempo é o da mais
        // recente (o mais conservador): o texto diz "pelo menos", e o número
        // precisa valer para todas as teclas do aviso.
        var haQuanto = TimeSpan.FromMilliseconds(agora - presas.Max(p => p.Desde));

        // Antes de soltar: o gancho pode ter morrido. O Windows remove um
        // gancho de baixo nível **em silêncio** quando o PC trava — e com o
        // gancho morto o "silêncio" do teclado é falso: o app soltaria a
        // tecla que o dono está segurando, que é o pior dano possível.
        // Sem prova de que o gancho está vivo, o app se cala.
        if (!_ganchoEstaVivo()) return;

        // O dono pode ter desligado a correção enquanto a vigia esperava a
        // prova do gancho: agora é a hora de soltar, e agora ele não quer.
        if (!_podeAgir()) return;

        try
        {
            _soltar(vks);
        }
        catch
        {
            // Soltar é a ação; se ela falhar, a vigia registra a falha (o
            // dono é avisado) e espera como nas outras tentativas — senão
            // ela tentaria a cada 5 segundos, para sempre, sem ninguém ver.
            lock (_tranco)
            {
                foreach (var familia in familias) _ultimaCorrecao[familia] = agora;
                _correcoes++;
            }

            var tentativa = new TeclaPresa(nomes, familias, _relogio(), haQuanto);
            if (PodeAvisarFalha(familias)) NaoConseguiu?.Invoke(tentativa);
            return;
        }

        // Confere: a tecla saiu mesmo? Sem esta conferência, o app diria
        // que soltou a tecla mesmo quando o "soltar" não limpou o estado.
        bool aindaPresa = ContinuaPresa(familias);

        lock (_tranco)
        {
            if (!aindaPresa)
            {
                foreach (var vk in vks) _caidaEm.Remove(vk);
            }
            foreach (var familia in familias) _ultimaCorrecao[familia] = agora;
            _correcoes++;
        }

        var registro = new TeclaPresa(nomes, familias, _relogio(), haQuanto);
        if (aindaPresa)
        {
            // Tecla que não solta: o aviso não pode virar spam (a cada
            // 5 minutos, para sempre). Depois de algumas tentativas, o
            // app para de avisar e continua tentando em silencio.
            if (PodeAvisarFalha(familias)) NaoConseguiu?.Invoke(registro);
        }
        else Corrigida?.Invoke(registro);
    }

    /// <summary>
    /// A tecla continua presa depois do "soltar"?
    /// </summary>
    private bool ContinuaPresa(IReadOnlyList<string> familias)
    {
        IReadOnlyList<Modificadora> estado;
        try
        {
            estado = _lerEstado() ?? Array.Empty<Modificadora>();
        }
        catch
        {
            // Sem conseguir reler, o app não pode afirmar que soltou: fica
            // quieto sobre o resultado (e a próxima rodada confere de novo).
            return true;
        }

        foreach (var familia in familias)
        {
            foreach (var vk in TeclaPresaRegras.VksDaFamilia(familia))
            {
                foreach (var modificadora in estado)
                {
                    if (modificadora.Vk == vk) return true;
                }
            }
        }
        return false;
    }

    /// <summary>
    /// A família pode ser corrigida agora? (A espera evita que o aviso vire
    /// spam quando o "soltar" não resolve de primeira.)
    /// </summary>
    private bool PodeCorrigir(string familia, long agora)
    {
        lock (_tranco)
        {
            if (!_ultimaCorrecao.TryGetValue(familia, out long ultima)) return true;
            return TimeSpan.FromMilliseconds(agora - ultima) >= TeclaPresaRegras.EsperaDepoisDaCorrecao;
        }
    }

    /// <summary>
    /// Esquece o que a vigia estava acompanhando.
    /// </summary>
    /// <remarks>
    /// Usado quando o dono desliga a correção: sem isso, o silencio
    /// continuaria crescendo e, ao religar, a vigia soltaria uma tecla que
    /// não estava presa (o silencio antigo valeria como prova).
    /// </remarks>
    public void Esquecer()
    {
        lock (_tranco)
        {
            _caidaEm.Clear();
            _ultimaCorrecao.Clear();
            _falhas.Clear();
        }
    }

    /// <summary>
    /// O app ainda avisa que não conseguiu soltar a tecla?
    /// </summary>
    /// <remarks>
    /// A resposta é sim até <see cref="MaximoDeAvisosDeFalha"/> vezes por
    /// família: depois disso o app continua tentando soltar (em silêncio),
    /// porque um aviso a cada 5 minutos não ajuda o dono — ele só ocupa a tela.
    /// </remarks>
    private bool PodeAvisarFalha(IReadOnlyList<string> familias)
    {
        lock (_tranco)
        {
            foreach (var familia in familias)
            {
                int falhas = _falhas.TryGetValue(familia, out int n) ? n : 0;
                if (falhas >= MaximoDeAvisosDeFalha) return false;
            }

            foreach (var familia in familias) _falhas[familia] = (_falhas.TryGetValue(familia, out int n) ? n : 0) + 1;
            return true;
        }
    }

    /// <summary>
    /// Quantas vezes o app avisa que não conseguiu soltar a mesma tecla
    /// (depois disso, ele tenta em silêncio).
    /// </summary>
    public const int MaximoDeAvisosDeFalha = 3;

    /// <summary>
    /// Quantas vezes a vigia tentou corrigir uma tecla presa nesta sessão.
    /// </summary>
    public int CorrecoesFeitas
    {
        get
        {
            // O contador é de correções, não de famílias: uma correção que
            // soltou Shift e Ctrl ao mesmo tempo é uma correção, não duas.
            lock (_tranco) return _correcoes;
        }
    }
}
