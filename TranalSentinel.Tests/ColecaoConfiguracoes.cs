using Xunit;

namespace TranalSentinel.Tests;

/// <summary>
/// Coleção dos testes que mexem no estado global do app (a pasta das
/// configurações).
///
/// <para>
/// O xUnit roda classes diferentes em paralelo. Duas classes que
/// redirecionam a mesma pasta global uma contra a outra corrompem o
/// arquivo — por isso elas ficam na mesma coleção, que o xUnit executa em
/// série.
/// </para>
/// </summary>
[CollectionDefinition("Configuracoes", DisableParallelization = true)]
public class ColecaoConfiguracoes
{
}
