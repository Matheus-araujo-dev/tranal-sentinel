using System;
using Xunit;

namespace TranalSentinel.Tests;

/// <summary>
/// Testes do coletor de tráfego (a parte que não precisa de ETW para ser
/// testada).
///
/// <para>
/// O crash de 09/11/2026 aconteceu aqui: um processo que já tinha morrido
/// entre o evento e a hora de perguntar o nome devolvia um nome NULO, e o
/// dicionário do ranking não aceita chave nula — a exceção subiu pelo
/// tique do relógio e derrubou o app inteiro. Cada teste aqui prende esse
/// tipo de falha: nome nunca nulo, base nunca somada duas vezes, e o
/// total do dia nunca misturado com o dia anterior.
/// </para>
/// </summary>
public class ColetorTrafegoTestes
{
    private static readonly DateTime Dia = new(2026, 11, 9, 0, 0, 0);

    [Fact]
    public void NomeDoProcesso_DeProcessoQueNaoExiste_NaoDevolveNulo()
    {
        using var coletor = new ColetorTrafego();

        // Um PID que não existe: o nome tem de ser "(encerrado)", nunca
        // nulo — um nome nulo derrubaria o app no ranking.
        string nome = coletor.NomeDoProcesso(999999);

        Assert.False(string.IsNullOrEmpty(nome));
        Assert.Equal("(encerrado)", nome);
    }

    [Fact]
    public void NomeDoProcesso_DeProcessoQueExiste_DevolveONome()
    {
        using var coletor = new ColetorTrafego();

        // O próprio processo de teste existe com certeza.
        string nome = coletor.NomeDoProcesso(Environment.ProcessId);

        Assert.False(string.IsNullOrEmpty(nome));
        Assert.NotEqual("(encerrado)", nome);
    }

    [Fact]
    public void Acumulado_SemNadaMedido_NaoQuebra()
    {
        using var coletor = new ColetorTrafego();

        Assert.Empty(coletor.Acumulado(Dia));
        Assert.Empty(coletor.Acumulado());
    }

    [Fact]
    public void Semear_ColocaABaseNoDia()
    {
        using var coletor = new ColetorTrafego();

        coletor.Semear(Dia, new[] { new TrafegoPrograma("chrome", 1000, 2000) });

        var acumulado = coletor.Acumulado(Dia);

        var linha = Assert.Single(acumulado);
        Assert.Equal("chrome", linha.Processo);
        Assert.Equal(1000, linha.Enviados);
        Assert.Equal(2000, linha.Recebidos);
    }

    [Fact]
    public void Semear_DuasVezes_NaoSomaABase()
    {
        using var coletor = new ColetorTrafego();

        // O banco é a fonte da verdade do que já estava salvo: semear duas
        // vezes colocaria o mesmo byte na conta duas vezes.
        coletor.Semear(Dia, new[] { new TrafegoPrograma("chrome", 1000, 0) });
        coletor.Semear(Dia, new[] { new TrafegoPrograma("chrome", 1000, 0) });

        var linha = Assert.Single(coletor.Acumulado(Dia));
        Assert.Equal(1000, linha.Enviados);
    }

    [Fact]
    public void Semear_ComNomeVazio_NaoDevolveNomeNulo()
    {
        using var coletor = new ColetorTrafego();

        coletor.Semear(Dia, new[] { new TrafegoPrograma(null!, 1000, 0) });

        var linha = Assert.Single(coletor.Acumulado(Dia));
        Assert.Equal("(sem nome)", linha.Processo);
    }

    [Fact]
    public void Semear_EmDiasDiferentes_NaoMistura()
    {
        using var coletor = new ColetorTrafego();

        coletor.Semear(Dia, new[] { new TrafegoPrograma("chrome", 1000, 0) });
        coletor.Semear(Dia.AddDays(-1), new[] { new TrafegoPrograma("chrome", 2000, 0) });

        Assert.Equal(1000, coletor.Acumulado(Dia)[0].Enviados);
        Assert.Equal(2000, coletor.Acumulado(Dia.AddDays(-1))[0].Enviados);
    }

    [Fact]
    public void AnotarErro_DeixaOMotivoGuardado()
    {
        using var coletor = new ColetorTrafego();

        Assert.Null(coletor.Erro);

        coletor.AnotarErro("falhou por causa X");

        Assert.Equal("falhou por causa X", coletor.Erro);
    }

    [Fact]
    public void Iniciar_SemAdministrador_NaoLanca()
    {
        using var coletor = new ColetorTrafego();

        // A ETW exige administrador. Sem ele, o coletor guarda o motivo —
        // e não derruba quem o chamou (a prova roda em uma máquina sem
        // elevação, e o teste tem de passar igual).
        Exception? erro = Record.Exception(() => coletor.Iniciar());

        Assert.Null(erro);
    }

    [Fact]
    public void Parar_ComAColetaDesligada_NaoLanca()
    {
        using var coletor = new ColetorTrafego();

        Exception? erro = Record.Exception(() => coletor.Parar());

        Assert.Null(erro);
    }
}
