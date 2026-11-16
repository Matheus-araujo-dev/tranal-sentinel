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
/// <param name="Familias">As famílias das teclas corrigidas (Shift, Ctrl, Alt, Win).</param>
/// <param name="DetectadaEm">Quando o app agiu (serve para o diário).</param>
/// <param name="HaQuanto">Há quanto tempo a tecla está caída.</param>
public sealed record TeclaPresa(IReadOnlyList<string> Familias, DateTime DetectadaEm, TimeSpan HaQuanto);

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
    /// Depois de corrigir uma tecla, o app espera este tempo antes de
    /// corrigir a mesma família de novo — para o aviso não virar spam quando
    /// o "soltar" não resolve de primeira.
    /// </summary>
    public static readonly TimeSpan EsperaDepoisDaCorrecao = TimeSpan.FromMinutes(5);

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
    public static string TextoDaCorrecao(IReadOnlyList<string> familias)
    {
        if (familias.Count == 0) return "Uma tecla ficou presa e foi solta";
        if (familias.Count == 1) return $"A tecla {familias[0]} ficou presa e foi solta";
        return $"As teclas {Juntar(familias)} ficaram presas e foram soltas";
    }

    /// <summary>
    /// A frase do aviso que o dono lê: o que o Windows achava, há quanto
    /// tempo, e o que o app fez.
    /// </summary>
    public static string TextoDoAviso(IReadOnlyList<string> familias, TimeSpan haQuanto)
    {
        long segundos = (long)Math.Round(haQuanto.TotalSeconds);
        string teclas = familias.Count == 1
            ? $"a tecla {familias[0]}"
            : $"as teclas {Juntar(familias)}";

        return $"O Windows continuava com {teclas} pressionada há {NumeroTexto.Inteiro(segundos)} s "
             + "sem ninguém no teclado. O TRANAL SENTINEL soltou a tecla.";
    }

    /// <summary>
    /// A frase do aviso quando o "soltar" não resolveu — o app não pode dizer
    /// que soltou o que não soltou.
    /// </summary>
    public static string TextoDoAvisoSemSucesso(IReadOnlyList<string> familias)
    {
        string teclas = familias.Count == 1
            ? $"a tecla {familias[0]}"
            : $"as teclas {Juntar(familias)}";

        return $"O Windows continua com {teclas} pressionada. O TRANAL SENTINEL tentou soltar, "
             + "mas a tecla continuou presa.";
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
