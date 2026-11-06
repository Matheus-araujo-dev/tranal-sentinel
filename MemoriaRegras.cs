namespace TranalSentinel;

/// <summary>Como a memória está configurada em relação ao que o módulo de fato oferece.</summary>
public enum ModoMemoria
{
    /// <summary>Não deu para saber (sem leitura do SPD ou sem clock configurado).</summary>
    Desconhecido,

    /// <summary>No padrão JEDEC, sem nenhum perfil de overclock ligado.</summary>
    PadraoJedec,

    /// <summary>Rodando no perfil que o módulo oferece (XMP/EXPO).</summary>
    PerfilAtivo,

    /// <summary>Acima do padrão JEDEC, mas por ajuste manual — fora do perfil do módulo.</summary>
    OverclockManual,

    /// <summary>Acima do perfil que o módulo oferece.</summary>
    AcimaDoPerfil
}

/// <summary>Como um tempo ativo se compara com o tempo do perfil do módulo.</summary>
public enum SituacaoTiming
{
    /// <summary>Não dá para comparar (o perfil não traz esse tempo).</summary>
    SemComparacao,

    /// <summary>Igual ao perfil (dentro da tolerância).</summary>
    Igual,

    /// <summary>Mais apertado que o perfil — é o ajuste manual que costuma dar travamento.</summary>
    MaisAperto,

    /// <summary>Mais solto que o perfil.</summary>
    MaisSolto
}

/// <summary>Um tempo de memória comparado entre o que está ativo e o que o perfil oferece.</summary>
public sealed class ComparacaoTiming
{
    /// <summary>Nome do tempo ("tCL", "tRCD"...).</summary>
    public string Nome { get; init; } = "";

    /// <summary>Valor ativo, em ciclos do clock de memória.</summary>
    public int? Ativo { get; init; }

    /// <summary>Valor do perfil de referência, em ciclos.</summary>
    public int? Perfil { get; init; }

    /// <summary>
    /// True nos tempos primários (tCL, tRCD, tRP, tRAS, tRC): os que a BIOS
    /// copia do perfil e que o usuário mexe quando aperta a memória.
    /// </summary>
    public bool Primario { get; init; }

    /// <summary>Como o ativo se compara com o perfil.</summary>
    public SituacaoTiming Situacao { get; init; }
}

/// <summary>O veredito sobre os tempos ativos comparados com um perfil.</summary>
public sealed class RelatorioTimings
{
    /// <summary>Nome da referência comparada ("XMP 2.0", "JEDEC"...).</summary>
    public string Referencia { get; init; } = "";

    /// <summary>Comparação de cada tempo.</summary>
    public List<ComparacaoTiming> Itens { get; init; } = new();

    /// <summary>Quantos tempos primários estão mais apertados que a referência.</summary>
    public int ApertadosPrimarios { get; init; }

    /// <summary>True quando algum tempo primário está mais apertado que a referência.</summary>
    public bool HaAjusteManual => ApertadosPrimarios > 0;

    /// <summary>Frase com o veredito.</summary>
    public string Resumo => HaAjusteManual
        ? $"{ApertadosPrimarios} tempo(s) primário(s) mais apertado(s) que o {Referencia}."
        : $"Nenhum tempo primário está mais apertado que o {Referencia}.";
}


/// <summary>
/// Regra da memória: compara o clock que a BIOS configurou com o que o módulo
/// de fato oferece (padrão JEDEC e perfis XMP/EXPO lidos do SPD).
/// </summary>
/// <remarks>
/// Esta é a única fonte da verdade sobre "a memória está no perfil ou foi
/// ajustada à mão?". A tela TRANAL MEMÓRIA e o TRANAL SAÚDE consomem esta
/// mesma classificação — elas não recalculam nada.
/// </remarks>
public static class MemoriaRegras
{
    /// <summary>
    /// Escolhe o módulo que manda na classificação: o que oferece a menor
    /// velocidade. Com módulos diferentes é o mais fraco que limita o clock do
    /// sistema — e é ele que a tela e o TRANAL SAÚDE precisam descrever.
    ///
    /// <para>
    /// O desempate é determinístico (não depende da ordem em que a coleta
    /// devolveu os módulos): primeiro a velocidade oferecida, depois o padrão
    /// JEDEC, depois o perfil e, por último, o part number.
    /// </para>
    /// </summary>
    /// <param name="modulos">Os módulos lidos no SPD.</param>
    public static SpdModulo? ModuloQueManda(IEnumerable<SpdModulo> modulos)
    {
        ArgumentNullException.ThrowIfNull(modulos);

        SpdModulo? escolhido = null;
        (int Oferece, int Jedec, int Perfil, string Parte) melhor = (int.MaxValue, int.MaxValue, int.MaxValue, "");

        foreach (SpdModulo modulo in modulos)
        {
            // O que o módulo consegue fazer: o perfil gravado nele, quando
            // existe; senão, o padrão JEDEC.
            int jedec = modulo.Jedec.VelocidadeMts;
            int perfil = modulo.MelhorPerfil?.Timing.VelocidadeMts ?? 0;
            int oferece = Math.Max(jedec, perfil);
            if (oferece <= 0) continue;

            var atual = (Oferece: oferece, Jedec: jedec, Perfil: perfil, Parte: modulo.PartNumber ?? "");

            bool melhor_ = atual.Oferece < melhor.Oferece
                        || (atual.Oferece == melhor.Oferece && atual.Jedec < melhor.Jedec)
                        || (atual.Oferece == melhor.Oferece && atual.Jedec == melhor.Jedec && atual.Perfil < melhor.Perfil)
                        || (atual.Oferece == melhor.Oferece && atual.Jedec == melhor.Jedec && atual.Perfil == melhor.Perfil
                            && string.CompareOrdinal(atual.Parte, melhor.Parte) < 0);

            if (melhor_)
            {
                melhor = atual;
                escolhido = modulo;
            }
        }

        return escolhido;
    }

    /// <summary>
    /// Classifica a memória configurada. <paramref name="velocidadeConfigMts"/> é
    /// o clock configurado na BIOS, em megatransferências por segundo.
    /// </summary>
    public static ModoMemoria Classificar(int velocidadeConfigMts, SpdModulo modulo)
    {
        ArgumentNullException.ThrowIfNull(modulo);

        if (velocidadeConfigMts <= 0) return ModoMemoria.Desconhecido;

        int jedec = modulo.Jedec.VelocidadeMts;
        SpdPerfil? perfil = modulo.MelhorPerfil;

        if (perfil != null && velocidadeConfigMts == perfil.Timing.VelocidadeMts)
            return ModoMemoria.PerfilAtivo;

        if (jedec > 0 && velocidadeConfigMts == jedec)
            return ModoMemoria.PadraoJedec;

        if (perfil != null && velocidadeConfigMts > perfil.Timing.VelocidadeMts)
            return ModoMemoria.AcimaDoPerfil;

        if (jedec > 0 && velocidadeConfigMts > jedec)
            return ModoMemoria.OverclockManual;

        return ModoMemoria.Desconhecido;
    }

    /// <summary>
    /// Compara os tempos que estão ativos com os de uma referência (o perfil
    /// XMP/EXPO ou o padrão JEDEC).
    ///
    /// <para>
    /// A comparação é feita em <b>ciclos</b>, não em nanossegundos: quando a
    /// BIOS sobe o clock, ela mantém a contagem de ciclos do perfil, e é
    /// justamente mexer nessa contagem que caracteriza o ajuste manual.
    /// </para>
    /// <para>
    /// Nos tempos primários qualquer diferença conta. Nos secundários a
    /// diferença de um ciclo é ruído de revisão de BIOS (o tFAW, por exemplo,
    /// vem do padrão JEDEC e não do perfil), então só conta a partir de dois.
    /// </para>
    /// </summary>
    /// <param name="ativos">Os tempos ativos lidos do controlador.</param>
    /// <param name="referencia">Os tempos da referência.</param>
    /// <param name="nomeReferencia">Nome da referência, para o texto.</param>
    public static RelatorioTimings CompararTimings(TimingsAtivos ativos, SpdTiming referencia, string nomeReferencia)
    {
        ArgumentNullException.ThrowIfNull(ativos);
        ArgumentNullException.ThrowIfNull(referencia);

        var itens = new List<ComparacaoTiming>
        {
            Comparar("tCL", ativos.TCl, referencia.Cl, true),
            Comparar("tRCD", ativos.TRcdLeitura, referencia.Trcd, true),
            Comparar("tRP", ativos.TRp, referencia.Trp, true),
            Comparar("tRAS", ativos.TRas, referencia.Tras, true),
            Comparar("tRC", ativos.TRc, referencia.Trc, true),
            Comparar("tFAW", ativos.TFaw, Ciclos(referencia.TfawNs, referencia.VelocidadeMts), false),
            Comparar("tWR", ativos.TWr, Ciclos(referencia.TwrNs, referencia.VelocidadeMts), false),
            Comparar("tWTR (S)", ativos.TWtrs, Ciclos(referencia.TwtrSNs, referencia.VelocidadeMts), false),
            Comparar("tWTR (L)", ativos.TWtrl, Ciclos(referencia.TwtrLNs, referencia.VelocidadeMts), false)
        };

        int apertados = itens.Count(i => i.Primario && i.Situacao == SituacaoTiming.MaisAperto);

        return new RelatorioTimings
        {
            Referencia = nomeReferencia,
            Itens = itens,
            ApertadosPrimarios = apertados
        };
    }

    /// <summary>
    /// Compara os tempos ativos com a referência que faz sentido para a
    /// memória como ela está configurada.
    ///
    /// <para>
    /// A referência é o perfil (XMP/EXPO) quando a memória está rodando nele
    /// ou acima dele. Abaixo do perfil — no padrão JEDEC, que é o padrão de
    /// quem não ligou o XMP — os tempos naturalmente são <b>menores</b> que os
    /// do perfil, e comparar contra ele acusaria um "ajuste manual" que não
    /// existe.
    /// </para>
    /// </summary>
    /// <param name="ativos">Os tempos ativos lidos do controlador.</param>
    /// <param name="modulo">O módulo que manda na comparação.</param>
    /// <param name="velocidadeConfigMts">Velocidade configurada na BIOS, em MT/s.</param>
    public static RelatorioTimings CompararTimings(TimingsAtivos ativos, SpdModulo modulo, int velocidadeConfigMts)
    {
        ArgumentNullException.ThrowIfNull(modulo);

        SpdPerfil? perfil = modulo.MelhorPerfil;

        if (perfil != null && velocidadeConfigMts >= perfil.Timing.VelocidadeMts)
        {
            return CompararTimings(ativos, perfil.Timing, perfil.Nome);
        }

        return CompararTimings(ativos, modulo.Jedec, "JEDEC");
    }

    /// <summary>Compara um tempo ativo com o da referência.</summary>
    private static ComparacaoTiming Comparar(string nome, int ativo, int? referencia, bool primario)
    {
        // A referência de um tempo que o perfil não traz (o XMP 2.0 deste
        // módulo não grava tWR nem tWTR) é desconhecida — não é zero.
        if (referencia == null || referencia.Value <= 0)
        {
            return new ComparacaoTiming
            {
                Nome = nome,
                Ativo = ativo,
                Perfil = null,
                Primario = primario,
                Situacao = SituacaoTiming.SemComparacao
            };
        }

        int tolerancia = primario ? 0 : 1;
        int diferenca = ativo - referencia.Value;

        SituacaoTiming situacao = Math.Abs(diferenca) <= tolerancia
            ? SituacaoTiming.Igual
            : diferenca < 0
                ? SituacaoTiming.MaisAperto
                : SituacaoTiming.MaisSolto;

        return new ComparacaoTiming
        {
            Nome = nome,
            Ativo = ativo,
            Perfil = referencia,
            Primario = primario,
            Situacao = situacao
        };
    }

    /// <summary>Converte um tempo em nanossegundos para ciclos, no clock da referência.</summary>
    private static int Ciclos(double nanossegundos, int velocidadeMts) =>
        velocidadeMts > 0 && nanossegundos > 0
            ? (int)Math.Round(nanossegundos * velocidadeMts / 2000.0, MidpointRounding.AwayFromZero)
            : 0;

    /// <summary>
    /// Frase que explica a situação, já com os números do módulo.
    /// </summary>
    public static string Descrever(ModoMemoria modo, int velocidadeConfigMts, SpdModulo modulo, RelatorioTimings? relatorio = null)
    {
        ArgumentNullException.ThrowIfNull(modulo);

        SpdPerfil? perfil = modulo.MelhorPerfil;
        string jedec = modulo.Jedec.VelocidadeMts > 0 ? $"{modulo.Jedec.VelocidadeMts} MT/s" : "desconhecido";

        string texto = modo switch
        {
            ModoMemoria.PerfilAtivo =>
                $"Perfil {perfil!.Nome} ativo: {velocidadeConfigMts} MT/s (o que o módulo oferece).",

            ModoMemoria.PadraoJedec when perfil != null =>
                $"No padrão JEDEC ({jedec}). O módulo tem perfil {perfil.Nome} de {perfil.Timing.VelocidadeMts} MT/s, desligado na BIOS.",

            ModoMemoria.PadraoJedec =>
                $"No padrão JEDEC ({jedec}). O módulo não tem perfil de overclock.",

            ModoMemoria.OverclockManual when perfil != null =>
                $"Ajuste manual: {velocidadeConfigMts} MT/s, acima do padrão JEDEC ({jedec}) e fora do perfil {perfil.Nome} ({perfil.Timing.VelocidadeMts} MT/s).",

            ModoMemoria.OverclockManual =>
                $"Ajuste manual: {velocidadeConfigMts} MT/s, acima do padrão JEDEC ({jedec}) e sem perfil gravado.",

            ModoMemoria.AcimaDoPerfil =>
                $"Acima do perfil do módulo: {velocidadeConfigMts} MT/s (o {perfil?.Nome} oferece {perfil?.Timing.VelocidadeMts} MT/s).",

            _ => $"Velocidade configurada: {velocidadeConfigMts} MT/s."
        };

        if (relatorio == null) return texto;

        return texto + " " + DescreverTimings(relatorio, modo);
    }

    /// <summary>
    /// A segunda frase da descrição: o que os tempos ativos contam sobre o
    /// ajuste manual.
    /// </summary>
    public static string DescreverTimings(RelatorioTimings relatorio, ModoMemoria modo)
    {
        ArgumentNullException.ThrowIfNull(relatorio);

        if (relatorio.HaAjusteManual)
        {
            string apertados = string.Join(", ", relatorio.Itens
                .Where(i => i.Primario && i.Situacao == SituacaoTiming.MaisAperto)
                .Select(i => $"{i.Nome} {i.Ativo} contra {i.Perfil} do {relatorio.Referencia}"));

            return $"Os tempos ativos estão mais apertados que o {relatorio.Referencia} ({apertados}): a BIOS apertou os tempos à mão, e é a causa mais comum de travamento por memória.";
        }

        if (modo is ModoMemoria.AcimaDoPerfil or ModoMemoria.OverclockManual)
        {
            return $"Os tempos ativos não estão mais apertados que o {relatorio.Referencia}: o que está acima do perfil é só o clock.";
        }

        // Nos outros casos vale a frase do resumo: nenhum tempo primário está
        // apertado além da referência. A referência comparada é a do
        // relatório (o perfil, quando o módulo tem um), não o padrão JEDEC.
        return relatorio.Resumo;
    }

    /// <summary>
    /// Acha um achado de saúde para a memória, quando houver o que dizer.
    /// Devolve null quando a memória está exatamente como o módulo manda
    /// (perfil ativo) — aí não há problema a relatar.
    /// </summary>
    /// <param name="velocidadeConfigMts">Clock configurado na BIOS, em MT/s.</param>
    /// <param name="modulo">O módulo que manda na classificação.</param>
    /// <param name="ativos">Os tempos ativos lidos do controlador (null quando não deu para ler).</param>
    public static Achado? Avaliar(int velocidadeConfigMts, SpdModulo modulo, TimingsAtivos? ativos = null)
    {
        ArgumentNullException.ThrowIfNull(modulo);

        ModoMemoria modo = Classificar(velocidadeConfigMts, modulo);
        RelatorioTimings? relatorio = ativos == null ? null : CompararTimings(ativos, modulo, velocidadeConfigMts);

        // Tempo apertado à mão tem achado próprio: é a causa mais comum de
        // travamento por memória e não aparece em nenhum outro sinal.
        if (relatorio != null && relatorio.HaAjusteManual)
        {
            return new Achado
            {
                Severidade = Severidade.Medio,
                Area = "Memória",
                Mensagem = Descrever(modo, velocidadeConfigMts, modulo, relatorio),
                Acao = "Volte os tempos para o perfil do módulo na BIOS e teste a estabilidade antes de investigar outras causas"
            };
        }

        return modo switch
        {
            ModoMemoria.PadraoJedec when modulo.MelhorPerfil != null => new Achado
            {
                Severidade = Severidade.Informativo,
                Area = "Memória",
                Mensagem = Descrever(modo, velocidadeConfigMts, modulo, relatorio),
                Acao = $"Ative o perfil {modulo.MelhorPerfil!.Nome} na BIOS para usar {modulo.MelhorPerfil.Timing.VelocidadeMts} MT/s"
            },

            ModoMemoria.OverclockManual => new Achado
            {
                Severidade = Severidade.Baixo,
                Area = "Memória",
                Mensagem = Descrever(modo, velocidadeConfigMts, modulo, relatorio),
                Acao = "Ajuste manual não registrado no módulo: vale testar no perfil oficial antes de investigar travamentos"
            },

            ModoMemoria.AcimaDoPerfil => new Achado
            {
                Severidade = Severidade.Medio,
                Area = "Estabilidade",
                Mensagem = Descrever(modo, velocidadeConfigMts, modulo, relatorio),
                Acao = "Volte ao perfil do módulo e teste a estabilidade antes de aumentar a frequência"
            },

            _ => null
        };
    }
}
