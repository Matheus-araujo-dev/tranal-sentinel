using Xunit;

namespace TranalSentinel.Tests;

/// <summary>
/// Coleção dos testes que mexem no estado global do log de sensores (a pasta
/// onde o histórico é gravado).
///
/// <para>
/// O xUnit roda classes diferentes em paralelo. Como a pasta do histórico é
/// um estado global (uma classe aponta o log para uma pasta temporária e a
/// outra apaga a pasta), as classes que usam esse desvio ficam na mesma
/// coleção, que o xUnit executa em série.
/// </para>
/// </summary>
[CollectionDefinition("Sensores", DisableParallelization = true)]
public class ColecaoSensores
{
}
