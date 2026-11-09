using System.Globalization;

namespace TranalSentinel;

/// <summary>Um aviso que o app dá em segundo plano.</summary>
public sealed class Alerta
{
    /// <summary>Identificador estável (o mesmo alerta não repete).</summary>
    public string Id { get; init; } = "";

    public string Titulo { get; init; } = "";
    public string Texto { get; init; } = "";
    public Severidade Severidade { get; init; } = Severidade.Medio;

    /// <summary>
    /// Desde quando a condição está valendo (o começo do episódio).
    ///
    /// <para>
    /// É o instante que o "por que disparou" usa para olhar o histórico: a
    /// janela que vem antes dele é o que levou o sensor ao limite. Sem este
    /// campo, a tela só poderia explicar o momento atual — que é outra coisa,
    /// e a explicação mentiria.
    /// </para>
    /// </summary>
    public DateTime Desde { get; init; }
}

/// <summary>
/// Os avisos em segundo plano: o usuário define a regra (o que vigiar, o
/// limite e quanto tempo a condição precisa valer) e o app avalia sozinho.
///
/// <para>
/// A regra fica em um lugar só (<see cref="RegraAlerta"/>): o motor lê o
/// valor do sensor, compara com o limite e avisa uma vez por episódio. O
/// mesmo alerta não repete a cada ciclo, e some da lista quando a condição
/// passa — então o usuário não é martelado.
/// </para>
///
/// <para>
/// Quando o usuário muda o que a regra vigia (o sensor, a operação ou o
/// limite), a regra passa a vigiar outra coisa: o episódio antigo é
/// encerrado, o aviso antigo sai da tela e o novo sai do jeito certo.
/// </para>
/// </summary>
public sealed class MonitorAlertas
{
    /// <summary>
    /// A chave da regra que vigia a bateria. Ela não está no catálogo: o
    /// valor só vale quando o PC está fora da tomada.
    /// </summary>
    public const string SensorBateria = "bateria";

    /// <summary>
    /// O prefixo da chave de regra que vigia uma ventoinha.
    ///
    /// <para>
    /// A ventoinha não está no catálogo: o catálogo é fixo (são oito
    /// sensores de hardware), e as ventoinhas dependem da máquina. A chave
    /// da regra carrega a identidade da ventoinha depois do prefixo.
    /// </para>
    /// </summary>
    public const string PrefixoVentoinha = "ventoinha:";

    /// <summary>A unidade do valor de uma ventoinha (rotações por minuto).</summary>
    public const string UnidadeVentoinha = "RPM";

    /// <summary>A dica de uma regra de ventoinha.</summary>
    public const string DicaVentoinha =
        "A ventoinha parou de girar. A aba TRANAL VENTOINHAS compara o RPM com o recorde da própria ventoinha.";

    /// <summary>
    /// A ventoinha de uma chave de regra (null quando a chave não é de
    /// ventoinha, ou quando a ventoinha não está no contexto agora).
    /// </summary>
    private static ContextoVentoinha? VentoinhaDoContexto(string sensor, ContextoSensores contexto)
    {
        if (!sensor.StartsWith(PrefixoVentoinha, StringComparison.Ordinal)) return null;

        string id = sensor[PrefixoVentoinha.Length..];
        return contexto.Ventoinhas.FirstOrDefault(v => v.Id == id);
    }

    /// <summary>
    /// O rótulo, a unidade e a dica de uma regra de alerta.
    ///
    /// <para>
    /// A chave da regra de ventoinha é uma identificação interna
    /// ("ventoinha:hardware|nome"): ela existe para amarrar a regra à
    /// ventoinha, e não é texto para o usuário ler. O nome vem da máquina (o
    /// contexto) e a unidade é RPM.
    /// </para>
    /// </summary>
    /// <param name="chave">A chave da regra.</param>
    /// <param name="ventoinhas">As ventoinhas que o app está vendo agora.</param>
    public static (string Rotulo, string Unidade, string Dica) ResolverSensor(
        string chave, IReadOnlyList<ContextoVentoinha> ventoinhas)
    {
        if (chave == SensorBateria)
            return (BateriaRotulo, BateriaUnidade, BateriaDica);

        if (chave.StartsWith(PrefixoVentoinha, StringComparison.Ordinal))
        {
            string id = chave[PrefixoVentoinha.Length..];

            // O nome vem do contexto (o hardware da máquina). Sem ele, o
            // nome do sensor ainda serve ("Fan #1") — o id cru não.
            string nome = ventoinhas.FirstOrDefault(v => v.Id == id)?.Rotulo
                          ?? VentoinhaRegras.NomeLegivel(id);

            return (nome, UnidadeVentoinha, DicaVentoinha);
        }

        var item = SensorCatalogo.PorChave(chave);
        return (item?.Rotulo ?? chave, item?.Unidade ?? "", item?.Dica ?? "");
    }

    /// <summary>
    /// A frase que diz que a regra não está vigiando nada (null quando ela
    /// está vigindo).
    ///
    /// <para>
    /// Uma regra de ventoinha aponta para a identidade de uma ventoinha
    /// ("ventoinha:hardware|nome"). Se essa ventoinha não existe mais na
    /// máquina — outro driver, outro nome de sensor, o arquivo de
    /// configuração copiado para outro PC — a regra cala em silêncio e o
    /// usuário fica achando que está protegido. O app precisa dizer que
    /// ela não está vigiando nada.
    ///
    /// <para>
    /// São duas frases diferentes porque são dois problemas diferentes: a
    /// ventoinha <b>não está nesta máquina</b> (o id não bate com nada) e
    /// a ventoinha <b>não está dando leitura</b> (o sensor parou de
    /// responder agora).
    /// </para>
    /// </summary>
    /// <param name="chave">A chave da regra.</param>
    /// <param name="ventoinhas">As ventoinhas que o app conhece agora.</param>
    /// <returns>A frase do problema, ou null quando a regra está vigiando.</returns>
    public static string? RegraInativa(string chave, IReadOnlyList<ContextoVentoinha> ventoinhas)
    {
        if (!chave.StartsWith(PrefixoVentoinha, StringComparison.Ordinal)) return null;

        string id = chave[PrefixoVentoinha.Length..];

        var ventoinha = ventoinhas.FirstOrDefault(v => v.Id == id);

        if (ventoinha is not null)
        {
            if (ventoinha.Rpm is not null) return null;

            return $"A ventoinha {ventoinha.Rotulo} não está dando leitura agora — esta regra não está vigiando nada.";
        }

        // O id cru não pode aparecer na tela: o nome do sensor serve.
        return $"A ventoinha {VentoinhaRegras.NomeLegivel(id)} não está nesta máquina — esta regra não está vigiando nada.";
    }

    /// <summary>O nome da bateria nos avisos (a chave não está no catálogo).</summary>
    public const string BateriaRotulo = "Carga da bateria";

    /// <summary>A unidade da bateria nos avisos (%).</summary>
    public const string BateriaUnidade = "%";

    /// <summary>A dica da bateria nos avisos.</summary>
    public const string BateriaDica = "Ligue o carregador ou reduza o que estiver consumindo.";

    /// <summary>
    /// A maior temperatura entre os discos (null se nenhum disco reler
    /// temperatura). Só o que é <see cref="LibreHardwareMonitor.Hardware.HardwareType.Storage"/>
    /// conta — sensor de GPU ou placa-mãe não é disco.
    /// </summary>
    public static float? TemperaturaMaximaDisco(IEnumerable<SensorRow> linhas)
    {
        float? maxima = null;
        foreach (var linha in linhas)
        {
            if (linha.HardwareType != LibreHardwareMonitor.Hardware.HardwareType.Storage) continue;
            if (linha.Type != LibreHardwareMonitor.Hardware.SensorType.Temperature) continue;
            if (linha.RawValue is float v && (maxima is null || v > maxima)) maxima = v;
        }
        return maxima;
    }

    private readonly Dictionary<string, Alerta> _ativos = new();

    /// <summary>Desde quando cada condição está valendo (para o tempo mínimo).</summary>
    private readonly Dictionary<string, DateTime> _inicios = new();

    /// <summary>
    /// O que cada regra está vigiando (sensor, operação e limite). Quando
    /// isso muda, a regra vigia outra coisa e o episódio anterior vale.
    /// </summary>
    private readonly Dictionary<string, string> _assinaturas = new();

    /// <summary>A vigia roda em outra thread e a tela lê a lista de ativos.</summary>
    private readonly object _trava = new();

    /// <summary>Disparado quando um alerta começa (uma vez por episódio).</summary>
    public event Action<Alerta>? Alertar;

    /// <summary>Os alertas que estão valendo agora.</summary>
    public IReadOnlyList<Alerta> Ativos
    {
        get { lock (_trava) return _ativos.Values.ToList(); }
    }

    /// <summary>
    /// Avalia as regras e avisa o que é novo. Devolve os alertas que
    /// acabaram de aparecer (o que já estava valendo não repete).
    /// </summary>
    /// <param name="agora">O instante da avaliação (a base do tempo mínimo).</param>
    /// <param name="regras">As regras configuradas pelo usuário.</param>
    /// <param name="contexto">Os valores lidos dos sensores neste ciclo.</param>
    public IReadOnlyList<Alerta> Verificar(DateTime agora, IReadOnlyList<RegraAlerta> regras, ContextoSensores contexto)
    {
        var novos = new List<Alerta>();

        lock (_trava)
        {
            var emCondicao = new HashSet<string>();
            var chavesAtuais = new HashSet<string>();

            foreach (var regra in regras)
            {
                // A chave que identifica a regra. Uma regra sem Id (um
                // arquivo de configuração editado à mão) ganha uma chave
                // pelo que ela vigia, para não colidir com outra.
                string chave = string.IsNullOrWhiteSpace(regra.Id) ? "~" + Assinatura(regra) : regra.Id;
                chavesAtuais.Add(chave);

                // O usuário mudou o que a regra vigia: o episódio anterior
                // valia para outra coisa. O aviso antigo sai da tela e a
                // contagem do tempo mínimo recomeça.
                string assinatura = Assinatura(regra);
                if (_assinaturas.TryGetValue(chave, out string? anterior) && anterior != assinatura)
                {
                    _ativos.Remove(chave);
                    _inicios.Remove(chave);
                }
                _assinaturas[chave] = assinatura;

                // Regra desligada, operação inválida ou sensor inexistente: a
                // regra não é avaliada e o registro do tempo mínimo é apagado
                // (assim ela recomeça do zero quando voltar a valer).
                if (!regra.Ativa) { _inicios.Remove(chave); continue; }
                if (regra.Operacao != ">=" && regra.Operacao != "<=") { _inicios.Remove(chave); continue; }

                float? valor = LerValor(regra.Sensor, contexto, out bool valeAgora);
                if (valor is null) { _inicios.Remove(chave); continue; }      // sensor ausente nesta máquina
                if (!valeAgora) { _inicios.Remove(chave); continue; }          // bateria carregando não avisa

                bool condicao = regra.Operacao == ">=" ? valor >= regra.Limite : valor <= regra.Limite;
                if (!condicao) { _inicios.Remove(chave); continue; }

                emCondicao.Add(chave);

                // Tempo mínimo: a condição precisa valer há tanto tempo antes
                // de o aviso sair.
                if (!_inicios.TryGetValue(chave, out DateTime inicio))
                {
                    inicio = agora;
                    _inicios[chave] = inicio;
                }
                if ((agora - inicio).TotalSeconds < regra.SegundosMinimos) continue;

                if (_ativos.TryGetValue(chave, out var ativo))
                {
                    // O aviso continua valendo, mas o texto acompanha a
                    // regra e o valor de agora (o usuário não pode ver um
                    // limite velho na tela). Isso não avisa de novo.
                    var atualizado = MontarAlerta(chave, regra, valor.Value, inicio, contexto);
                    if (atualizado.Texto != ativo.Texto || atualizado.Titulo != ativo.Titulo
                        || atualizado.Severidade != ativo.Severidade)
                        _ativos[chave] = atualizado;
                    continue;
                }

                var alerta = MontarAlerta(chave, regra, valor.Value, inicio, contexto);
                _ativos[chave] = alerta;
                novos.Add(alerta);
            }

            // O que não está mais valendo sai da lista (pode avisar de novo depois).
            foreach (var id in _ativos.Keys.ToList())
                if (!emCondicao.Contains(id))
                    _ativos.Remove(id);

            // Regra que o usuário apagou não pode deixar estado para trás.
            foreach (var id in _inicios.Keys.ToList())
                if (!chavesAtuais.Contains(id))
                    _inicios.Remove(id);
            foreach (var id in _assinaturas.Keys.ToList())
                if (!chavesAtuais.Contains(id))
                    _assinaturas.Remove(id);
        }

        // O aviso sai fora da trava: quem recebe pode chamar a tela (e a
        // tela lê a lista de ativos).
        foreach (var alerta in novos)
        {
            try { Alertar?.Invoke(alerta); }
            catch { /* um aviso que falha não pode derrubar a vigilância */ }
        }

        return novos;
    }

    /// <summary>
    /// O que a regra vigia (sensor, operação e limite). Quando isso muda, a
    /// regra passa a vigiar outra coisa.
    /// </summary>
    private static string Assinatura(RegraAlerta regra) =>
        $"{regra.Sensor}|{regra.Operacao}|{regra.Limite.ToString("R", CultureInfo.InvariantCulture)}";

    /// <summary>
    /// Lê o valor do sensor da regra. A bateria tem um caminho próprio: o
    /// valor só vale quando o PC está fora da tomada.
    /// </summary>
    private static float? LerValor(string sensor, ContextoSensores contexto, out bool vale)
    {
        if (sensor == SensorBateria)
        {
            vale = !contexto.Carregando;
            return contexto.Bateria is int b ? b : null;
        }

        // A ventoinha: o valor é o RPM de agora. Uma ventoinha que não está
        // no contexto não tem valor (o sensor sumiu) — e a regra fica sem
        // avaliar, como acontece com qualquer sensor ausente.
        var ventoinha = VentoinhaDoContexto(sensor, contexto);
        if (ventoinha is not null)
        {
            // A ventoinha que parou de dar leitura continua na lista, mas
            // sem RPM: não há valor para comparar, e a regra não pode
            // avisar com o número de antes.
            vale = ventoinha.Rpm is not null;
            return ventoinha.Rpm;
        }

        var item = SensorCatalogo.PorChave(sensor);
        if (item is null) { vale = false; return null; }

        vale = true;
        return item.Ler(contexto);
    }

    /// <summary>
    /// Monta o aviso da regra (o texto sai do catálogo do sensor).
    ///
    /// <param name="desde">Desde quando a condição está valendo (o começo do episódio).</param>
    /// </summary>
    private static Alerta MontarAlerta(string chave, RegraAlerta regra, float valor, DateTime desde, ContextoSensores contexto)
    {
        // O rótulo, a unidade e a dica saem do mesmo lugar para o aviso e
        // para a descrição da regra na tela: dois textos para o mesmo sensor
        // é o jeito mais rápido de o app se contradizer.
        var (rotuloCatalogo, unidade, dica) = ResolverSensor(regra.Sensor, contexto.Ventoinhas);

        string titulo = string.IsNullOrWhiteSpace(regra.Rotulo) ? rotuloCatalogo : regra.Rotulo!;

        // O número sai pelo formatador da casa (vírgula decimal, independente
        // da cultura do Windows): a interpolação crua usaria a cultura do
        // usuário, e o mesmo evento sairia diferente em PCs diferentes.
        string texto = $"{rotuloCatalogo}: {NumeroTexto.Numero(valor)} {unidade} " +
                       $"(limite {regra.Operacao} {NumeroTexto.Numero(regra.Limite)} {unidade}). {dica}";

        return new Alerta
        {
            Id = "regra:" + chave,
            Titulo = titulo,
            Texto = texto,
            Severidade = regra.Severidade,
            Desde = desde,
        };
    }
}
