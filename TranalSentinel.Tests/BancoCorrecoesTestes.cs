using System.IO;
using Xunit;

namespace TranalSentinel.Tests;

/// <summary>
/// Testes das correções da segunda revisão adversarial do banco de dados.
///
/// <para>
/// Cada teste prende uma falha que a revisão encontrou — a cópia avariada
/// que virava banco, a troca de programa que sumia, o relato do reparo que
/// dizia que nada se perdeu, os dias recentes que sumiam da lista. Sem
/// teste, a mesma falha volta na próxima mudança.
/// </para>
///
/// <para>
/// A classe fica na coleção "Banco": o banco e as pastas do CSV antigo são
/// estado global, e o xUnit roda as classes da mesma coleção em série.
/// </para>
/// </summary>
[Collection("Banco")]
public class BancoCorrecoesTestes : IDisposable
{
    private string? _pastaBanco;
    private string? _pastaLegada;

    public void Dispose()
    {
        BancoDeTeste.Restaurar();

        foreach (string? pasta in new[] { _pastaBanco, _pastaLegada })
        {
            if (pasta == null) continue;
            try
            {
                if (Directory.Exists(pasta)) Directory.Delete(pasta, true);
            }
            catch
            {
                // A pasta é temporária: se o Windows ainda estiver segurando o
                // arquivo, ela some sozinha depois.
            }
        }
    }

    private void UsarPastasTemporarias()
    {
        (_pastaBanco, _pastaLegada) = BancoDeTeste.Isolar();
    }

    private static AmostraSensor Amostra(DateTime momento) => new(
        momento,
        78.5f, 43f, 92.4f, 3200f,
        71f, 100f, 2010f, 2048f,
        87.4f, 12.5f, 0.3f);

    // ------------------------------------------------------------------ cópia avariada

    [Fact]
    public void CopiaAvariadaRecente_IgnoraOWalQueAcompanhaACopia()
    {
        UsarPastasTemporarias();

        Directory.CreateDirectory(Banco.Pasta);

        // A cópia do banco estragado e o WAL que vai junto com ela.
        string copia = Path.Combine(Banco.Pasta, "historico.db.avariado-20261108-034945");
        File.WriteAllBytes(copia, new byte[] { 1, 2, 3 });
        File.WriteAllBytes(copia + "-wal", new byte[] { 4, 5, 6 });

        // O nome do WAL é maior que o da cópia (ele tem o "-wal" no fim),
        // e a ordem é do maior para o menor: sem o filtro, o WAL era a
        // "cópia mais recente" — e restaurá-lo no lugar do banco apagava
        // o histórico em vez de trazê-lo de volta.
        Assert.Equal(copia, Banco.CopiaAvariadaRecente());
    }

    [Fact]
    public void CopiaAvariadaRecente_IgnoraArquivoComNomeForaDoPadrao()
    {
        UsarPastasTemporarias();

        Directory.CreateDirectory(Banco.Pasta);

        string copia = Path.Combine(Banco.Pasta, "historico.db.avariado-20261108-034945");
        File.WriteAllBytes(copia, new byte[] { 1, 2, 3 });

        // Um arquivo com o prefixo, mas sem a data completa: não é uma
        // cópia do reparo, e não pode virar o desfazer.
        File.WriteAllBytes(Path.Combine(Banco.Pasta, "historico.db.avariado-"), new byte[] { 4 });

        Assert.Equal(copia, Banco.CopiaAvariadaRecente());
    }

    [Fact]
    public void RestaurarCopia_DevolveOBancoEONaoOWal()
    {
        UsarPastasTemporarias();

        Directory.CreateDirectory(Banco.Pasta);

        string copia = Path.Combine(Banco.Pasta, "historico.db.avariado-20261108-034945");
        File.WriteAllBytes(copia, new byte[] { 1, 2, 3 });
        File.WriteAllBytes(copia + "-wal", new byte[] { 4, 5, 6, 7, 8, 9 });

        var resultado = Banco.RestaurarCopia();

        Assert.True(resultado.Sucesso);

        // O banco voltou no lugar do banco — e não o WAL.
        Assert.Equal(3, new FileInfo(Banco.Caminho).Length);

        // O WAL veio junto, com o nome que o SQLite entende: a cópia só
        // vale alguma coisa se trouxer o que ainda estava nele.
        Assert.True(File.Exists(Banco.Caminho + "-wal"));
        Assert.Equal(6, new FileInfo(Banco.Caminho + "-wal").Length);
    }

    // ------------------------------------------------------------------ troca de programa

    [Fact]
    public void GravarUso_ComDuasTrocasNoMesmoInstante_GravaAsDuas()
    {
        UsarPastasTemporarias();

        DateTime instante = DateTime.Today.AddHours(10);

        // Duas trocas no mesmo milissegundo, de programas diferentes: a
        // chave de identidade da troca é (instante, programa, título), e
        // as duas cabem. Nenhuma pode ser engolida.
        Assert.True(Banco.GravarUso(new TrocaUso(instante, "chrome", "Painel")));
        Assert.True(Banco.GravarUso(new TrocaUso(instante, "code", "projeto.cs")));

        var lidas = Banco.LerUso(instante);

        Assert.Equal(2, lidas.Count);
        Assert.Equal("chrome", lidas[0].Processo);
        Assert.Equal("code", lidas[1].Processo);
    }

    [Fact]
    public void GravarUso_ComAmesmaTrocaRepetida_NaoDuplicaEContinuaRegistrada()
    {
        UsarPastasTemporarias();

        var troca = new TrocaUso(DateTime.Today.AddHours(10), "chrome", "Painel");

        Assert.True(Banco.GravarUso(troca));

        // A mesma troca de novo: ela JÁ está registrada, e o amostrador
        // não pode ficar tentando de novo (e adiando a troca seguinte).
        Assert.True(Banco.GravarUso(troca));

        Assert.Single(Banco.LerUso(DateTime.Today));
    }

    // ------------------------------------------------------------------ relato do reparo

    [Fact]
    public void Reparar_ComBancoCorrompidoMasLegivel_ReconstituiOHistoricoInteiro()
    {
        UsarPastasTemporarias();

        for (int i = 0; i < 300; i++)
            Banco.GravarAmostra(Amostra(DateTime.Today.AddMinutes(i)));

        Banco.GravarUso(new TrocaUso(DateTime.Today.AddHours(10), "chrome", "Painel"));
        Banco.Fechar();

        // Uma página do banco é corrompida: a verificação de integridade
        // passa a reprovar, mas os dados continuam legíveis. É o caso em
        // que o reparo reconstrói e salva tudo.
        CorromperPaginaLegivel(Banco.Caminho, 300, 1);

        var resultado = Banco.Reparar();

        Assert.True(resultado.Sucesso, resultado.Relato);
        Assert.Equal(300, resultado.AmostrasRecuperadas);
        Assert.Equal(1, resultado.UsoRecuperado);
        Assert.Contains("300 amostras de sensor", resultado.Relato);

        // Nada se perdeu: o relato não pode inventar perda.
        Assert.False(resultado.Relato.Contains("Se perderam", StringComparison.OrdinalIgnoreCase));

        // E o banco novo funciona.
        Assert.Equal(300, Banco.Contagem().Amostras);
        Assert.Equal("ok", Banco.Estado);
    }

    [Fact]
    public void Reparar_ComTabelaIlegivel_NaoAfirmaQueNadaSePerdeu()
    {
        UsarPastasTemporarias();

        for (int i = 0; i < 300; i++)
            Banco.GravarAmostra(Amostra(DateTime.Today.AddMinutes(i)));

        Banco.Fechar();

        // O cabeçalho do arquivo é estragado: nem a contagem nem a
        // leitura funcionam.
        CorromperCabecalho(Banco.Caminho);

        var resultado = Banco.Reparar();

        Assert.True(resultado.Sucesso, resultado.Relato);

        // O relato tem que dizer que a contagem não deu — e não dizer que
        // não se perdeu nada. Perder em silêncio é o que o app não faz.
        Assert.Contains("Não deu para contar", resultado.Relato);
        Assert.False(resultado.Relato.Contains("Se perderam 0", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Reparar_ComLinhaInutilizavel_RelataQueElaSePerdeu()
    {
        UsarPastasTemporarias();

        for (int i = 0; i < 300; i++)
            Banco.GravarAmostra(Amostra(DateTime.Today.AddMinutes(i)));

        Banco.GravarUso(new TrocaUso(DateTime.Today.AddHours(10), "chrome", "Painel"));
        Banco.Fechar();

        // Uma página corrompida em que o histórico de uso volta com as
        // colunas vazias: a linha não pode ser gravada (o esquema não
        // aceita nulo), e o relato tem que dizer que ela se perdeu — o
        // INSERT não pode engolir a linha em silêncio.
        CorromperPaginaComUsoVazio(Banco.Caminho, 300);

        var resultado = Banco.Reparar();

        Assert.True(resultado.Sucesso, resultado.Relato);
        Assert.Equal(300, resultado.AmostrasRecuperadas);
        Assert.Equal(0, resultado.UsoRecuperado);
        Assert.Contains("Se perderam", resultado.Relato);
        Assert.Contains("1 registros de uso", resultado.Relato);
    }

    [Fact]
    public void Ver_ComBancoEstragado_NaoDizQueTemZeroAmostras()
    {
        UsarPastasTemporarias();

        for (int i = 0; i < 30; i++)
            Banco.GravarAmostra(Amostra(DateTime.Today.AddMinutes(i)));

        Banco.Fechar();
        CorromperCabecalho(Banco.Caminho);

        var retrato = Banco.Ver();

        // O banco não abre: a tela diz que precisa de reparo — e a
        // contagem mostra que não deu para fazer. "0 amostras" num banco
        // que tem 30 linhas seria mentira.
        Assert.NotEqual("ok", retrato.Estado);
        Assert.Equal(-1, retrato.Amostras);
        Assert.Equal(-1, retrato.Uso);
    }

    // ------------------------------------------------------------------ lista de dias

    [Fact]
    public void DiasComUso_ComHistoricoLongo_NaoEsqueceOsDiasRecentes()
    {
        UsarPastasTemporarias();

        // Um dia de três anos atrás e o dia de hoje. A varredura começa
        // pelo dia mais novo: começando pelo mais antigo, o limite de dias
        // consumia nos dias velhos — e o dia de hoje sumia da lista, que
        // é justamente o que a tela mostra primeiro.
        Banco.Instancia!.Executar(
            "INSERT OR IGNORE INTO uso (momento, processo, titulo) VALUES (?, ?, ?)",
            Sqlite.MomentoParaUnix(DateTime.Today.AddDays(-1100)), "antigo", "janela");

        Banco.Instancia.Executar(
            "INSERT OR IGNORE INTO uso (momento, processo, titulo) VALUES (?, ?, ?)",
            Sqlite.MomentoParaUnix(DateTime.Today.AddHours(10)), "chrome", "Painel");

        var dias = Banco.DiasComUso();

        Assert.Contains(DateTime.Today, dias);
        Assert.Contains(DateTime.Today.AddDays(-1100), dias);
    }

    // ------------------------------------------------------------------ migração

    [Fact]
    public void Migracao_ImportaSoOQueEstaDentroDoPrazo()
    {
        UsarPastasTemporarias();

        Directory.CreateDirectory(_pastaLegada!);

        // Uma amostra de dentro do prazo e uma de fora dele: o app só
        // promete guardar 15 dias de amostras.
        File.WriteAllLines(LegadoCsv.ArquivoSensores, new[]
        {
            LegadoCsv.MontarLinhaAmostra(Amostra(DateTime.Today.AddHours(10))),
            LegadoCsv.MontarLinhaAmostra(Amostra(DateTime.Today.AddDays(-40)))
        });

        var lidas = Banco.LerAmostras(DateTime.MinValue, DateTime.MaxValue);

        Assert.Single(lidas);
    }

    [Fact]
    public void Migracao_NaoImportaArquivosDeUsoForaDoPrazo()
    {
        UsarPastasTemporarias();

        Directory.CreateDirectory(_pastaLegada!);

        // O arquivo de hoje e o de 40 dias atrás: o app só promete
        // guardar 30 dias de histórico de uso.
        File.WriteAllLines(LegadoCsv.ArquivoDeUso(DateTime.Today), new[]
        {
            LegadoCsv.MontarLinhaUso(new TrocaUso(DateTime.Today.AddHours(10), "chrome", "Painel"))
        });

        File.WriteAllLines(LegadoCsv.ArquivoDeUso(DateTime.Today.AddDays(-40)), new[]
        {
            LegadoCsv.MontarLinhaUso(new TrocaUso(DateTime.Today.AddDays(-40).AddHours(10), "code", "antigo.cs"))
        });

        var lidas = Banco.LerUso(DateTime.Today);

        Assert.Single(lidas);
        Assert.Equal("chrome", lidas[0].Processo);
    }

    [Fact]
    public void Migracao_GravaAVersaoDoEsquemaDentroDaTransacao()
    {
        UsarPastasTemporarias();

        Directory.CreateDirectory(_pastaLegada!);
        File.WriteAllLines(LegadoCsv.ArquivoSensores, new[]
        {
            LegadoCsv.MontarLinhaAmostra(Amostra(DateTime.Today.AddHours(10)))
        });

        Banco.LerAmostras(DateTime.MinValue, DateTime.MaxValue);

        // A versão do esquema é a marca de "o CSV já entrou". Se ela
        // fosse gravada fora da transação, uma importação pela metade
        // deixaria o histórico antigo órfão, sem aviso.
        var linhas = Banco.Instancia!.Consultar("PRAGMA user_version");

        Assert.Single(linhas);
        Assert.Equal(1, linhas[0].Inteiro(linhas[0].Colunas[0]));
    }

    // ------------------------------------------------------------------ limpeza e retrato

    [Fact]
    public void Limpar_ReiniciaASequenciaDoHistoricoDeUso()
    {
        UsarPastasTemporarias();

        Banco.GravarUso(new TrocaUso(DateTime.Today.AddHours(10), "chrome", "Painel"));
        Banco.GravarUso(new TrocaUso(DateTime.Today.AddHours(11), "code", "projeto.cs"));

        Assert.True(Banco.Limpar());

        // A sequência da tabela de uso foi reiniciada: o próximo registro
        // começa de novo no 1. (A tabela sqlite_sequence só existe depois
        // que um AUTOINCREMENT foi usado — apagar dela quando ela não
        // existe desfaazia a limpeza inteira.)
        Banco.GravarUso(new TrocaUso(DateTime.Today.AddHours(12), "code", "outro.cs"));

        var linhas = Banco.Instancia!.Consultar("SELECT id FROM uso");

        var linha = Assert.Single(linhas);
        Assert.Equal(1, linha.Inteiro("id"));
    }

    [Fact]
    public void Ver_ComBancoIntegro_TrazOCountEOEstadoJuntos()
    {
        UsarPastasTemporarias();

        Banco.GravarAmostra(Amostra(DateTime.Today.AddHours(10)));
        Banco.GravarUso(new TrocaUso(DateTime.Today.AddHours(10), "chrome", "Painel"));

        var retrato = Banco.Ver();

        Assert.Equal("ok", retrato.Estado);
        Assert.Equal(1, retrato.Amostras);
        Assert.Equal(1, retrato.Uso);
        Assert.True(retrato.TamanhoBytes > 0);
        Assert.Equal(Banco.Caminho, retrato.Caminho);
    }

    [Fact]
    public void Ver_SemBancoNaoDevolveNumeroInventado()
    {
        Banco.PastaOverride = "Z:\\inexistente\\pasta\\que\\nao\\existe";
        Banco.Fechar();

        try
        {
            var retrato = Banco.Ver();

            // Sem banco não há contagem: o retrato diz que não está
            // disponível, e não inventa "0 amostras" como se o histórico
            // tivesse sido medido.
            Assert.NotEqual("ok", retrato.Estado);
            Assert.Equal(0, retrato.Amostras);
            Assert.Equal(0, retrato.Uso);
        }
        finally
        {
            BancoDeTeste.Restaurar();
        }
    }

    // ------------------------------------------------------------------ auxiliares

    /// <summary>
    /// Corrompe o cabeçalho do arquivo do banco: o arquivo deixa de ser um
    /// banco, e nem a contagem nem a leitura funcionam.
    /// </summary>
    private static void CorromperCabecalho(string caminho)
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
    private static void CorromperPaginaLegivel(string caminho, int amostrasEsperadas, int usosEsperados)
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
    private static void CorromperPaginaComUsoVazio(string caminho, int amostrasEsperadas)
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
