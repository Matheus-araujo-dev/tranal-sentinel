using System;
using System.Diagnostics;
using System.Linq;
using Xunit;

namespace TranalSentinel.Tests;

/// <summary>
/// Testes da gravação do tráfego em massa.
///
/// <para>
/// O nome do arquivo veio de uma revisão (era um arquivo temporário de
/// medição). O conteúdo é permanente: a gravação acontece na thread da
/// tela, a cada dez segundos, e não pode travar a janela.
/// </para>
/// </summary>
public class TrafegoGravacaoEmMassaTestes
{
    /// <summary>
    /// Quantos programas a tela mostra no ranking. A gravação, não: o
    /// banco guarda todos.
    /// </summary>
    private const int Programas = 200;

    [Fact]
    public void GravarTrafego_ComMuitosProgramas_GravaTodos()
    {
        BancoDeTeste.Isolar();
        try
        {
            var dia = DateTime.Today;
            var itens = Enumerable.Range(0, Programas)
                .Select(i => new TrafegoPrograma("programa" + i, 1000, 0))
                .ToList();

            Banco.GravarTrafego(dia, itens);

            var leitura = Banco.LerTrafego(dia);

            Assert.True(leitura.Completa);
            Assert.Equal(Programas, leitura.Linhas.Count);
        }
        finally
        {
            BancoDeTeste.Restaurar();
        }
    }

    [Fact]
    public void GravarTrafego_NaoDemoraNaThreadDaTela()
    {
        BancoDeTeste.Isolar();
        try
        {
            var dia = DateTime.Today;
            var itens = Enumerable.Range(0, Programas)
                .Select(i => new TrafegoPrograma("programa" + i, 1000, 0))
                .ToList();

            Banco.GravarTrafego(dia, itens);

            var sw = Stopwatch.StartNew();
            for (int i = 0; i < 10; i++) Banco.GravarTrafego(dia, itens);
            sw.Stop();

            // Medido em 09/11/2026: 1,4 ms por gravacao de 200 programas.
            // O limite aqui e folgado de proposito — o que ele prende e a
            // gravacao na thread da tela virar um travamento, nao a
            // diferenca de um milissegundo.
            Assert.True(sw.ElapsedMilliseconds < 2000,
                "A gravacao do trafego na thread da tela demorou " +
                sw.ElapsedMilliseconds + " ms para 10 gravacoes de " +
                Programas + " programas.");
        }
        finally
        {
            BancoDeTeste.Restaurar();
        }
    }
}
