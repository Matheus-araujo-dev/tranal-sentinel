using System;
using Xunit;

namespace TranalSentinel.Tests;

/// <summary>
/// Testes do texto do dia e do item que os seletores de dia mostram.
///
/// <para>
/// Duas páginas têm seletor de dia (o TRANAL USO e o TRANAL REDE). Cada uma
/// tinha a sua versão do rótulo — e uma delas escrevia "HOJE" e a outra
/// "hoje". E o ComboBox da casa escreve o item selecionado como texto puro:
/// sem o <c>ToString</c> do item, a caixa fechada mostrava o registro
/// inteiro ("OpcaoDia { Data = ... }") em vez do dia. Os dois defeitos
/// foram achados olhando a tela (09/11/2026), e cada teste aqui prende um
/// deles.
/// </para>
/// </summary>
public class DiaTextoTestes
{
    private static readonly DateTime Hoje = new(2026, 11, 9, 14, 32, 11);

    [Fact]
    public void Rotulo_DeHojeEHojem()
    {
        Assert.Equal("hoje", DiaTexto.Rotulo(Hoje, Hoje));
    }

    [Fact]
    public void Rotulo_DeOntemEOntem()
    {
        Assert.Equal("ontem", DiaTexto.Rotulo(Hoje.AddDays(-1), Hoje));
    }

    [Fact]
    public void Rotulo_DeUmDiaAntigoEAdata()
    {
        Assert.Equal("04/11/2026", DiaTexto.Rotulo(Hoje.AddDays(-5), Hoje));
    }

    [Fact]
    public void Rotulo_IgnoraAHoraDoDia()
    {
        // 23:59 do dia de hoje ainda é "hoje".
        Assert.Equal("hoje", DiaTexto.Rotulo(Hoje.Date.AddHours(23).AddMinutes(59), Hoje.Date));
    }

    [Fact]
    public void RotuloMaiusculo_EAMesmaRegraEmCaixaAlta()
    {
        Assert.Equal("HOJE", DiaTexto.RotuloMaiusculo(Hoje, Hoje));
        Assert.Equal("ONTEM", DiaTexto.RotuloMaiusculo(Hoje.AddDays(-1), Hoje));
        Assert.Equal("04/11/2026", DiaTexto.RotuloMaiusculo(Hoje.AddDays(-5), Hoje));
    }

    [Fact]
    public void Rotulo_EDaMesmaRegraNasDuasPaginas()
    {
        // A regra é uma só: o TRANAL USO e o TRANAL REDE não podem escrever
        // dias diferentes para o mesmo dia.
        Assert.Equal(UsoTexto.RotuloDia(Hoje, Hoje), DiaTexto.Rotulo(Hoje, Hoje));
        Assert.Equal(UsoTexto.RotuloDia(Hoje.AddDays(-1), Hoje), DiaTexto.Rotulo(Hoje.AddDays(-1),Hoje));
    }

    [Fact]
    public void OpcaoDia_EscreveORotulo()
    {
        // O ComboBox da casa escreve o item selecionado como texto puro.
        var opcao = new OpcaoDia(Hoje, "HOJE");

        Assert.Equal("HOJE", opcao.ToString());
    }

    [Fact]
    public void ItemDoCatalogoDeSensores_EscreveORotulo()
    {
        // O seletor de sensor da página de alertas tem o mesmo problema.
        var item = new SensorCatalogoItem { Chave = "gpu-temp", Rotulo = "Temperatura da GPU" };

        Assert.Equal("Temperatura da GPU", item.ToString());
    }
}
