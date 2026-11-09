using System.IO;
using Xunit;

namespace TranalSentinel.Tests;

/// <summary>
/// As ferramentas de estrago do banco de dados, usadas nos testes.
///
/// <para>
/// A estragagem de um arquivo SQLite é um trabalho chato e delicado (tem
/// que reprovar na verificação de integridade e continuar legível ao
/// mesmo tempo). Ele mora aqui, em um lugar só, para que os testes das
/// várias rodadas usem a mesma ferramenta — e não cada um com a sua
/// versão, que quebra na primeira diferença.
/// </para>
/// </summary>
internal static class BancoCorrupcao
{
    /// <summary>
    /// Corrompe o cabeçalho do arquivo do banco: o arquivo deixa de ser um
    /// banco, e nem a contagem nem a leitura funcionam.
    /// </summary>
    internal static void CorromperCabecalho(string caminho)
    {
        byte[] bytes = File.ReadAllBytes(caminho);
        for (int i = 0; i < 16; i++)
            bytes[i] = 0x5A;

        File.WriteAllBytes(caminho, bytes);
    }

    /// <summary>
    /// Corrompe uma página do banco de um jeito que os dados continuam
    /// legíveis (a verificação reprova, mas a leitura funciona). É o caso
    /// mais comum de banco estragado: o arquivo tem um problema, e o
    /// histórico ainda está lá.
    /// </summary>
    internal static void CorromperPaginaLegivel(string caminho, int amostrasEsperadas, int usosEsperados)
    {
        byte[] original = File.ReadAllBytes(caminho);
        int tamanhoPagina = (original[16] << 8) | original[17];
        int paginas = original.Length / tamanhoPagina;

        for (int p = 2; p <= paginas; p++)
        {
            byte[] copia = (byte[])original.Clone();
            int inicio = (p - 1) * tamanhoPagina + 8;
            for (int i = 0; i < 60; i++)
                copia[inicio + i] = 0x5A;

            string teste = caminho + ".teste";
            File.WriteAllBytes(teste, copia);

            bool serve = false;
            using (var banco = Sqlite.Abrir(teste))
            {
                if (banco != null
                    && banco.VerificarIntegridade() != "ok"
                    && banco.Consultar("SELECT momento FROM amostras").Count == amostrasEsperadas)
                {
                    // O histórico de uso precisa voltar com os valores
                    // inteiros: uma página corrompida pode devolver a
                    // linha com as colunas vazias.
                    var linhasUso = banco.Consultar("SELECT momento, processo, titulo FROM uso");
                    serve = linhasUso.Count == usosEsperados;
                    foreach (var linha in linhasUso)
                    {
                        if (!linha.TemValor("momento") || linha.Texto("processo").Length == 0)
                            serve = false;
                    }
                }
            }

            if (!serve) continue;

            File.WriteAllBytes(caminho, copia);
            return;
        }

        Assert.Fail("Nenhuma página do banco pôde ser corrompida de um jeito que os dados continuassem legíveis.");
    }

    /// <summary>
    /// Corrompe uma página do banco de um jeito em que o histórico de uso
    /// volta com as colunas vazias (e o resto do histórico continua
    /// inteiro). É a linha que não pode ser gravada de volta.
    /// </summary>
    internal static void CorromperPaginaComUsoVazio(string caminho, int amostrasEsperadas)
    {
        byte[] original = File.ReadAllBytes(caminho);
        int tamanhoPagina = (original[16] << 8) | original[17];
        int paginas = original.Length / tamanhoPagina;

        for (int p = 2; p <= paginas; p++)
        {
            byte[] copia = (byte[])original.Clone();
            int inicio = (p - 1) * tamanhoPagina + 8;
            for (int i = 0; i < 60; i++)
                copia[inicio + i] = 0x5A;

            string teste = caminho + ".teste";
            File.WriteAllBytes(teste, copia);

            bool serve = false;
            using (var banco = Sqlite.Abrir(teste))
            {
                if (banco != null && banco.Consultar("SELECT momento FROM amostras").Count == amostrasEsperadas)
                {
                    var linhasUso = banco.Consultar("SELECT momento, processo, titulo FROM uso");
                    serve = linhasUso.Count > 0;
                    foreach (var linha in linhasUso)
                    {
                        if (linha.TemValor("momento")) serve = false;
                    }
                }
            }

            if (!serve) continue;

            File.WriteAllBytes(caminho, copia);
            return;
        }

        Assert.Fail("Nenhuma página do banco devolveu o histórico de uso com as colunas vazias.");
    }
}
