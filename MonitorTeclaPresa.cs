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
    private readonly Func<bool> _ganchoEstaVivo;
    private readonly Func<bool> _podeAgir;

    /// <summary>
    /// Desde quando cada tecla está caída (em milissegundos do relógio
    /// monotônico). A trava existe porque a tela e a vigia rodam em threads
    /// diferentes e o dicionário é lido e escrito ao mesmo tempo.
    /// </summary>
    private readonly Dictionary<int, long> _caidaEm = new();

    /// <summary>
    /// Quando cada família tentou soltar a tecla pela última vez (relógio
    /// monotônico, em milissegundos).
    /// </summary>
    /// <remarks>
    /// O nome é "tentativa", e não "correção", porque a espera de 1 min vale
    /// para a tentativa que <b>falhou</b>: uma correção que deu certo não
    /// trava a próxima (medido no PC do dono em 16/11/2026: a segunda
    /// travada, logo depois da primeira, ficava 5 minutos sem tratamento
    /// nenhum).
    /// </remarks>
    private readonly Dictionary<string, long> _ultimaTentativa = new();

    /// <summary>
    /// As famílias cuja última tentativa não deu certo: só elas esperam
    /// antes de tentar de novo.
    /// </summary>
    private readonly HashSet<string> _familiasQueFalharam = new();

    /// <summary>
    /// Os avisos de SUCESSO dados a cada família: quantos e quando foi o
    /// último.
    /// </summary>
    /// <remarks>
    /// O teto segura o aviso na tela, não a correção: o app continua
    /// soltando a tecla, porque um aviso a cada 35 segundos não ajuda
    /// ninguém.
    /// </remarks>
    private readonly Dictionary<string, (int Quantos, long Quando)> _avisosDeSucesso = new();

    /// <summary>
    /// Os avisos de FALHA dados a cada família: quantos e quando foi o último.
    /// </summary>
    /// <remarks>
    /// O orçamento é separado do sucesso de propósito: a falha é o aviso que
    /// o dono mais precisa ver (a tecla continua presa), e ela não pode ser
    /// calada por três correções que deram certo.
    /// </remarks>
    private readonly Dictionary<string, (int Quantos, long Quando)> _avisosDeFalha = new();

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
        Func<bool>? ganchoEstaVivo = null,
        Func<bool>? podeAgir = null)
    {
        _lerEstado = lerEstado;
        _agora = agora ?? (() => Environment.TickCount64);
        _silencioDoTeclado = silencioDoTeclado ?? (() => null);
        _repeticaoLigada = repeticaoLigada ?? (() => false);
        _soltar = soltar ?? (_ => { });
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
                if (ids.Contains(vk)) continue;
                _caidaEm.Remove(vk);

                // O episódio acabou (a tecla subiu). Se a última tentativa
                // desta família tinha falhado, a espera acaba com ele: um
                // travamento novo é tratado no tempo normal. Sem isso, a
                // segunda travada ficava 5 minutos sem tratamento (medido
                // no PC do dono em 16/11/2026).
                string familia = FamiliaDoVk(vk);
                if (familia.Length == 0) continue;
                if (_caidaEm.Keys.Any(k => FamiliaDoVk(k) == familia)) continue;
                _familiasQueFalharam.Remove(familia);
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
        // Os nomes que o dono reconhece: a genérica (Shift) fica de fora
        // quando há lateral (Shift direito) — o Windows marca as duas, mas
        // "Shift e Shift direito" no aviso soa errado.
        var nomes = TeclaPresaRegras.NomesObservados(presas.Select(p => p.Modificadora));

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
            // O aviso é decidido antes de marcar a tentativa: a janela de
            // avisos olha o Último AVISO dado a essa família (o de agora
            // ainda não conta).
            bool avisarFalha = PodeAvisar(familias, agora, sucesso: false);

            lock (_tranco)
            {
                foreach (var familia in familias)
                {
                    _ultimaTentativa[familia] = agora;
                    _familiasQueFalharam.Add(familia);
                }
                _correcoes++;
            }

            NaoConseguiu?.Invoke(
                new TeclaPresa(nomes, familias, haQuanto) { Avisar = avisarFalha });
            return;
        }

        // Confere: a tecla saiu mesmo? Sem esta conferência, o app diria
        // que soltou a tecla mesmo quando o "soltar" não limpou o estado.
        //
        // A conferência é POR FAMÍLIA: o "soltar" pode limpar o Shift e não
        // limpar o Ctrl (o Windows re-marca a tecla). Julgar o aviso inteiro
        // faria o app dizer que o Shift "continuou preso" quando ele já foi
        // solto — e faria a família do Shift esperar à toa.
        var familiasAindaPresas = FamiliasQueContinuamPresas(familias);

        // O aviso é decidido antes de marcar a tentativa: a janela de avisos
        // olha o Último AVISO dado a essa família (o de agora ainda não
        // conta).
        bool algumaAindaPresa = familiasAindaPresas.Count > 0;
        bool avisar = PodeAvisar(
            algumaAindaPresa ? familiasAindaPresas : familias,
            agora,
            sucesso: !algumaAindaPresa);

        lock (_tranco)
        {
            foreach (var familia in familias)
            {
                _ultimaTentativa[familia] = agora;

                if (familiasAindaPresas.Contains(familia))
                {
                    // A tecla continua presa: a família espera antes de
                    // tentar de novo (é o que impede o app de martelar a
                    // tecla a cada 5 segundos).
                    _familiasQueFalharam.Add(familia);
                    continue;
                }

                // A família foi solta de verdade: ela fica sem pendência
                // (um novo travamento é tratado no tempo normal).
                _familiasQueFalharam.Remove(familia);
                foreach (int vk in TeclaPresaRegras.VksDaFamilia(familia)) _caidaEm.Remove(vk);
            }
            _correcoes++;
        }

        if (familiasAindaPresas.Count > 0)
        {
            // Tecla que não solta: o aviso não pode virar spam (o teto
            // segura o aviso, não a correção). Depois de algumas
            // tentativas, o app para de avisar e continua tentando soltar
            // em silêncio.
            //
            // O aviso é sobre as teclas que CONTINUARAM presas: o "soltar"
            // pode ter limpo uma família e deixado a outra.
            var presasQueFicaram = presas
                .Where(p => familiasAindaPresas.Contains(p.Modificadora.Familia))
                .ToList();

            var nomesQueFicaram = TeclaPresaRegras.NomesObservados(
                presasQueFicaram.Select(p => p.Modificadora));

            var tempoQueFicou = TimeSpan.FromMilliseconds(
                agora - presasQueFicaram.Max(p => p.Desde));

            NaoConseguiu?.Invoke(new TeclaPresa(
                nomesQueFicaram, familiasAindaPresas, tempoQueFicou) { Avisar = avisar });
        }
        else
        {
            Corrigida?.Invoke(
                new TeclaPresa(nomes, familias, haQuanto) { Avisar = avisar });
        }
    }

    /// <summary>
    /// Quais famílias continuam presas depois do "soltar"?
    /// </summary>
    /// <remarks>
    /// A resposta é por família: o "soltar" pode limpar o Shift e não limpar
    /// o Ctrl (o Windows re-marca a tecla). Julgar o aviso inteiro faria o
    /// app dizer que o Shift "continuou preso" quando ele já foi solto.
    /// </remarks>
    private IReadOnlyList<string> FamiliasQueContinuamPresas(IReadOnlyList<string> familias)
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
            return familias;
        }

        var lista = new List<string>();
        foreach (var familia in familias)
        {
            foreach (int vk in TeclaPresaRegras.VksDaFamilia(familia))
            {
                if (!estado.Any(m => m.Vk == vk)) continue;
                lista.Add(familia);
                break;
            }
        }
        return lista;
    }

    /// <summary>A que família pertence este código de tecla ("" se não for).</summary>
    private static string FamiliaDoVk(int vk)
    {
        foreach (var modificadora in TeclaPresaRegras.Modificadoras)
        {
            if (modificadora.Vk == vk) return modificadora.Familia;
        }
        return "";
    }

    /// <summary>
    /// A família pode ser corrigida agora?
    /// </summary>
    /// <remarks>
    /// Só a família cuja última tentativa <b>falhou</b> espera: é a espera
    /// que segura o aviso quando o "soltar" não resolve de primeira. Uma
    /// tentativa que deu certo não trava a próxima — e uma tentativa que
    /// falhou deixa de valer quando o episódio acaba (a tecla sobe).
    /// </remarks>
    private bool PodeCorrigir(string familia, long agora)
    {
        lock (_tranco)
        {
            if (!_familiasQueFalharam.Contains(familia)) return true;
            if (!_ultimaTentativa.TryGetValue(familia, out long ultima)) return true;
            return TimeSpan.FromMilliseconds(agora - ultima) >= TeclaPresaRegras.EsperaDepoisDaTentativa;
        }
    }

    /// <summary>
    /// Esquece o que a vigia estava acompanhando.
    /// </summary>
    /// <remarks>
    /// Usado quando o dono desliga a correção: sem isso, o silêncio
    /// continuaria crescendo e, ao religar, a vigia soltaria uma tecla que
    /// não estava presa (o silêncio antigo valeria como prova).
    /// </remarks>
    public void Esquecer()
    {
        lock (_tranco)
        {
            _caidaEm.Clear();
            _ultimaTentativa.Clear();
            _familiasQueFalharam.Clear();
            _avisosDeSucesso.Clear();
            _avisosDeFalha.Clear();
        }
    }

    /// <summary>
    /// O dono tem que ser avisado (o aviso na tela) desta correção?
    /// </summary>
    /// <remarks>
    /// O teto é por família e por janela: depois de
    /// <see cref="TeclaPresaRegras.MaximoDeAvisos"/> avisos, a família fica
    /// em silêncio até a janela passar (ela recomeça
    /// <see cref="TeclaPresaRegras.JanelaDeAvisos"/> depois do último aviso,
    /// não da última tentativa).
    ///
    /// <para>
    /// O que cala é o aviso na tela, não a correção: o app continua soltando
    /// a tecla (e o diário continua registrando). Um aviso a cada 35 segundos
    /// não ajuda o dono — ele só ocupa a tela.
    /// </para>
    /// </remarks>
    private bool PodeAvisar(IReadOnlyList<string> familias, long agora, bool sucesso)
    {
        lock (_tranco)
        {
            Dictionary<string, (int Quantos, long Quando)> contadores =
                sucesso ? _avisosDeSucesso : _avisosDeFalha;

            bool avisar = false;
            foreach (var familia in familias)
            {
                contadores.TryGetValue(familia, out (int Quantos, long Quando) registro);
                int avisos = registro.Quantos;

                // A janela passou desde o ÚLTIMO AVISO: a família volta a ser
                // avisada. A janela conta do aviso (e não da última
                // tentativa): se contasse da tentativa, um travamento que se
                // repete renova a janela a cada rodada e o dono nunca mais
                // ouve falar do problema.
                if (TimeSpan.FromMilliseconds(agora - registro.Quando) >= TeclaPresaRegras.JanelaDeAvisos)
                {
                    avisos = 0;
                }

                if (avisos >= TeclaPresaRegras.MaximoDeAvisos) continue;

                contadores[familia] = (avisos + 1, agora);
                avisar = true;
            }
            return avisar;
        }
    }

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
