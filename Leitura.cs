namespace TranalSentinel;

/// <summary>
/// A leitura de um histórico: as linhas lidas e se a leitura ficou
/// completa.
///
/// <para>
/// A distinção é a diferença entre "não tinha nada" e "não deu para ler".
/// Uma lista vazia com <c>Completa = true</c> diz que o dia não teve uso;
/// uma lista vazia com <c>Completa = false</c> diz que o histórico não pôde
/// ser lido. Confundir as duas faz o app mentir sobre o próprio histórico
/// — e número errado é pior que número ausente.
///
/// <para>
/// O mesmo tipo serve para a leitura do CSV antigo
/// (<see cref="LegadoCsv"/>) e para a leitura do banco
/// (<see cref="Banco"/>): a regra é uma só.
/// </para>
/// </summary>
/// <typeparam name="T">O tipo da linha lida.</typeparam>
/// <param name="Linhas">As linhas lidas.</param>
/// <param name="Completa">
/// False quando a leitura parou no meio (o arquivo trancado, o banco
/// estragado, o disco cheio). Uma leitura pela metade <b>não pode valer
/// como leitura completa</b>: o que faltou nunca entra — e ninguém
/// ficaria sabendo.
/// </param>
public sealed record Leitura<T>(List<T> Linhas, bool Completa);
