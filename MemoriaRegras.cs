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
    /// Frase que explica a situação, já com os números do módulo.
    /// </summary>
    public static string Descrever(ModoMemoria modo, int velocidadeConfigMts, SpdModulo modulo)
    {
        ArgumentNullException.ThrowIfNull(modulo);

        SpdPerfil? perfil = modulo.MelhorPerfil;
        string jedec = modulo.Jedec.VelocidadeMts > 0 ? $"{modulo.Jedec.VelocidadeMts} MT/s" : "desconhecido";

        return modo switch
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
    }

    /// <summary>
    /// Acha um achado de saúde para a memória, quando houver o que dizer.
    /// Devolve null quando a memória está exatamente como o módulo manda
    /// (perfil ativo) — aí não há problema a relatar.
    /// </summary>
    public static Achado? Avaliar(int velocidadeConfigMts, SpdModulo modulo)
    {
        ArgumentNullException.ThrowIfNull(modulo);

        ModoMemoria modo = Classificar(velocidadeConfigMts, modulo);

        return modo switch
        {
            ModoMemoria.PadraoJedec when modulo.MelhorPerfil != null => new Achado
            {
                Severidade = Severidade.Informativo,
                Area = "Memória",
                Mensagem = Descrever(modo, velocidadeConfigMts, modulo),
                Acao = $"Ative o perfil {modulo.MelhorPerfil!.Nome} na BIOS para usar {modulo.MelhorPerfil.Timing.VelocidadeMts} MT/s"
            },

            ModoMemoria.OverclockManual => new Achado
            {
                Severidade = Severidade.Baixo,
                Area = "Memória",
                Mensagem = Descrever(modo, velocidadeConfigMts, modulo),
                Acao = "Ajuste manual não registrado no módulo: vale testar no perfil oficial antes de investigar travamentos"
            },

            ModoMemoria.AcimaDoPerfil => new Achado
            {
                Severidade = Severidade.Medio,
                Area = "Estabilidade",
                Mensagem = Descrever(modo, velocidadeConfigMts, modulo),
                Acao = "Volte ao perfil do módulo e teste a estabilidade antes de aumentar a frequência"
            },

            _ => null
        };
    }
}
