using System.Threading;
using Xunit;

namespace TranalSentinel.Tests;

/// <summary>
/// Testes da trava de instância única.
///
/// <para>
/// A trava decide duas coisas: se o app pode subir (a segunda instância
/// só traz a janela da primeira para a frente) e se a linha de comando
/// do banco pode trocar o arquivo do histórico (com o app aberto, o
/// Windows segura o arquivo e a troca falha).
///
/// <para>
/// A classe fica na coleção "Banco" porque a trava é estado global da
/// máquina: dois testes que mexem nela ao mesmo tempo mentem um sobre o
/// outro.
/// </para>
/// </summary>
[Collection("Banco")]
public class InstanciaUnicaTestes
{
    [Fact]
    public void EstaRodando_SemNinguemNoApp_DizQueNao()
    {
        Assert.False(InstanciaUnica.EstaRodando());
    }

    [Fact]
    public void EstaRodando_ComATravaSegurada_DizQueSim()
    {
        using var trava = new Mutex(true, InstanciaUnica.Nome, out bool criado);

        Assert.True(criado);
        Assert.True(InstanciaUnica.EstaRodando());
    }

    [Fact]
    public void Segurar_ComATravaJaSegurada_DevolveNulo()
    {
        using var trava = new Mutex(true, InstanciaUnica.Nome, out bool criado);

        Assert.True(criado);

        // A segunda instância não sobe: o app já está rodando.
        Assert.Null(InstanciaUnica.Segurar());
    }
}
