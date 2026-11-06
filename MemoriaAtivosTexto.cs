using System.Collections.Generic;

namespace TranalSentinel;

/// <summary>
/// A montagem do texto dos tempos ativos, em um lugar só: a tela TRANAL MEMÓRIA
/// e o <c>--memoria</c> mostram as mesmas linhas, com as mesmas palavras.
/// </summary>
public static class MemoriaAtivosTexto
{
    /// <summary>Resumo em uma linha: clock, velocidade e os quatro tempos principais.</summary>
    public static string Resumo(TimingsAtivos ativos)
    {
        ArgumentNullException.ThrowIfNull(ativos);

        string extras = $"{ativos.CommandRate}T" + (ativos.GearDownMode ? " · gear down" : string.Empty);

        return $"{ativos.VelocidadeMts} MT/s ({ativos.ClockMhz} MHz) · {ativos.ResumoCiclos} · {extras}";
    }

    /// <summary>Como descrever a situação de um tempo em relação à referência.</summary>
    public static string Situacao(SituacaoTiming situacao) => situacao switch
    {
        SituacaoTiming.Igual => "igual",
        SituacaoTiming.MaisAperto => "mais apertado",
        SituacaoTiming.MaisSolto => "mais solto",
        _ => "—"
    };

    /// <summary>As linhas da tabela de comparação, prontas para a tela e para o texto.</summary>
    public static List<MemoriaAtivoLinhaVm> Linhas(RelatorioTimings relatorio)
    {
        ArgumentNullException.ThrowIfNull(relatorio);

        var linhas = new List<MemoriaAtivoLinhaVm>();
        foreach (ComparacaoTiming item in relatorio.Itens)
        {
            linhas.Add(new MemoriaAtivoLinhaVm
            {
                Nome = item.Nome,
                Ativo = item.Ativo?.ToString() ?? "—",
                Referencia = item.Perfil?.ToString() ?? "—",
                Situacao = Situacao(item.Situacao)
            });
        }

        return linhas;
    }
}
