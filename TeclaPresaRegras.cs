namespace TranalSentinel;

/// <summary>
/// Uma tecla modificadora do teclado: o código que o Windows usa para ela,
/// o nome que o app mostra e a família a que pertence.
/// </summary>
/// <param name="Vk">Código virtual da tecla (VK_*).</param>
/// <param name="Nome">Nome como o app escreve para o dono.</param>
/// <param name="Familia">A família da tecla (Shift, Ctrl, Alt, Win) — é ela que vira a frase do aviso.</param>
public sealed record Modificadora(int Vk, string Nome, string Familia);

/// <summary>
/// Uma tecla que ficou presa: quais são, quando o app agiu e há quanto tempo
/// o Windows continua achando que elas estão pressionadas.
/// </summary>
/// <param name="Teclas">O nome das teclas corrigidas ("Shift direito", "Ctrl").</param>
/// <param name="Familias">As famílias das teclas corrigidas (Shift, Ctrl, Alt, Win).</param>
/// <param name="HaQuanto">Há quanto tempo o app viu a tecla caída (um piso: a tecla
/// pode estar presa desde antes do app começar a vigiar).</param>
public sealed record TeclaPresa(
    IReadOnlyList<string> Teclas,
    IReadOnlyList<string> Familias,
    TimeSpan HaQuanto)
{
    /// <summary>
    /// O dono tem que ser avisado na tela? (o teto de avisos por família).
    /// </summary>
    /// <remarks>
    /// O evento sai sempre — o diário registra o que aconteceu, mesmo
    /// quando o aviso cala. Quem decide se aparece na tela é o teto.
    /// </remarks>
    public bool Avisar { get; init; } = true;
}

/// <summary>
/// A regra da tecla presa: quando uma tecla modificadora conta como "presa".
///
/// <para>
/// <b>O problema.</b> De vez em quando o Windows continua dizendo que Shift,
/// Ctrl, Alt ou Win está pressionado — mesmo com o dedo longe do teclado. O
/// dono percebe na hora: o clique começa a valer como clique com Shift, o
/// arrastar copia em vez de mover, o navegador faz o que não foi pedido. Medido
/// no PC do dono em 16/11/2026: o <c>GetAsyncKeyState</c> e o
/// <c>GetKeyState</c> marcavam o Shift direito como pressionado, sem nenhuma
/// mudança de estado por mais de dez minutos.
/// </para>
///
/// <para>
/// <b>Como se reconhece.</b> A tecla que alguém está segurando de verdade
/// continua mandando repetição de teclado para o Windows (é o "auto-repeat" do
/// próprio Windows). A tecla presa não manda nada: o estado ficou marcado e
/// ninguém mais mexe. Por isso a regra exige as três coisas ao mesmo tempo: a
/// tecla caída há bastante tempo, o teclado em silêncio e a repetição
/// automática ligada (sem ela, segurar a tecla de verdade também produz
/// silêncio — e aí não dá para distinguir "preso" de "segurando").
/// </para>
///
/// <para>
/// A regra é pura: não fala com o Windows. Quem mede é
/// <see cref="TeclasWindows"/> e a vigia (<see cref="MonitorTeclaPresa"/>);
/// quem decide é aqui.
/// </para>
/// </summary>
public static class TeclaPresaRegras
{
    /// <summary>
    /// Quanto tempo a tecla precisa estar caída (e o teclado em silêncio)
    /// para o app considerar que ela está presa.
    ///
    /// <para>
    /// 30 s é o ponto de equilíbrio: abaixo disso ainda dá para ser alguém
    /// segurando a tecla sem querer (ou o auto-repeat ainda não chegou); muito
    /// acima, o dono já passou a sofrer com o mouse travado antes de o app agir.
    /// </para>
    /// </summary>
    public const int SegundosMinimos = 30;

    /// <summary>
    /// Quantos avisos o app dá para a mesma família antes de calar o aviso
    /// na tela.
    /// </summary>
    /// <remarks>
    /// O teto vale para o sucesso e para a falha: o que cala é o aviso, não
    /// a correção. O app continua soltando a tecla (e o diário continua
    /// registrando) — um aviso a cada 35 segundos não ajuda ninguém.
    /// </remarks>
    public const int MaximoDeAvisos = 3;

    /// <summary>
    /// Depois de quanto tempo sem aviso a janela de avisos recomeça.
    /// </summary>
    /// <remarks>
    /// A janela é o tempo em que a família pode ficar sem aviso. Ela
    /// recomeça quando a família fica este tempo sem <b>aviso</b> — e não sem
    /// tentativa: um travamento que se repete renovaria a janela a cada
    /// rodada e o dono nunca mais ouviria falar do problema.
    /// </remarks>
    public static readonly TimeSpan JanelaDeAvisos = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Depois de uma tentativa que <b>não</b> deu certo, o app espera este
    /// tempo antes de corrigir a mesma família de novo — para não ficar
    /// injetando "soltar" a cada 5 segundos enquanto a tecla continua presa.
    /// </summary>
    /// <remarks>
    /// A espera existe para não martelar a tecla, não para segurar o aviso:
    /// o teto de avisos (<see cref="MaximoDeAvisos"/>) já segura o spam. Por
    /// isso ela é curta: <b>1 minuto</b>. Com 5 minutos (o valor até a v0.33),
    /// uma tentativa que falhou deixava o dono até 5 min com o mouse preso na
    /// tecla — a mesma classe do defeito que a v0.33 veio para tirar (medido no
    /// PC do dono em 16/11/2026; apontado na revisão adversária da v0.33,
    /// achados A-4 e A-5).
    ///
    /// <para>
    /// Quando a tecla é solta de verdade, a espera não entra: um novo
    /// travamento da mesma família é tratado no tempo normal (30 s de tecla
    /// caída). Medido no PC do dono em 16/11/2026: com a espera valendo
    /// também para o sucesso, a segunda travada logo depois da primeira
    /// ficava 5 minutos sem tratamento nenhum.
    /// </remarks>
    public static readonly TimeSpan EsperaDepoisDaTentativa = TimeSpan.FromMinutes(1);

    /// <summary>
    /// As modificadoras que o Windows pode deixar presas.
    ///
    /// <para>
    /// Entram as três de cada família (a genérica e as duas laterais): o
    /// Windows marca a genérica junto com a lateral, e o "soltar" precisa
    /// chegar em todas para o estado interno largar a tecla.
    /// </para>
    /// </summary>
    public static IReadOnlyList<Modificadora> Modificadoras { get; } = Array.AsReadOnly(new[]
    {
        new Modificadora(0x10, "Shift", "Shift"),
        new Modificadora(0xA0, "Shift esquerdo", "Shift"),
        new Modificadora(0xA1, "Shift direito", "Shift"),
        new Modificadora(0x11, "Ctrl", "Ctrl"),
        new Modificadora(0xA2, "Ctrl esquerdo", "Ctrl"),
        new Modificadora(0xA3, "Ctrl direito", "Ctrl"),
        new Modificadora(0x12, "Alt", "Alt"),
        new Modificadora(0xA4, "Alt esquerdo", "Alt"),
        new Modificadora(0xA5, "Alt direito", "Alt"),
        new Modificadora(0x5B, "Win", "Win"),
        new Modificadora(0x5C, "Win direito", "Win"),
    });

    /// <summary>
    /// A regra: esta tecla está presa?
    /// </summary>
    /// <param name="pressionada">O Windows diz que a tecla está pressionada?</param>
    /// <param name="repeticaoDoTecladoLigada">A repetição automática do Windows está ligada?</param>
    /// <param name="atividadeConhecida">Dá para saber se o teclado mandou evento?</param>
    /// <param name="haQuantoEstaCaiada">Há quanto tempo a tecla está caída.</param>
    /// <param name="semAtividadeDeTeclado">Há quanto tempo o teclado não manda evento nenhum.</param>
    /// <returns>true quando a tecla está presa de verdade.</returns>
    public static bool EstaPresa(
        bool pressionada,
        bool repeticaoDoTecladoLigada,
        bool atividadeConhecida,
        TimeSpan haQuantoEstaCaiada,
        TimeSpan semAtividadeDeTeclado)
    {
        // Não está pressionada: não há nada a corrigir.
        if (!pressionada) return false;

        // Sem o gancho do teclado não se sabe se o silêncio é de verdade:
        // sem essa prova, o app se cala (tecla presa é chato; soltar a tecla
        // que o dono está segurando é pior).
        if (!atividadeConhecida) return false;

        // Sem repetição automática, segurar a tecla de verdade também não
        // gera evento nenhum — silêncio não distingue "preso" de "segurando".
        if (!repeticaoDoTecladoLigada) return false;

        if (haQuantoEstaCaiada < TimeSpan.FromSeconds(SegundosMinimos)) return false;
        if (semAtividadeDeTeclado < TimeSpan.FromSeconds(SegundosMinimos)) return false;

        return true;
    }

    /// <summary>
    /// A repetição automática do teclado está ligada?
    /// </summary>
    /// <param name="velocidade">
    /// A velocidade da repetição que o Windows guardou (0 = desligada).
    /// Null = o Windows não guardou o valor, e vale o padrão dele, que é a
    /// repetição ligada.
    /// </param>
    /// <remarks>
    /// A decisão mora aqui (e não no Windows) porque é ela que decide se o
    /// app pode agir. Medido no PC do dono em 16/11/2026: o valor da
    /// repetição não existe no perfil do usuário — ele está no perfil
    /// padrão do Windows, e o padrão é a repetição ligada. Quem tratasse
    /// "sem valor" como "desligada" desligaria a vigia inteira sem o dono
    /// pedir.
    /// </remarks>
    public static bool RepeticaoDoTecladoLigada(int? velocidade) =>
        !velocidade.HasValue || velocidade.Value != 0;

    /// <summary>
    /// As famílias de um conjunto de teclas, sem repetição ("Shift", "Ctrl").
    /// </summary>
    public static IReadOnlyList<string> ListaDeFamilias(IEnumerable<Modificadora> modificadoras)
    {
        var familias = new List<string>();
        foreach (var m in modificadoras)
        {
            if (familias.Contains(m.Familia)) continue;
            familias.Add(m.Familia);
        }
        return familias;
    }

    /// <summary>
    /// Todos os códigos de tecla de uma família (Shift = genérica, esquerda e
    /// direita). O "soltar" tem que chegar em todos: o Windows marca a
    /// genérica junto com a lateral.
    /// </summary>
    public static IReadOnlyList<int> VksDaFamilia(string familia) => Modificadoras
        .Where(m => m.Familia == familia)
        .Select(m => m.Vk)
        .ToList();

    /// <summary>
    /// Junta as famílias de um conjunto de teclas em uma frase ("Shift",
    /// "Shift e Ctrl").
    /// </summary>
    public static string Families(IEnumerable<Modificadora> modificadoras)
    {
        var familias = ListaDeFamilias(modificadoras);
        return Juntar(familias);
    }

    /// <summary>
    /// Junta uma lista de famílias em uma frase ("Shift", "Shift e Ctrl").
    /// </summary>
    public static string Juntar(IReadOnlyList<string> familias)
    {
        if (familias.Count == 0) return "tecla";
        if (familias.Count == 1) return familias[0];
        return string.Join(", ", familias.Take(familias.Count - 1)) + " e " + familias[familias.Count - 1];
    }

    /// <summary>
    /// A frase curta do que aconteceu (a do diário e do cartão). O verbo
    /// concorda com o número de teclas.
    /// </summary>
    /// <param name="teclas">
    /// O nome das teclas ("Shift direito"). O nome da tecla é o que o dono
    /// reconhece: no caso medido no PC dele, a tecla que grudou foi a
    /// **direita** — e "Shift" sozinho não diz qual foi.
    /// </param>
    public static string TextoDaCorrecao(IReadOnlyList<string> teclas)
    {
        if (teclas.Count == 0) return "Uma tecla ficou presa e foi solta";
        if (teclas.Count == 1) return $"A tecla {teclas[0]} ficou presa e foi solta";
        return $"As teclas {Juntar(teclas)} ficaram presas e foram soltas";
    }

    /// <summary>
    /// A frase do diário quando o "soltar" não resolveu.
    /// </summary>
    public static string TextoDaTentativa(IReadOnlyList<string> teclas)
    {
        if (teclas.Count == 0) return "Tentativa de soltar uma tecla que continuou presa";
        if (teclas.Count == 1) return $"Tentativa de soltar a tecla {teclas[0]}, que continuou presa";
        return $"Tentativa de soltar as teclas {Juntar(teclas)}, que continuaram presas";
    }

    /// <summary>
    /// Os segundos do aviso — o mesmo número no aviso e no diário (o mesmo
    /// evento não pode sair com dois números).
    /// </summary>
    /// <remarks>
    /// É o **piso** do tempo, e por isso arredonda para baixo: o texto diz
    /// "pelo menos N s", e "pelo menos 31 s" seria falso para uma tecla
    /// caída há 30,6 s. Com 30,6 s o número é 30 — e "pelo menos 30 s" é
    /// verdade.
    /// </remarks>
    public static long SegundosDoAviso(TimeSpan haQuanto) =>
        (long)Math.Floor(haQuanto.TotalSeconds);

    /// <summary>
    /// A frase do aviso que o dono lê: o que o Windows achava, há quanto
    /// tempo, e o que o app fez.
    /// </summary>
    /// <param name="teclas">O nome das teclas ("Shift direito").</param>
    /// <param name="haQuanto">
    /// Há quanto tempo o app viu a tecla caída. O texto diz "pelo menos":
    /// o app começa a vigiar depois que a tecla ficou presa, e o tempo real
    /// pode ser maior.
    /// </param>
    public static string TextoDoAviso(IReadOnlyList<string> teclas, TimeSpan haQuanto)
    {
        long segundos = SegundosDoAviso(haQuanto);
        bool uma = teclas.Count == 1;
        string quem = NomesDasTeclas(teclas);
        string estado = uma ? "pressionada" : "pressionadas";
        string acao = uma ? "soltou a tecla" : "soltou as teclas";
        return $"O Windows continuava com {quem} {estado} há pelo menos "
             + $"{NumeroTexto.Inteiro(segundos)} s, sem ninguém no teclado. "
             + $"O TRANAL SENTINEL {acao}.";
    }

    /// <summary>
    /// A tecla é a genérica da família (Shift, Ctrl, Alt)?
    /// </summary>
    /// <remarks>
    /// O Windows marca a genérica junto com a lateral: injetar o Shift
    /// direito deixa os dois (0x10 e 0xA1) marcados. É por isso que o
    /// "soltar" precisa chegar nas duas.
    /// </remarks>
    public static bool EhGenerica(int vk) => vk == 0x10 || vk == 0x11 || vk == 0x12;

    /// <summary>
    /// Os nomes das teclas que o dono reconhece, sem a genérica repetida.
    /// </summary>
    /// <remarks>
    /// Com o Shift direito marcado, o Windows marca também o Shift
    /// genérico. Nomear as dois faria o aviso dizer "as teclas Shift e
    /// Shift direito" — e "Shift direito" sozinho já diz tudo.
    /// </remarks>
    public static IReadOnlyList<string> NomesObservados(IEnumerable<Modificadora> modificadoras)
    {
        var lista = modificadoras.ToList();
        var nomes = new List<string>();

        foreach (var modificadora in lista)
        {
            // A genérica só entra quando não há lateral da mesma família.
            if (EhGenerica(modificadora.Vk)
                && lista.Any(outro => outro.Familia == modificadora.Familia && !EhGenerica(outro.Vk)))
            {
                continue;
            }

            if (!nomes.Contains(modificadora.Nome)) nomes.Add(modificadora.Nome);
        }

        return nomes;
    }

    /// <summary>
    /// Os nomes das teclas, como o dono lê: "a tecla Shift direito" ou
    /// "as teclas Shift direito e Ctrl".
    /// </summary>
    public static string NomesDasTeclas(IReadOnlyList<string> teclas)
    {
        if (teclas.Count == 0) return "a tecla";
        if (teclas.Count == 1) return $"a tecla {teclas[0]}";
        return $"as teclas {Juntar(teclas)}";
    }

    /// <summary>
    /// A frase do aviso quando o "soltar" não resolveu — o app não pode dizer
    /// que soltou o que não soltou.
    /// </summary>
    public static string TextoDoAvisoSemSucesso(IReadOnlyList<string> teclas)
    {
        bool uma = teclas.Count == 1;
        string quem = NomesDasTeclas(teclas);
        string estado = uma ? "pressionada" : "pressionadas";
        string ficou = uma ? "a tecla continuou presa" : "as teclas continuaram presas";
        return $"O Windows continua com {quem} {estado}. "
             + $"O TRANAL SENTINEL tentou soltar, mas {ficou}.";
    }

    /// <summary>
    /// A explicação do que costuma ter por trás de uma tecla presa — usada
    /// na tela do TRANAL SAÚDE, onde o dono pode agir.
    /// </summary>
    public static string ExplicacaoDaCausa =>
        "A tecla presa quase sempre vem de uma transição de sessão do Windows "
        + "(o PC dorme ou acorda por causa de um dispositivo USB) ou de um "
        + "receptor sem fio que perde o comando de soltar a tecla. Se repetir, "
        + "tire o receptor USB da tomada e recoloque, e reinstale o driver do "
        + "teclado ou do mouse nas Configurações de dispositivos.";
}
