namespace TranalSentinel;

/// <summary>
/// Decodifica os registradores do UMC do AMD em tempos de memória ativos.
///
/// <para>
/// Esta é a parte <b>pura</b> da feature: ela recebe os valores brutos dos
/// registradores e devolve os tempos, ou null quando os valores não fazem
/// sentido. Não toca em driver, nem em disco, nem em hardware — é o que os
/// testes cobrem.
/// </para>
/// <para>
/// O mapa dos registradores vem de duas referências abertas: o
/// <c>ryzen_smu</c> (que imprime os timings do DRAM) e o <c>zencli.c</c>
/// (do projeto MemTimings-Linux), que descrevem o bloco 0x50200 do UMC das
/// famílias 17h em diante.
/// </para>
/// </remarks>
public static class TimingsAtivosDecoder
{
    /// <summary>Bloco de registradores do canal 0 do UMC.</summary>
    public const uint BlocoCanal0 = 0x50200;

    /// <summary>Bloco de registradores do canal 1 do UMC (+0x100000).</summary>
    public const uint BlocoCanal1 = 0x150200;

    /// <summary>
    /// Valor que o registrador de clock assume quando o canal 0 está vazio:
    /// nesse caso o bloco de verdade é o do canal 1 (regra do ryzen_smu).
    /// </summary>
    public const uint MarcadorCanalVazio = 0x300;

    /// <summary>Endereço do tipo de memória (relativo ao bloco: -0x100).</summary>
    public const uint DesvioTipoMemoria = 0x100;

    /// <summary>Desvio do registrador de troca de banco (relativo ao bloco).</summary>
    public const uint DesvioBankGroupSwap0 = 0x1B0;

    /// <summary>Desvio do segundo registrador de troca de banco.</summary>
    public const uint DesvioBankGroupSwap1 = 0x1A8;

    /// <summary>Valor que indica "banco/grupo de bancos sem troca".</summary>
    public const uint MarcadorBankGroupSwap = 0x87654321;

    /// <summary>Desvios, relativos ao bloco, dos registradores que interessam.</summary>
    public static readonly uint[] Desvios =
    {
        0x00, // clock, taxa de comando, gear down
        0x04, // tCL, tRAS, tRCD leitura, tRCD escrita
        0x08, // tRC, tRP
        0x0C, // tRRDS, tRRDL, tRTP
        0x10, // tFAW
        0x14, // tCWL, tWTRS, tWTRL
        0x18, // tWR
        0x30, // tREFI
        0x50, // tSTAG
        0x54, // tCKE
    };

    /// <summary>
    /// Decodifica os tempos ativos.
    ///
    /// <para>
    /// <paramref name="velocidadeEsperadaMts"/> é a velocidade que o Windows
    /// informa como configurada. Ela serve de conferência: se o clock lido
    /// no controlador não bater com ela, os registradores não são de memória
    /// e o app prefere não mostrar nada a mostrar número errado.
    /// </para>
    /// </summary>
    /// <param name="registros">Os registradores lidos.</param>
    /// <param name="velocidadeEsperadaMts">Velocidade configurada, em MT/s (0 = não conferida).</param>
    /// <param name="erro">O motivo da recusa, quando não deu para decodificar.</param>
    public static TimingsAtivos? Decodificar(RegistrosSmn registros, int velocidadeEsperadaMts, out string? erro)
    {
        ArgumentNullException.ThrowIfNull(registros);

        // O tipo de memória é do processador, não do canal: lê uma vez, do
        // primeiro canal que responder. Por isso a recusa por tipo não tem
        // "tentar o outro canal" — DDR5 é DDR5 nos dois canais.
        TipoMemoriaDdr tipo = Tipo(registros.Valor(BlocoCanal0 - DesvioTipoMemoria));
        if (tipo == TipoMemoriaDdr.Desconhecido)
        {
            tipo = Tipo(registros.Valor(BlocoCanal1 - DesvioTipoMemoria));
        }

        if (tipo != TipoMemoriaDdr.Ddr4)
        {
            erro = tipo switch
            {
                TipoMemoriaDdr.Ddr5 =>
                    "A memória é DDR5: o mapa dos tempos ativos ainda não foi conferido para DDR5.",

                TipoMemoriaDdr.Desconhecido =>
                    "O controlador de memória não informa o tipo de memória.",

                _ => "A memória não é DDR4: o mapa dos tempos ativos não se aplica."
            };

            return null;
        }

        // Regra do ryzen_smu: quando o primeiro registrador do canal 0 vale
        // 0x300, o canal 0 está vazio e o bloco de verdade é o do canal 1.
        // Quando o canal 0 não responde, o do canal 1 é a outra hipótese.
        uint baseBloco = BlocoCanal0;
        if (!registros.Valido(BlocoCanal0) || registros.Valor(BlocoCanal0) == MarcadorCanalVazio)
        {
            baseBloco = BlocoCanal1;
        }

        TimingsAtivos? timings = DecodificarBloco(registros, baseBloco, tipo, velocidadeEsperadaMts, out string? erroBloco);
        if (timings != null)
        {
            erro = null;
            return timings;
        }

        // O canal escolhido não deu resultado fazível: tenta o outro.
        uint outro = baseBloco == BlocoCanal0 ? BlocoCanal1 : BlocoCanal0;
        timings = DecodificarBloco(registros, outro, tipo, velocidadeEsperadaMts, out string? erroOutro);
        if (timings != null)
        {
            erro = null;
            return timings;
        }

        // Os dois canais falharam: o motivo do canal que a regra escolheu é o
        // que vale; o do outro entra como contexto, porque as causas podem
        // ser diferentes.
        erro = erroOutro == erroBloco ? erroBloco : $"{erroBloco} (o outro canal também falhou: {erroOutro})";
        return null;
    }

    /// <summary>Decodifica um bloco específico (usado no teste, com base fixa).</summary>
    private static TimingsAtivos? DecodificarBloco(RegistrosSmn registros, uint baseBloco, TipoMemoriaDdr tipo, int velocidadeEsperadaMts, out string? erro)
    {
        erro = null;

        // ---- todos os registradores precisam ter vindo com dado --------------
        foreach (uint desvio in Desvios)
        {
            if (registros.Valido(baseBloco + desvio)) continue;

            erro = "Faltou um registrador de tempo do controlador de memória.";
            return null;
        }

        // ---- clock -----------------------------------------------------------
        uint rel0 = registros.Valor(baseBloco + 0x00);
        int clockMhz = (int)Math.Round((rel0 & 0x7F) * 100.0 / 3.0);
        if (clockMhz < 200 || clockMhz > 4400)
        {
            erro = "O clock de memória lido no controlador não é plausível.";
            return null;
        }

        int velocidadeMts = clockMhz * 2;

        // A conferência com o que o Windows informa é o que separa "os
        // registradores são mesmo de memória" de "li o endereço errado".
        if (velocidadeEsperadaMts > 0)
        {
            double desvio = Math.Abs(velocidadeMts - velocidadeEsperadaMts) / (double)velocidadeEsperadaMts;
            if (desvio > 0.02)
            {
                erro = $"O clock lido no controlador ({velocidadeMts} MT/s) não bate com o configurado ({velocidadeEsperadaMts} MT/s).";
                return null;
            }
        }

        // ---- tempos ----------------------------------------------------------
        uint rel1 = registros.Valor(baseBloco + 0x04);
        uint rel2 = registros.Valor(baseBloco + 0x08);
        uint rel3 = registros.Valor(baseBloco + 0x0C);
        uint rel4 = registros.Valor(baseBloco + 0x10);
        uint rel5 = registros.Valor(baseBloco + 0x14);
        uint rel6 = registros.Valor(baseBloco + 0x18);
        uint rel7 = registros.Valor(baseBloco + 0x30);
        uint rel8 = registros.Valor(baseBloco + 0x50);
        uint rel9 = registros.Valor(baseBloco + 0x54);

        int tCl = (int)(rel1 & 0xFF);
        int tRas = (int)((rel1 >> 8) & 0xFF);
        int tRcdLeitura = (int)((rel1 >> 16) & 0x3F);
        int tRcdEscrita = (int)((rel1 >> 24) & 0x3F);

        int tRc = (int)(rel2 & 0xFF);
        int tRp = (int)((rel2 >> 16) & 0x3F);

        int tRrds = (int)(rel3 & 0x1F);
        int tRrdl = (int)((rel3 >> 8) & 0x1F);
        int tRtp = (int)((rel3 >> 24) & 0x1F);

        int tFaw = (int)(rel4 & 0xFF);
        int tCwl = (int)(rel5 & 0x3F);
        int tWtrs = (int)((rel5 >> 8) & 0x1F);
        int tWtrl = (int)((rel5 >> 16) & 0x3F);

        int tWr = (int)(rel6 & 0xFF);
        int tRefi = (int)(rel7 & 0xFFFF);
        int tStag = (int)((rel8 >> 16) & 0x3FF);
        int tCke = (int)((rel9 >> 24) & 0x1F);

        // ---- sanidade dos números -------------------------------------------
        // Os tetos abaixo são os de DDR4 plausível. Onde a máscara do campo é
        // maior que o teto (tCL, tRAS, tRC, tFAW, tWR são de 8 bits), o teto
        // é a rede de segurança contra registro lido no endereço errado; nos
        // campos de 5 e 6 bits a própria máscara já é o limite.
        //
        // Tempo zerado também é rejeitado: um tempo zerado entraria na
        // comparação com o perfil como "mais apertado", e o app acusaria um
        // ajuste manual que não existe.
        if (tCl <= 0 || tCl > 63 ||
            tRas < tCl || tRas > 127 ||
            tRcdLeitura < tCl || tRcdEscrita < tCl ||
            tRp < tCl ||
            tRc < tRas || tRc > 127 ||
            tFaw <= 0 || tFaw > 127 ||
            tCwl <= 0 ||
            tWtrl <= 0 ||
            tWr <= 0 || tWr > 127 ||
            tRefi <= 0)
        {
            erro = "Os tempos lidos no controlador não são coerentes entre si.";
            return null;
        }

        double cicloNs = 1000.0 / clockMhz;
        double tRefiNs = tRefi * cicloNs;

        // O intervalo de refresh do DDR4 é de poucos microssegundos: fora
        // dessa faixa o registro não é o do refresh.
        if (tRefiNs < 1000 || tRefiNs > 100_000)
        {
            erro = "O intervalo de refresh lido no controlador não é plausível.";
            return null;
        }

        // ---- troca de banco/grupo de bancos ---------------------------------
        // Sem os dois registradores lidos, o app não afirma nada: o estado
        // padrão (desligada) é o que o registro marcador significa.
        uint enderecoBgs0 = baseBloco - DesvioBankGroupSwap0;
        uint enderecoBgs1 = baseBloco - DesvioBankGroupSwap1;
        uint bgs0 = registros.Valor(enderecoBgs0);
        uint bgs1 = registros.Valor(enderecoBgs1);

        bool bankGroupSwap = registros.Valido(enderecoBgs0)
                          && registros.Valido(enderecoBgs1)
                          && !(bgs0 == bgs1 && bgs0 == MarcadorBankGroupSwap);

        return new TimingsAtivos
        {
            Tipo = tipo,
            ClockMhz = clockMhz,
            VelocidadeMts = velocidadeMts,
            GearDownMode = ((rel0 >> 11) & 1) != 0,
            CommandRate = ((rel0 & 0x400) >> 10) != 0 ? 2 : 1,
            BankGroupSwap = bankGroupSwap,
            TCl = tCl,
            TRas = tRas,
            TRcdLeitura = tRcdLeitura,
            TRcdEscrita = tRcdEscrita,
            TRp = tRp,
            TRc = tRc,
            TRrds = tRrds,
            TRrdl = tRrdl,
            TRtp = tRtp,
            TFaw = tFaw,
            TCwl = tCwl,
            TWtrs = tWtrs,
            TWtrl = tWtrl,
            TWr = tWr,
            TCke = tCke,
            TRefi = tRefi,
            TRefiNs = tRefiNs,
            TStag = tStag,
            EnderecoBloco = (int)baseBloco
        };
    }

    /// <summary>Converte o registro de tipo de memória no tipo correspondente.</summary>
    private static TipoMemoriaDdr Tipo(uint valor)
    {
        // Um registro que não foi lido vale 0xFFFFFFFF: isso é "não sei", não
        // "LPDDR5" (que seria o valor 3 nos dois bits baixos).
        if (valor == RegistrosSmn.SemDado) return TipoMemoriaDdr.Desconhecido;

        return (valor & 0x3) switch
        {
            0 => TipoMemoriaDdr.Ddr4,
            1 => TipoMemoriaDdr.Ddr5,
            2 => TipoMemoriaDdr.Lpddr4,
            3 => TipoMemoriaDdr.Lpddr5,
            _ => TipoMemoriaDdr.Desconhecido
        };
    }
}
