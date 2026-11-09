using System.Runtime.InteropServices;
using System.Text;

namespace TranalSentinel;

/// <summary>
/// O acesso ao SQLite do Windows, por P/Invoke.
///
/// <para>
/// O app não ganha dependência nenhuma: o Windows 10 e o 11 já trazem o
/// SQLite em <c>System32\winsqlite3.dll</c> e a gente chama a API C dele
/// direto. É a mesma biblioteca que o próprio Windows usa internamente.
///
/// <para>
/// Tudo que entra e sai daqui é UTF-8 (o SQLite guarda texto em UTF-8), e
/// a conversão é feita na mão — a marshalling padrão do .NET usaria a
/// codepage do Windows, e um usuário com acento no nome da pasta perderia
/// o texto.
///
/// <para>
/// A API é mínima de propósito: abrir, executar, consultar, transação e
/// verificar integridade. Se aparecer algum outro uso, ele entra aqui — e
/// só aqui.
/// </para>
/// </summary>
public sealed class Sqlite : IDisposable
{
    /// <summary>A biblioteca do Windows que traz o SQLite.</summary>
    private const string Biblioteca = "winsqlite3.dll";

    private const int Ok = 0;
    private const int Linha = 100;
    private const int Feito = 101;

    private const int TipoInteiro = 1;
    private const int TipoReal = 2;
    private const int TipoNulo = 5;

    private const int AbrirLeituraEscrita = 0x00000002;
    private const int AbrirCriar = 0x00000004;
    private const int AbrirFullMutex = 0x00010000;

    /// <summary>O SQLite copia o texto na hora (em vez de guardar o ponteiro).</summary>
    private static readonly IntPtr Transitorio = new(-1);

    private IntPtr _banco;
    private bool _fechado;
    private string _ultimoErro = "";

    /// <summary>
    /// Serializa o uso do handle.
    ///
    /// <para>
    /// O reparo fecha o banco e troca o arquivo no lugar. Se outra thread
    /// estivesse no meio de um <c>sqlite3_step</c> nesse instante, o handle
    /// fecharia por baixo dela — e um handle freed é acesso inválido, que o
    /// .NET não consegue pegar: o processo inteiro cai. Com a trava, o
    /// fechamento espera a operação em curso terminar.
    /// </para>
    /// </summary>
    private readonly object _tranca = new();

    private Sqlite(IntPtr banco) => _banco = banco;

    // ------------------------------------------------------------------ P/Invoke

    [DllImport(Biblioteca, CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_open_v2(IntPtr filename, out IntPtr db, int flags, IntPtr vfs);

    [DllImport(Biblioteca, CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_close(IntPtr db);

    [DllImport(Biblioteca, CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_exec(IntPtr db, IntPtr sql, IntPtr callback, IntPtr arg, out IntPtr errmsg);

    [DllImport(Biblioteca, CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_prepare_v2(IntPtr db, IntPtr sql, int nByte, out IntPtr stmt, out IntPtr tail);

    [DllImport(Biblioteca, CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_step(IntPtr stmt);

    [DllImport(Biblioteca, CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_finalize(IntPtr stmt);

    [DllImport(Biblioteca, CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_bind_text(IntPtr stmt, int index, IntPtr value, int n, IntPtr destructor);

    [DllImport(Biblioteca, CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_bind_int64(IntPtr stmt, int index, long value);

    [DllImport(Biblioteca, CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_bind_double(IntPtr stmt, int index, double value);

    [DllImport(Biblioteca, CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_bind_null(IntPtr stmt, int index);

    [DllImport(Biblioteca, CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_column_count(IntPtr stmt);

    [DllImport(Biblioteca, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr sqlite3_column_text(IntPtr stmt, int index);

    [DllImport(Biblioteca, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr sqlite3_column_name(IntPtr stmt, int index);

    [DllImport(Biblioteca, CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_column_type(IntPtr stmt, int index);

    [DllImport(Biblioteca, CallingConvention = CallingConvention.Cdecl)]
    private static extern long sqlite3_column_int64(IntPtr stmt, int index);

    [DllImport(Biblioteca, CallingConvention = CallingConvention.Cdecl)]
    private static extern double sqlite3_column_double(IntPtr stmt, int index);

    [DllImport(Biblioteca, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr sqlite3_errmsg(IntPtr db);

    [DllImport(Biblioteca, CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_busy_timeout(IntPtr db, int ms);

    [DllImport(Biblioteca, CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_changes(IntPtr db);

    [DllImport(Biblioteca, CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_reset(IntPtr stmt);

    [DllImport(Biblioteca, CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_clear_bindings(IntPtr stmt);

    // ------------------------------------------------------------------ ciclo de vida

    /// <summary>
    /// Abre (ou cria) um banco. Devolve null quando não dá para abrir — o
    /// chamador decide o que fazer, e ninguém derruba o app por causa disso.
    /// </summary>
    public static Sqlite? Abrir(string caminho)
    {
        IntPtr banco = IntPtr.Zero;
        IntPtr caminhoUtf8 = IntPtr.Zero;

        try
        {
            caminhoUtf8 = Marshal.StringToCoTaskMemUTF8(caminho);
            int rc = sqlite3_open_v2(caminhoUtf8, out banco, AbrirLeituraEscrita | AbrirCriar | AbrirFullMutex, IntPtr.Zero);
            if (rc != Ok)
            {
                if (banco != IntPtr.Zero) sqlite3_close(banco);
                return null;
            }

            var db = new Sqlite(banco);

            // Espera se outro uso do banco estiver escrevendo (o app abre o
            // banco de mais de um lugar: a coleta e a tela).
            sqlite3_busy_timeout(banco, 5000);

            return db;
        }
        catch (DllNotFoundException)
        {
            // Windows sem o winsqlite3.dll: não há banco, mas o app segue.
            return null;
        }
        catch (EntryPointNotFoundException)
        {
            return null;
        }
        finally
        {
            if (caminhoUtf8 != IntPtr.Zero) Marshal.FreeCoTaskMem(caminhoUtf8);
        }
    }

    /// <summary>Fecha o banco. Nunca lança.</summary>
    public void Dispose()
    {
        lock (_tranca)
        {
            if (_fechado) return;
            _fechado = true;

            try
            {
                if (_banco != IntPtr.Zero) sqlite3_close(_banco);
            }
            catch
            {
                // Fechar o banco é a última coisa que o app faz com ele: uma
                // falha aqui não pode virar exceção na frente do usuário.
            }
            finally
            {
                _banco = IntPtr.Zero;
            }
        }
    }

    /// <summary>
    /// A mensagem de erro da última operação ("" quando não houve).
    ///
    /// <para>
    /// A consulta que falha devolve lista vazia — e lista vazia também é
    /// o resultado de uma tabela sem linhas. O motivo do erro é o que
    /// separa as duas coisas, e é o que permite ao reparo saber se a
    /// leitura falhou ou se a tabela estava mesmo vazia.
    /// </para>
    /// </summary>
    public string UltimoErro
    {
        get
        {
            lock (_tranca)
            {
                if (_banco == IntPtr.Zero) return "banco fechado";

                // A mensagem guardada é a do ÚLTIMO erro — e não a de uma
                // falha antiga. O SQLite guarda a mensagem do último erro
                // para sempre: consultá-la depois de uma operação que
                // deu certo devolveria a falha velha, e o reparo passaria
                // a dizer que a leitura do banco estragado falhou (quando
                // ela tinha dado certo).
                return _ultimoErro;
            }
        }
    }

    /// <summary>
    /// Quantas linhas a última execução alterou. Um <c>INSERT OR IGNORE</c>
    /// que ignorou a linha (o registro já existia) altera zero — e é assim
    /// que a coleta sabe que a troca não foi gravada.
    /// </summary>
    public int Alteracoes
    {
        get
        {
            lock (_tranca)
            {
                return _banco == IntPtr.Zero ? 0 : sqlite3_changes(_banco);
            }
        }
    }

    // ------------------------------------------------------------------ comandos

    /// <summary>
    /// Executa um comando (CREATE, INSERT, DELETE...). Sem argumentos, é só
    /// SQL; com argumentos, cada <c>?</c> é preenchido na ordem.
    /// </summary>
    public bool Executar(string sql, params object?[] argumentos) =>
        Executar(sql, (IReadOnlyList<object?>)argumentos);

    /// <summary>
    /// Executa um comando com uma lista de argumentos (para o INSERT em
    /// lote, em que os mesmos <c>?</c> se repetem a cada linha).
    /// </summary>
    public bool Executar(string sql, IReadOnlyList<object?> argumentos)
    {
        lock (_tranca)
        {
            if (_banco == IntPtr.Zero) return false;

            IntPtr stmt = IntPtr.Zero;
            try
            {
                stmt = Preparar(sql, argumentos);
                if (stmt == IntPtr.Zero)
                {
                    _ultimoErro = MotivoDoErro(null);
                    return false;
                }

                int rc = sqlite3_step(stmt);
                _ultimoErro = "";
                return rc == Feito || rc == Linha;
            }
            catch (Exception ex)
            {
                // Falha de SQL não derruba o app: quem chama decide o que fazer
                // (o histórico é acessório, o diagnóstico não pode quebrar).
                _ultimoErro = MotivoDoErro(ex);
                return false;
            }
            finally
            {
                if (stmt != IntPtr.Zero) sqlite3_finalize(stmt);
            }
        }
    }

    /// <summary>Consulta que devolve linhas.</summary>
    public List<SqliteLinha> Consultar(string sql, params object?[] argumentos)
    {
        var linhas = new List<SqliteLinha>();

        lock (_tranca)
        {
            if (_banco == IntPtr.Zero) return linhas;

            IntPtr stmt = IntPtr.Zero;
            try
            {
                stmt = Preparar(sql, argumentos);
                if (stmt == IntPtr.Zero)
                {
                    _ultimoErro = MotivoDoErro(null);
                    return new List<SqliteLinha>();
                }

                int colunas = sqlite3_column_count(stmt);
                var nomes = new string[colunas];
                for (int c = 0; c < colunas; c++)
                {
                    IntPtr nome = sqlite3_column_name(stmt, c);
                    nomes[c] = nome == IntPtr.Zero ? "" : Marshal.PtrToStringUTF8(nome) ?? "";
                }

                int rc = sqlite3_step(stmt);
                while (rc == Linha)
                {
                    var valores = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                    for (int c = 0; c < colunas; c++)
                        valores[nomes[c]] = ValorDaColuna(stmt, c);

                    linhas.Add(new SqliteLinha(nomes, valores));

                    rc = sqlite3_step(stmt);
                }

                // "Feito" é o fim normal da consulta. Qualquer outro código é
                // erro: a leitura parou no meio, e devolver as linhas lidas
                // até ali como se fossem o resultado inteiro seria mostrar
                // metade do histórico como se fosse o histórico todo — e o
                // reparo contaria essa metade como se fosse tudo.
                if (rc != Feito)
                {
                    _ultimoErro = MotivoDoErro(null);
                    return new List<SqliteLinha>();
                }

                _ultimoErro = "";
            }
            catch (Exception ex)
            {
                // Consulta que falha devolve lista vazia: a tela mostra o que
                // sabe, sem número errado. O motivo fica registrado, que é
                // o que separa "a tabela está vazia" de "a consulta falhou".
                _ultimoErro = MotivoDoErro(ex);
                return new List<SqliteLinha>();
            }
            finally
            {
                if (stmt != IntPtr.Zero) sqlite3_finalize(stmt);
            }
        }

        return linhas;
    }

    /// <summary>
    /// Executa tudo dentro de uma transação: ou entra tudo, ou nada entra.
    ///
    /// <para>
    /// Devolve false quando a transação não fechou — e ela sempre fecha:
    /// se o COMMIT falhar, o ROLLBACK explícito desfaz o que ficou pelo
    /// caminho. Sem ele, a transação ficava aberta e as escritas
    /// seguintes falhavam uma por uma, sem aviso: o histórico parava de
    /// crescer e ninguém sabia por quê.
    /// </para>
    /// </summary>
    public bool EmTransacao(Action<Sqlite> acao) => Transacao("BEGIN IMMEDIATE", acao);

    /// <summary>
    /// Executa tudo dentro de uma transação de LEITURA: as consultas veem
    /// o mesmo retrato do banco, mesmo que outra thread escreva nele.
    ///
    /// <para>
    /// É o que o reparo usa: contar as linhas e ler as linhas em instantes
    /// diferentes dava dois números de bancos diferentes — e o relato da
    /// perda saía errado.
    /// </para>
    /// </summary>
    public bool EmTransacaoLeitura(Action<Sqlite> acao) => Transacao("BEGIN DEFERRED", acao);

    /// <summary>
    /// Abre a transação, roda a ação e fecha. O núcleo das duas
    /// transações (de escrita e de leitura).
    /// </summary>
    private bool Transacao(string begin, Action<Sqlite> acao)
    {
        lock (_tranca)
        {
            if (_banco == IntPtr.Zero) return false;

            if (!Executar(begin)) return false;

            bool fechou = false;
            try
            {
                acao(this);
                fechou = Executar("COMMIT");
            }
            catch (Exception ex)
            {
                // A ação falhou: o que ela escreveu vai embora no
                // ROLLBACK, e o motivo fica guardado para quem chamou.
                _ultimoErro = MotivoDoErro(ex);
                fechou = false;
            }

            if (!fechou)
            {
                // O COMMIT falhou (ou a ação lançou): a transação continua
                // aberta, e as escritas seguintes iam falhar uma por uma,
                // sem aviso. O ROLLBACK explícito fecha ela.
                string motivo = _ultimoErro;
                Executar("ROLLBACK");
                _ultimoErro = motivo.Length > 0 ? motivo : "a transação não fechou";
            }

            return fechou;
        }
    }

    /// <summary>
    /// Executa o mesmo comando para muitas linhas, reaproveitando o
    /// comando preparado.
    ///
    /// <para>
    /// Preparar e finaliza um comando por linha é caro: a migração de
    /// 260 mil linhas gastava 23 segundos assim (medido). Reaproveitando o
    /// comando preparado, o mesmo trabalho é uma fração disso — e o
    /// resultado é o mesmo.
    /// </para>
    ///
    /// <para>
    /// Para no primeiro erro e devolve quantas linhas entraram: quem chama
    /// compara com o que mandou gravar e cancela tudo se não bateu.
    /// </para>
    /// </summary>
    public int ExecutarEmLote(string sql, IEnumerable<IReadOnlyList<object?>> linhas)
    {
        lock (_tranca)
        {
            if (_banco == IntPtr.Zero) return 0;

            IntPtr stmt = IntPtr.Zero;
            try
            {
                stmt = Preparar(sql, Array.Empty<object?>());
                if (stmt == IntPtr.Zero)
                {
                    _ultimoErro = MotivoDoErro(null);
                    return 0;
                }

                int entradas = 0;
                foreach (var argumentos in linhas)
                {
                    // O comando volta ao começo e larga os valores da
                    // linha anterior antes de receber os novos.
                    sqlite3_reset(stmt);
                    sqlite3_clear_bindings(stmt);

                    for (int i = 0; i < argumentos.Count; i++)
                        Ligar(stmt, i + 1, argumentos[i]);

                    int rc = sqlite3_step(stmt);
                    if (rc != Feito && rc != Linha)
                    {
                        _ultimoErro = MotivoDoErro(null);
                        return entradas;
                    }

                    entradas++;
                }

                _ultimoErro = "";
                return entradas;
            }
            catch (Exception ex)
            {
                _ultimoErro = MotivoDoErro(ex);
                return 0;
            }
            finally
            {
                if (stmt != IntPtr.Zero) sqlite3_finalize(stmt);
            }
        }
    }

    /// <summary>
    /// O motivo da última falha: a mensagem do SQLite quando foi o SQLite
    /// que falhou, a da exceção quando a falha foi do .NET, e "erro
    /// desconhecido" quando nenhuma das duas ajuda.
    ///
    /// <para>
    /// O motivo nunca é vazio: um erro sem explicação não deixa o reparo
    /// decidir se pode seguir (ler de novo é seguro, trocar o arquivo não
    /// é), e não deixa a tela dizer o que aconteceu.
    /// </para>
    /// </summary>
    private string MotivoDoErro(Exception? excecao)
    {
        if (excecao != null && excecao.Message.Length > 0) return excecao.Message;

        if (_banco != IntPtr.Zero)
        {
            IntPtr msg = sqlite3_errmsg(_banco);
            string texto = msg == IntPtr.Zero ? "" : Marshal.PtrToStringUTF8(msg) ?? "";
            if (texto.Length > 0) return texto;
        }

        return "erro desconhecido";
    }

    /// <summary>
    /// Verifica se o banco está inteiro ("ok" quando está). A rotina de
    /// reparo usa isso para decidir se precisa reconstruir.
    ///
    /// <para>
    /// Quando a verificação não roda, o motivo do SQLite volta em vez de
    /// um "não foi possível verificar" genérico: é a diferença entre "o
    /// arquivo não é um banco" (estragado) e "o banco está trancado"
    /// (temporário), e o reparo trata os dois de jeitos diferentes.
    /// </para>
    ///
    /// <para>
    /// A verificação é sempre a completa (<c>integrity_check</c>). A rápida
    /// (<c>quick_check</c>) foi descartada de propósito: ela não confere se
    /// o conteúdo dos índices bate com o das tabelas, e um banco com o
    /// índice fora de sintonia passava por "ok". Medido em 09/11/2026: o
    /// <c>quick_check</c> respondeu "ok" num banco cuja tabela tinha 40
    /// linhas e o índice <c>sqlite_autoindex_uso_1</c> apontava para uma
    /// linha que não existia mais, enquanto o <c>integrity_check</c> respondia
    /// "row 40 missing from index sqlite_autoindex_uso_1". Um "ok" falso é
    /// pior do que uma pausa de 60 ms.
    ///
    /// <para>
    /// E não há ganho de tempo em trocar: no esquema do TRANAL SENTINEL os
    /// dois levam o mesmo tempo — medido em 09/11/2026, cerca de 60 ms cada
    /// num banco com 258 mil amostras e 18,6 MB. (Um número antigo dizia que
    /// a completa "demoraria segundos": era medo sem medição, e a medição
    /// desmentiu.) Uma só verificação, sem caminho alternativo: o estado do
    /// banco na tela, o relatório da linha de comando e o reparo falam do
    /// mesmo arquivo do mesmo jeito.
    /// </para>
    /// </summary>
    public string VerificarIntegridade()
    {
        if (_banco == IntPtr.Zero) return "banco fechado";

        var linhas = Consultar("PRAGMA integrity_check");
        if (linhas.Count == 0)
        {
            string erro = UltimoErro;
            return erro.Length > 0 ? erro : "não foi possível verificar";
        }

        string coluna = linhas[0].Colunas.Count > 0 ? linhas[0].Colunas[0] : "integrity_check";
        string texto = linhas[0].Texto(coluna);
        return texto.Length == 0 ? "não foi possível verificar" : texto;
    }

    // ------------------------------------------------------------------ interno

    private IntPtr Preparar(string sql, IReadOnlyList<object?> argumentos)
    {
        IntPtr sqlUtf8 = IntPtr.Zero;
        IntPtr stmt = IntPtr.Zero;

        try
        {
            sqlUtf8 = Marshal.StringToCoTaskMemUTF8(sql);
            int rc = sqlite3_prepare_v2(_banco, sqlUtf8, -1, out stmt, out IntPtr fim);
            if (rc != Ok) return IntPtr.Zero;

            for (int i = 0; i < argumentos.Count; i++)
                Ligar(stmt, i + 1, argumentos[i]);

            return stmt;
        }
        finally
        {
            // O SQLite copiou o SQL no prepare: o buffer pode ir embora.
            if (sqlUtf8 != IntPtr.Zero) Marshal.FreeCoTaskMem(sqlUtf8);
        }
    }

    private void Ligar(IntPtr stmt, int indice, object? valor)
    {
        switch (valor)
        {
            case null:
                sqlite3_bind_null(stmt, indice);
                break;

            case string texto:
                {
                    IntPtr utf8 = Marshal.StringToCoTaskMemUTF8(texto);
                    try
                    {
                        // Transitorio: o SQLite copia o texto agora, então o
                        // buffer pode ser liberado logo em seguida.
                        sqlite3_bind_text(stmt, indice, utf8, -1, Transitorio);
                    }
                    finally
                    {
                        Marshal.FreeCoTaskMem(utf8);
                    }
                }
                break;

            case bool booleano:
                sqlite3_bind_int64(stmt, indice, booleano ? 1 : 0);
                break;

            case int numero:
                sqlite3_bind_int64(stmt, indice, numero);
                break;

            case long numero:
                sqlite3_bind_int64(stmt, indice, numero);
                break;

            case double numero:
                sqlite3_bind_double(stmt, indice, numero);
                break;

            case float numero:
                sqlite3_bind_double(stmt, indice, numero);
                break;

            case DateTime instante:
                sqlite3_bind_int64(stmt, indice, MomentoParaUnix(instante));
                break;

            default:
                {
                    IntPtr utf8 = Marshal.StringToCoTaskMemUTF8(valor.ToString() ?? "");
                    try
                    {
                        sqlite3_bind_text(stmt, indice, utf8, -1, Transitorio);
                    }
                    finally
                    {
                        Marshal.FreeCoTaskMem(utf8);
                    }
                }
                break;
        }
    }

    private static object? ValorDaColuna(IntPtr stmt, int indice)
    {
        // O SQLite guarda o tipo de cada valor; o app lê pelo tipo, para
        // número virar número e não texto.
        switch (sqlite3_column_type(stmt, indice))
        {
            case TipoInteiro:
                return sqlite3_column_int64(stmt, indice);

            case TipoReal:
                return sqlite3_column_double(stmt, indice);

            case TipoNulo:
                return null;

            default:
                IntPtr texto = sqlite3_column_text(stmt, indice);
                if (texto == IntPtr.Zero) return null;
                return Marshal.PtrToStringUTF8(texto) ?? "";
        }
    }

    /// <summary>
    /// Converte um instante local para epoch em milissegundos.
    ///
    /// <para>
    /// As pontas do dia (<see cref="DateTime.MinValue"/> e
    /// <see cref="DateTime.MaxValue"/}) viram o menor e o maior número: são
    /// elas que o SQL usa como "sem limite", e elas não cabem em epoch.
    /// </para>
    /// </summary>
    public static long MomentoParaUnix(DateTime instante)
    {
        if (instante <= DateTime.MinValue.AddDays(1)) return long.MinValue;
        if (instante >= DateTime.MaxValue.AddDays(-1)) return long.MaxValue;

        return new DateTimeOffset(instante.ToUniversalTime(), TimeSpan.Zero).ToUnixTimeMilliseconds();
    }

    /// <summary>Converte epoch em milissegundos de volta para hora local.</summary>
    public static DateTime UnixParaMomento(long epoch) =>
        DateTimeOffset.FromUnixTimeMilliseconds(epoch).LocalDateTime;
}
