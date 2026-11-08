namespace TranalSentinel;

/// <summary>
/// O histórico contínuo de sensores: o que o PC estava fazendo antes de
/// travar.
///
/// <para>
/// A partir da v0.28 o histórico mora num banco SQLite (<see cref="Banco"/>),
/// e não mais num CSV. O que muda para quem usa é o lugar: a pasta e o
/// arquivo não existem mais — o histórico está no banco, que consulta por
/// período sem varrer o arquivo inteiro e sabe dizer se está estragado.
///
/// <para>
/// A cadência é fixa (5 s) e não depende do perfil de recursos: o log não
/// precisa da resolução de 1 s que a tela usa, e 5 s basta para dizer o que a
/// máquina estava fazendo antes de travar.
///
/// <para>
/// A gravação nunca derruba o app: se o banco não estiver disponível, a
/// amostra se perde e o TRANAL SAÚDE avisa que o histórico não está
/// gravando.
/// </para>
/// </summary>
public static class SensorHistorico
{
    /// <summary>De quanto em quanto tempo uma amostra é gravada.</summary>
    public static TimeSpan IntervaloGravacao => TimeSpan.FromSeconds(5);

    /// <summary>
    /// Quantos dias de histórico ficam guardados. O mais antigo é apagado.
    /// </summary>
    public const int DiasRetidos = Banco.DiasRetidosAmostras;

    /// <summary>
    /// Acrescenta a amostra ao histórico. Nunca lança: o log de sensores é
    /// um extra, e perdê-lo é melhor que derrubar o app.
    /// </summary>
    public static void Gravar(AmostraSensor amostra)
    {
        try
        {
            Banco.GravarAmostra(amostra);
        }
        catch
        {
            // Banco fechado, disco cheio, arquivo em uso: o log é um extra
            // e não pode derrubar o app.
        }
    }

    /// <summary>
    /// Lê as amostras gravadas a partir de <paramref name="desde"/>.
    /// Não lança: um banco ilegível devolve lista vazia.
    /// </summary>
    public static List<AmostraSensor> Ler(DateTime desde) =>
        Ler(desde, DateTime.MaxValue);

    /// <summary>
    /// Lê as amostras gravadas entre <paramref name="desde"/> e
    /// <paramref name="ate"/>.
    ///
    /// <para>
    /// A consulta vai direto ao instante pedido: o histórico guarda 15 dias
    /// de amostras (de 5 em 5 segundos) e a leitura não precisa passar por
    /// tudo para saber o que aconteceu na janela.
    /// </para>
    ///
    /// <para>
    /// Não lança: um banco ilegível devolve lista vazia.
    /// </para>
    /// </summary>
    public static List<AmostraSensor> Ler(DateTime desde, DateTime ate)
    {
        try
        {
            return Banco.LerAmostras(desde, ate);
        }
        catch
        {
            // Banco trancado ou ilegível: sem histórico, sem drama.
            return new List<AmostraSensor>();
        }
    }

    /// <summary>
    /// Corta o que é mais antigo que <see cref="DiasRetidos"/>. A gravação
    /// já faz isso de tempos em tempos; este método é para quando o app
    /// quer arrumar na hora.
    /// </summary>
    public static void Podar()
    {
        try
        {
            Banco.ApagarAntigos(DateTime.Now.AddDays(-DiasRetidos));
        }
        catch
        {
            // Sem poda, o histórico só cresce até a próxima tentativa.
        }
    }
}
