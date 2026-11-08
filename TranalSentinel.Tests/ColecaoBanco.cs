using Xunit;

namespace TranalSentinel.Tests;

/// <summary>
/// Coleção dos testes que mexem no estado global do banco de dados do
/// histórico (a pasta onde ele mora).
///
/// <para>
/// O xUnit roda classes diferentes em paralelo. Como a pasta do banco é um
/// estado global (uma classe aponta o banco para uma pasta temporária e a
/// outra apaga a pasta), as classes que usam esse desvio ficam na mesma
/// coleção, que o xUnit executa em série.
/// </para>
/// </summary>
[CollectionDefinition("Banco", DisableParallelization = true)]
public class ColecaoBanco
{
}
