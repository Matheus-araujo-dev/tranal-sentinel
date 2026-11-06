namespace TranalSentinel;

/// <summary>
/// Coleta os tempos de memória que estão <b>ativos</b> no controlador do AMD,
/// lendo os registradores do UMC pelo módulo PawnIO do SMN.
///
/// <para>
/// É a parte impura da feature: fala com o driver e entrega os registradores
/// crus para <see cref="TimingsAtivosDecoder"/>, que é quem interpreta.
/// </para>
/// </remarks>
public static class MemoriaAtivos
{
    /// <summary>Nome do arquivo do módulo PawnIO do AMD (famílias 17h a 1Ah).</summary>
    public const string ArquivoModulo = "AMDFamily17.bin";

    /// <summary>
    /// Lê os tempos ativos.
    /// </summary>
    /// <param name="velocidadeEsperadaMts">
    /// Velocidade que o Windows informa como configurada. Serve de conferência
    /// do clock lido no controlador (0 = não conferir).
    /// </param>
    /// <param name="erro">Motivo da recusa, quando não deu para ler.</param>
    public static TimingsAtivos? Coletar(int velocidadeEsperadaMts, out string? erro)
    {
        byte[]? modulo = ModulosPawnIo.Obter(ArquivoModulo, ModulosPawnIo.RecursoModuloAmdFamilia17);

        SmnPawnIo? smn = SmnPawnIo.Abrir(modulo, out string? erroAbrir);
        if (smn == null)
        {
            erro = erroAbrir;
            return null;
        }

        try
        {
            // Todos os registradores dos dois canais, lidos de uma vez: o
            // mutex do SMN é tomado uma única vez, e o registro que é lido
            // não pode ser trocado por outra ferramenta no meio da leitura.
            var enderecos = new List<uint>();
            foreach (uint baseBloco in new[] { TimingsAtivosDecoder.BlocoCanal0, TimingsAtivosDecoder.BlocoCanal1 })
            {
                foreach (uint desvio in TimingsAtivosDecoder.Desvios)
                {
                    enderecos.Add(baseBloco + desvio);
                }

                foreach (uint desvio in new[]
                         {
                             TimingsAtivosDecoder.DesvioTipoMemoria,
                             TimingsAtivosDecoder.DesvioBankGroupSwap0,
                             TimingsAtivosDecoder.DesvioBankGroupSwap1
                         })
                {
                    enderecos.Add(baseBloco - desvio);
                }
            }

            var valores = new Dictionary<uint, uint>();
            smn.LerBloco(enderecos, valores);

            return TimingsAtivosDecoder.Decodificar(new RegistrosSmn(valores), velocidadeEsperadaMts, out erro);
        }
        finally
        {
            smn.Dispose();
        }
    }
}
