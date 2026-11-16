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
/// Tudo que ela recebe entra por função: o relógio, a leitura do teclado, o
/// silêncio, a repetição e o "soltar". É o que deixa a vigia testável sem
/// Windows em cima.
/// </para>
/// </summary>
public sealed class MonitorTeclaPresa
{
    private readonly Func<IReadOnlyList<Modificadora>> _lerEstado;
    private readonly Func<DateTime> _agora;
    private readonly Func<TimeSpan?> _silencioDoTeclado;
    private readonly Func<bool> _repeticaoLigada;
    private readonly Action<IReadOnlyList<int>> _soltar;

    /// <summary>
    /// Desde quando cada tecla está caída. A trava existe porque a tela e a
    /// vigia rodam em threads diferentes e o dicionário é lido e escrito ao
    /// mesmo tempo.
    /// </summary>
    private readonly Dictionary<int, DateTime> _caidaEm = new();

    /// <summary>Quando cada família foi corrigida pela última vez (espera de 5 min).</summary>
    private readonly Dictionary<string, DateTime> _ultimaCorrecao = new();

    /// <summary>Quantas correções a vigia fez nesta sessão.</summary>
    private int _correcoes;

    private readonly object _tranco = new();

    /// <summary>Disparado quando uma tecla presa é solta.</summary>
    public event Action<TeclaPresa>? Corrigida;

    /// <summary>
    /// Cria a vigia.
    /// </summary>
    /// <param name="lerEstado">Devolve as modificadoras que o Windows diz que estão pressionadas.</param>
    /// <param name="agora">Relógio (UTC). Sem ele, o relógio do sistema.</param>
    /// <param name="silencioDoTeclado">
    /// Há quanto tempo o teclado não manda evento. Null quando não dá para
    /// saber (gancho fora) — e aí a vigia se cala.
    /// </param>
    /// <param name="repeticaoLigada">A repetição automática do teclado está ligada?</param>
    /// <param name="soltar">Recebe as teclas a soltar.</param>
    public MonitorTeclaPresa(
        Func<IReadOnlyList<Modificadora>> lerEstado,
        Func<DateTime>? agora = null,
        Func<TimeSpan?>? silencioDoTeclado = null,
        Func<bool>? repeticaoLigada = null,
        Action<IReadOnlyList<int>>? soltar = null)
    {
        _lerEstado = lerEstado;
        _agora = agora ?? (() => DateTime.UtcNow);
        _silencioDoTeclado = silencioDoTeclado ?? (() => null);
        _repeticaoLigada = repeticaoLigada ?? (() => false);
        _soltar = soltar ?? (_ => { });
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
            // Falhar em ler o teclado não pode derrubar a vigia.
            return;
        }

        var agora = _agora();

        // Quem caiu e quando. A trava é só desta conta: a correção (que fala
        // com o Windows) acontece fora dela.
        var caidas = new List<(Modificadora Modificadora, DateTime Desde, TimeSpan HaQuanto)>();
        var ids = new HashSet<int>();
        lock (_tranco)
        {
            foreach (var modificadora in estado)
            {
                ids.Add(modificadora.Vk);
                if (!_caidaEm.ContainsKey(modificadora.Vk)) _caidaEm[modificadora.Vk] = agora;
                DateTime caiuEm = _caidaEm[modificadora.Vk];
                caidas.Add((modificadora, caiuEm, agora - caiuEm));
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
        catch { return; }

        bool repeticao;
        try { repeticao = _repeticaoLigada(); }
        catch { return; }

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

        var vks = presas.Select(p => p.Modificadora.Vk).Distinct().ToList();
        var familias = TeclaPresaRegras.Families(presas.Select(p => p.Modificadora));
        var desde = presas.Min(p => p.Desde);
        var haQuanto = agora - desde;

        try
        {
            _soltar(vks);
        }
        catch
        {
            // Soltar é a ação; se ela falhar, a vigia apenas não corrige
            // agora (o diário registra e a próxima rodada tenta de novo).
            return;
        }

        lock (_tranco)
        {
            foreach (var vk in vks) _caidaEm.Remove(vk);
            foreach (var familia in TeclaPresaRegras.ListaDeFamilias(presas.Select(p => p.Modificadora)))
            {
                // A espera é por família de tecla ("Shift"), e não pela frase
                // montada ("Shift e Ctrl").
                _ultimaCorrecao[familia] = agora;
            }
            _correcoes++;
        }

        Corrigida?.Invoke(new TeclaPresa(familias, desde, haQuanto));
    }

    /// <summary>
    /// A família pode ser corrigida agora? (A espera evita que o aviso vire
    /// spam quando o "soltar" não resolve de primeira.)
    /// </summary>
    private bool PodeCorrigir(string familia, DateTime agora)
    {
        lock (_tranco)
        {
            if (!_ultimaCorrecao.TryGetValue(familia, out DateTime ultima)) return true;
            return agora - ultima >= TeclaPresaRegras.EsperaDepoisDaCorrecao;
        }
    }

    /// <summary>
    /// Quantas vezes a vigia corrigiu uma tecla presa nesta sessão.
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
