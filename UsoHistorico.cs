namespace TranalSentinel;

/// <summary>
/// O histórico de uso: que programa esteve em frente, e quando.
///
/// <para>
/// A partir da v0.28 o histórico mora num banco SQLite (<see cref="Banco"/>),
/// e não mais num CSV por dia. O evento registrado continua sendo a TROCA
/// de programa (trocar de aba do navegador a cada segundo não interessa);
/// a duração de cada uso é a distância até a próxima troca.
///
/// <para>
/// A escrita é silenciosa por desingênio (como o log de sensores): falhar no
/// registro de uso nunca pode derrubar o app. E o amostrador usa o retorno
/// para não dar a troca por registrada quando ela não foi — senão o
/// programa anterior ficaria com o tempo dela.
///
/// <para>
/// Quem interpreta o histórico é <see cref="UsoRegras"/> (função pura): entra
/// a lista de trocas e a de amostras, sai o relatório. Esta classe só
/// guarda e lê.
/// </para>
/// </summary>
public static class UsoHistorico
{
    /// <summary>Quantos dias de histórico são mantidos. O mais antigo é apagado.</summary>
    public const int DiasRetidos = Banco.DiasRetidosUso;

    /// <summary>
    /// Acrescenta uma troca ao histórico. Nunca lança: o registro de uso é
    /// um extra, e perder uma linha é melhor que derrubar o app.
    ///
    /// <para>
    /// Devolve <c>true</c> quando a linha foi gravada. A coleta usa isso
    /// para não dar a troca por registrada quando ela não foi — senão o
    /// programa anterior ficaria com o tempo dela.
    /// </para>
    /// </summary>
    public static bool Gravar(TrocaUso troca)
    {
        try
        {
            return Banco.GravarUso(troca);
        }
        catch
        {
            // Banco trancado, sem permissão, disco cheio: o histórico de uso
            // é acessório e não pode derrubar o app.
            return false;
        }
    }

    /// <summary>
    /// Lê as trocas de um dia. Devolve lista vazia quando o dia não existe
    /// (o app nunca rodou, ou já foi podado).
    ///
    /// <para>
    /// A leitura diz se ela ficou completa: lista vazia com leitura
    /// completa é "não teve uso nesse dia"; lista vazia com leitura
    /// incompleta é "não deu para ler" — e a tela precisa saber a
    /// diferença, senão mostra um dia vazio como se o PC não tivesse sido
    /// usado.
    /// </para>
    /// </summary>
    public static Leitura<TrocaUso> Ler(DateTime dia)
    {
        try
        {
            return Banco.LerUso(dia);
        }
        catch
        {
            // Banco trancado ou ilegível: a leitura não ficou completa.
            return new Leitura<TrocaUso>(new List<TrocaUso>(), false);
        }
    }

    /// <summary>
    /// Lê as trocas de um dia <b>e a última troca do dia anterior</b>.
    ///
    /// <para>
    /// A última troca de ontem é a que atravessa a meia-noite: o programa
    /// que estava em frente às 23:50 continua em frente às 00:10. Sem ela,
    /// o dia inteiro ficaria sem dono quando o app rodou a noite toda sem
    /// trocar de programa.
    /// </para>
    /// </summary>
    public static Leitura<TrocaUso> LerComAnterior(DateTime dia)
    {
        try
        {
            return Banco.LerUsoComAnterior(dia);
        }
        catch
        {
            // Banco trancado ou ilegível: a leitura não ficou completa.
            return new Leitura<TrocaUso>(new List<TrocaUso>(), false);
        }
    }

    /// <summary>
    /// As trocas de vários dias, em ordem cronológica.
    ///
    /// <para>
    /// O primeiro dia (o mais antigo) traz também a última troca do dia
    /// anterior: é a que atravessa a meia-noite, e sem ela um período que
    /// começa de madrugada não sabe dizer quem estava em frente.
    /// </para>
    /// </summary>
    public static Leitura<TrocaUso> LerVarios(IEnumerable<DateTime> dias)
    {
        try
        {
            return Banco.LerUsoVarios(dias);
        }
        catch
        {
            // Banco trancado ou ilegível: a leitura não ficou completa.
            return new Leitura<TrocaUso>(new List<TrocaUso>(), false);
        }
    }

    /// <summary>Lista os dias que têm histórico, do mais novo para o mais antigo.</summary>
    public static List<DateTime> DiasComUso()
    {
        try
        {
            return Banco.DiasComUso();
        }
        catch
        {
            // Banco trancado ou ilegível: sem lista de dias.
            return new List<DateTime>();
        }
    }

    /// <summary>Apaga os dias mais antigos que <see cref="DiasRetidos"/>.</summary>
    public static void Podar()
    {
        try
        {
            Banco.ApagarUsosAntigos(DateTime.Now.AddDays(-DiasRetidos));
        }
        catch
        {
            // Sem poda, o histórico só cresce até a próxima tentativa.
        }
    }
}
