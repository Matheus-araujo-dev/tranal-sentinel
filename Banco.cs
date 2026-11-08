using System.Globalization;
using System.IO;

namespace TranalSentinel;

/// <summary>
/// O banco de dados do TRANAL SENTINEL: onde o histórico mora a partir da
/// v0.28.
///
/// <para>
/// Antes, o histórico (os sensores e o tempo de tela) eram arquivos CSV
/// appendados. Isso servia para começar, mas não permite consultar por
/// período sem varrer o arquivo inteiro, não sabe dizer se o arquivo está
/// estragado, e não tem como se recuperar. Este banco resolve os três: a
/// consulta vai direto ao que interessa, a verificação de integridade diz se
/// algo quebrou, e a rotina de reparo reconstrói o que der para ler.
///
/// <para>
/// O banco é <b>a</b> o lugar do histórico: os CSV antigos são lidos uma vez
/// só, na migração, e ficam no disco como cópia — o app não grava mais CSV.
///
/// <para>
/// O SQLite vem do próprio Windows (<c>System32\winsqlite3.dll</c>), acessado
/// por P/Invoke em <see cref="Sqlite"/>: o app não ganha dependência nova.
///
/// <para>
/// Regra da casa: o app nunca fica sem funcionar por causa do banco. Se ele
/// não abre, a coleta continua e a tela avisa que o histórico não está
/// disponível — nenhum número inventado, nenhum histórico mentido.
/// </para>
/// </summary>
public static class Banco
{
    /// <summary>O nome do arquivo do banco.</summary>
    public const string NomeArquivo = "historico.db";

    /// <summary>Quantos dias de amostras de sensor ficam no banco.</summary>
    public const int DiasRetidosAmostras = 15;

    /// <summary>Quantos dias de registro de uso ficam no banco.</summary>
    public const int DiasRetidosUso = 30;

    /// <summary>A versão do esquema (1 = o esquema atual).</summary>
    private const int VersaoEsquema = 1;

    private static readonly object Tranca = new();

    private static Sqlite? _instancia;
    private static string _caminhoInstancia = "";
    private static DateTime _ultimaTentativa = DateTime.MinValue;
    private static DateTime _ultimaPoda = DateTime.MinValue;
    private static string _estado = "";

    /// <summary>
    /// Troca a pasta do banco. Só os testes usam isto: eles não podem
    /// escrever no <c>%LOCALAPPDATA%</c> do usuário. Vazio = a pasta de verdade.
    /// </summary>
    internal static string? PastaOverride;

    /// <summary>
    /// A pasta do banco. Fica no <c>%LOCALAPPDATA%</c> — o histórico é
    /// volumoso e não tem por que viajar com o perfil do Windows.
    /// </summary>
    public static string Pasta => PastaOverride ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TranalSentinel", "historico");

    /// <summary>O caminho do arquivo do banco.</summary>
    public static string Caminho => Path.Combine(Pasta, NomeArquivo);

    /// <summary>
    /// O banco aberto (null quando não deu para abrir). A instância é
    /// única: o app abre o banco de vários lugares (a coleta escreve, a tela
    /// lê) e o SQLite em modo WAL resolve a concorrência.
    /// </summary>
    public static Sqlite? Instancia
    {
        get
        {
            lock (Tranca)
            {
                // O caminho mudou (a pasta do banco foi apontada para outro
                // lugar): a instância aberta é de outro arquivo, e usá-la
                // seria ler e escrever no banco errado.
                if (_instancia != null && _caminhoInstancia != Caminho)
                {
                    _instancia.Dispose();
                    _instancia = null;
                    _caminhoInstancia = "";
                }

                if (_instancia != null) return _instancia;

                // Uma falha na abertura não pode virar uma tentativa a cada
                // 5 segundos para sempre.
                if (DateTime.Now - _ultimaTentativa < TimeSpan.FromSeconds(30))
                    return null;

                _ultimaTentativa = DateTime.Now;
                _instancia = Abrir();
                _caminhoInstancia = _instancia != null ? Caminho : "";

                if (_instancia == null && _estado.Length == 0)
                    _estado = "Não foi possível abrir o banco de dados.";

                return _instancia;
            }
        }
    }

    /// <summary>
    /// O que o app sabe sobre o banco ("ok" quando está tudo bem). É o que
    /// a tela mostra e o que a rotina de reparo conserta.
    ///
    /// <para>
    /// A verificação aqui é a rápida (<c>quick_check</c>): o retrato do
    /// banco aparece na tela e não pode custar segundos. A completa fica
    /// para o botão de verificar e para o reparo.
    /// </para>
    /// </summary>
    public static string Estado => EstadoDaIntegridade(completo: false);

    /// <summary>
    /// O estado do banco com a verificação de integridade escolhida: a
    /// completa (percorre o arquivo inteiro) ou a rápida.
    /// </summary>
    private static string EstadoDaIntegridade(bool completo)
    {
        // A leitura acontece com a trava do banco: o reparo fecha a
        // instância e troca o arquivo, e uma verificação fora da trava
        // leria um arquivo que está sendo trocado por baixo.
        string estado = EmBanco(b =>
        {
            string integridade = b.VerificarIntegridade(completo);
            return integridade == "ok"
                ? "ok"
                : "O banco de dados precisa de reparo: " + TraduzirIntegridade(integridade);
        }, "");

        if (estado.Length > 0) return estado;

        // Sem instância: ou não há banco, ou o arquivo não abriu.
        if (File.Exists(Caminho))
            return "O banco de dados precisa de reparo: o arquivo não pôde ser aberto.";

        return _estado.Length > 0 ? _estado : "O banco de dados não está disponível.";
    }

    /// <summary>
    /// O retrato do banco para a tela: o estado, o tamanho, quantas linhas
    /// tem e onde fica a cópia avariada (quando existe).
    /// </summary>
    /// <param name="Estado">"ok", ou o motivo pelo qual o banco não está bem.</param>
    /// <param name="TamanhoBytes">O tamanho do arquivo do banco.</param>
    /// <param name="Amostras">Quantas amostras de sensor estão gravadas.</param>
    /// <param name="Uso">Quantos registros de uso estão gravados.</param>
    /// <param name="Caminho">Onde o banco está.</param>
    /// <param name="CopiaAvariada">A cópia avariada mais recente (null quando não há).</param>
    public sealed record Diagnostico(
        string Estado,
        long TamanhoBytes,
        long Amostras,
        long Uso,
        string Caminho,
        string? CopiaAvariada);

    /// <summary>
    /// Junta o retrato do banco. Nunca lança: a tela do TRANAL SAÚDE mostra
    /// o que deu, e nunca um número inventado.
    /// </summary>
    /// <param name="completo">
    /// True para a verificação de integridade completa (percorre o arquivo
    /// inteiro). O retrato automático usa a rápida, que é o bastante para
    /// dizer se o banco está de pé.
    /// </param>
    public static Diagnostico Ver(bool completo = false)
    {
        try
        {
            var (amostras, uso) = Contagem();
            return new Diagnostico(
                EstadoDaIntegridade(completo),
                Tamanho,
                amostras,
                uso,
                Caminho,
                CopiaAvariadaRecente());
        }
        catch
        {
            // O retrato é informativo: se ele falhar, a tela mostra que o
            // banco não pôde ser lido.
            return new Diagnostico(
                "O banco de dados não pôde ser lido.",
                0, 0, 0, Caminho, null);
        }
    }

    /// <summary>
    /// Abre (ou cria) o banco, cria o esquema e migra o que for preciso.
    /// </summary>
    private static Sqlite? Abrir()
    {
        try
        {
            Directory.CreateDirectory(Pasta);
        }
        catch
        {
            // Pasta que não pode ser criada: sem banco, sem histórico. O app
            // continua funcionando (e diz que o banco não está disponível).
            _estado = "Não foi possível criar a pasta do banco de dados.";
            return null;
        }

        var banco = Sqlite.Abrir(Caminho);
        if (banco == null)
        {
            _estado = "Não foi possível abrir o banco de dados.";
            return null;
        }

        // WAL: a coleta escreve enquanto a tela lê, sem se atrapalhar.
        banco.Executar("PRAGMA journal_mode=WAL");
        banco.Executar("PRAGMA synchronous=NORMAL");

        if (!CriarEsquema(banco))
        {
            _estado = "Não foi possível criar o esquema do banco de dados.";
            banco.Dispose();
            return null;
        }

        Migrar(banco);
        _estado = "ok";

        return banco;
    }

    /// <summary>Cria as tabelas (se ainda não existirem).</summary>
    private static bool CriarEsquema(Sqlite banco)
    {
        bool ok = banco.Executar(@"
            CREATE TABLE IF NOT EXISTS amostras (
                momento       INTEGER PRIMARY KEY,
                cpu_temp      REAL,
                cpu_carga     REAL,
                cpu_consumo   REAL,
                cpu_clock     REAL,
                gpu_temp      REAL,
                gpu_carga     REAL,
                gpu_clock     REAL,
                gpu_vram      REAL,
                ram_uso       REAL,
                disco_leitura REAL,
                disco_escrita REAL
            )");

        ok &= banco.Executar(@"
            CREATE TABLE IF NOT EXISTS uso (
                id       INTEGER PRIMARY KEY AUTOINCREMENT,
                momento INTEGER NOT NULL,
                processo TEXT NOT NULL,
                titulo   TEXT NOT NULL,
                UNIQUE (momento, processo, titulo)
            )");

        ok &= banco.Executar("CREATE INDEX IF NOT EXISTS ix_uso_momento ON uso (momento)");

        return ok;
    }

    // ------------------------------------------------------------------ amostras

    /// <summary>
    /// Grava uma amostra de sensor. Devolve false quando não deu para gravar
    /// (banco indisponível) — a coleta não pode derrubar o app por causa
    /// disso.
    /// </summary>
    public static bool GravarAmostra(AmostraSensor amostra)
    {
        bool ok = EmBanco(b => b.Executar(
            @"INSERT OR REPLACE INTO amostras
              (momento, cpu_temp, cpu_carga, cpu_consumo, cpu_clock,
               gpu_temp, gpu_carga, gpu_clock, gpu_vram, ram_uso,
               disco_leitura, disco_escrita)
              VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
            Sqlite.MomentoParaUnix(amostra.Momento),
            amostra.CpuTemp, amostra.CpuCarga, amostra.CpuConsumo, amostra.CpuClock,
            amostra.GpuTemp, amostra.GpuCarga, amostra.GpuClock, amostra.GpuVram,
            amostra.RamUso, amostra.DiscoLeitura, amostra.DiscoEscrita), false);

        if (ok) PodarSeEstourou();
        return ok;
    }

    /// <summary>
    /// Lê as amostras gravadas entre <paramref name="desde"/> e
    /// <paramref name="ate"/> (ambos inclusivos), em ordem cronológica.
    ///
    /// <para>
    /// A consulta vai ao índice do instante: não é preciso varrer o
    /// histórico inteiro para saber o que aconteceu na janela pedida.
    /// </para>
    /// </summary>
    public static List<AmostraSensor> LerAmostras(DateTime desde, DateTime ate)
    {
        return EmBanco(b =>
        {
            var resultado = new List<AmostraSensor>();

            var linhas = b.Consultar(
                @"SELECT momento, cpu_temp, cpu_carga, cpu_consumo, cpu_clock,
                         gpu_temp, gpu_carga, gpu_clock, gpu_vram, ram_uso,
                         disco_leitura, disco_escrita
                  FROM amostras
                  WHERE momento >= ? AND momento <= ?
                  ORDER BY momento",
                Sqlite.MomentoParaUnix(desde),
                Sqlite.MomentoParaUnix(ate));

            foreach (var linha in linhas)
            {
                resultado.Add(new AmostraSensor(
                    linha.Momento("momento"),
                    Real(linha, "cpu_temp"),
                    Real(linha, "cpu_carga"),
                    Real(linha, "cpu_consumo"),
                    Real(linha, "cpu_clock"),
                    Real(linha, "gpu_temp"),
                    Real(linha, "gpu_carga"),
                    Real(linha, "gpu_clock"),
                    Real(linha, "gpu_vram"),
                    Real(linha, "ram_uso"),
                    Real(linha, "disco_leitura"),
                    Real(linha, "disco_escrita")));
            }

            return resultado;
        }, new List<AmostraSensor>());
    }

    /// <summary>
    /// O valor de uma coluna real, ou null quando o sensor não existia
    /// (a coluna é nula). Zero é um valor válido e não pode servir de
    /// "não li".
    /// </summary>
    private static float? Real(SqliteLinha linha, string coluna)
    {
        double? valor = linha.RealNulo(coluna);
        return valor is null ? null : (float)valor.Value;
    }

    // ------------------------------------------------------------------ uso

    /// <summary>
    /// Grava uma troca de programa. Devolve true quando a linha entrou no
    /// banco — o amostrador usa isso para não dar a troca por registrada
    /// quando ela não foi (senão o programa anterior ficaria com o tempo
    /// dela).
    /// </summary>
    public static bool GravarUso(TrocaUso troca)
    {
        long momento = Sqlite.MomentoParaUnix(troca.Momento);
        string processo = Higienizar(troca.Processo);
        string titulo = Higienizar(troca.Titulo);

        bool ok = EmBanco(b =>
        {
            if (!b.Executar(
                    "INSERT OR IGNORE INTO uso (momento, processo, titulo) VALUES (?, ?, ?)",
                    momento, processo, titulo))
                return false;

            // O INSERT OR IGNORE pode ignorar a linha (a mesma troca
            // repetida): nesse caso a troca JÁ está registrada, e o
            // amostrador não precisa ficar tentando de novo (e adiar a
            // troca seguinte).
            if (b.Alteracoes > 0) return true;

            var existe = b.Consultar(
                "SELECT 1 AS ok FROM uso WHERE momento = ? AND processo = ? AND titulo = ?",
                momento, processo, titulo);

            return existe.Count > 0;
        }, false);

        if (ok) PodarSeEstourou();
        return ok;
    }

    /// <summary>
    /// Lê as trocas de programa de um dia, em ordem cronológica.
    /// </summary>
    public static List<TrocaUso> LerUso(DateTime dia)
    {
        DateTime inicioDia = dia.Date;
        DateTime fimDia = inicioDia >= DateTime.MaxValue.Date ? DateTime.MaxValue : inicioDia.AddDays(1);

        long inicio = Sqlite.MomentoParaUnix(inicioDia);
        long fim = Sqlite.MomentoParaUnix(fimDia);

        return EmBanco(b =>
        {
            var resultado = new List<TrocaUso>();

            var linhas = b.Consultar(
                "SELECT momento, processo, titulo FROM uso WHERE momento >= ? AND momento < ? ORDER BY momento, id",
                inicio, fim);

            foreach (var linha in linhas)
                resultado.Add(new TrocaUso(linha.Momento("momento"), linha.Texto("processo"), linha.Texto("titulo")));

            return resultado;
        }, new List<TrocaUso>());
    }

    /// <summary>
    /// Lê as trocas de um dia <b>e a última troca do dia anterior</b>.
    ///
    /// <para>
    /// A última troca de ontem é a que atravessa a meia-noite: o programa
    /// que estava em frente às 23:50 continua em frente às 00:10. Sem ela,
    /// o dia inteiro ficaria sem dono quando o app rodou a noite toda sem
    /// trocar de programa.
    /// </para>
    /// </summary>
    public static List<TrocaUso> LerUsoComAnterior(DateTime dia)
    {
        var trocas = LerUso(dia);

        DateTime diaAnterior = dia.Date <= DateTime.MinValue.Date ? DateTime.MinValue : dia.Date.AddDays(-1);
        var anteriores = LerUso(diaAnterior);
        if (anteriores.Count > 0)
        {
            trocas.Add(anteriores[^1]);

            // A troca que atravessa a meia-noite é mais antiga que as do
            // dia: a lista sai em ordem cronológica, senão a sessão que
            // atravessa a virada do dia nasce torta.
            trocas.Sort((a, b) => a.Momento.CompareTo(b.Momento));
        }

        return trocas;
    }

    /// <summary>
    /// As trocas de vários dias, em ordem cronológica.
    ///
    /// <para>
    /// O primeiro dia (o mais antigo) traz também a última troca do dia
    /// anterior: é a que atravessa a meia-noite, e sem ela um período que
    /// começa de madrugada não sabe dizer quem estava em frente.
    /// </para>
    /// </summary>
    public static List<TrocaUso> LerUsoVarios(IEnumerable<DateTime> dias)
    {
        var trocas = new List<TrocaUso>();
        var lista = dias.OrderBy(d => d).ToList();

        for (int i = 0; i < lista.Count; i++)
        {
            trocas.AddRange(i == 0
                ? LerUsoComAnterior(lista[i])
                : LerUso(lista[i]));
        }

        trocas.Sort((a, b) => a.Momento.CompareTo(b.Momento));
        return trocas;
    }

    /// <summary>Lista os dias que têm histórico, do mais novo para o mais antigo.</summary>
    public static List<DateTime> DiasComUso()
    {
        // A leitura inteira (inclusive as contagens) acontece com a trava do
        // banco: o reparo pode trocar o arquivo no meio, e ler um arquivo
        // que está sendo trocado dá número errado na tela.
        return EmBanco(b =>
        {
            var dias = new List<DateTime>();

            var limites = b.Consultar("SELECT MIN(momento) AS primeiro, MAX(momento) AS ultimo FROM uso");
            if (limites.Count == 0) return dias;

            long primeiro = limites[0].Inteiro("primeiro");
            long ultimo = limites[0].Inteiro("ultimo");
            if (primeiro <= 0 || ultimo <= 0) return dias;

            DateTime dia = Sqlite.UnixParaMomento(primeiro).Date;
            DateTime fim = Sqlite.UnixParaMomento(ultimo).Date;

            // O dia local é uma faixa de instantes — e a faixa muda no
            // horário de verão. Por isso a conta é feita aqui, com a data
            // local do .NET, e não no SQLite (lá o 'localtime' depois do
            // 'unixepoch' não devolve nada na biblioteca do Windows).
            //
            // O limite de dias conferidos é uma rede de segurança: a poda
            // mantém 30 dias de uso, então a lista é curta.
            int conferidos = 0;
            while (dia <= fim && conferidos < 800)
            {
                long inicio = Sqlite.MomentoParaUnix(dia);
                long fimDia = Sqlite.MomentoParaUnix(dia.AddDays(1));

                var existe = b.Consultar(
                    "SELECT 1 AS dia FROM uso WHERE momento >= ? AND momento < ? LIMIT 1",
                    inicio, fimDia);

                if (existe.Count > 0) dias.Add(dia);

                dia = dia.AddDays(1);
                conferidos++;
            }

            // Do mais novo para o mais antigo (a lista da tela começa por hoje).
            dias.Sort((a, b) => b.CompareTo(a));
            return dias;
        }, new List<DateTime>());
    }

    /// <summary>
    /// Higieniza o texto antes de gravar: separador e quebra de linha viram
    /// espaço, e o texto é cortado. É a mesma regra do CSV antigo — assim
    /// o histórico não muda de formato quando migra.
    /// </summary>
    private static string Higienizar(string? texto)
    {
        if (string.IsNullOrEmpty(texto)) return "";

        string limpo = texto.Replace(';', ' ').Replace('\r', ' ').Replace('\n', ' ').Trim();
        return limpo.Length > 200 ? limpo[..200] : limpo;
    }

    // ------------------------------------------------------------------ poda

    /// <summary>
    /// Apaga do banco o que é mais velho que o prazo, nas duas tabelas.
    ///
    /// <para>
    /// Cada tabela tem o seu prazo (as amostras ficam 15 dias, o uso fica
    /// 30). O método existe para quem quer cortar os dois de uma vez; a
    /// poda automática usa os prazos separados, porque cortar o histórico
    /// de uso aos 15 dias seria perder dado que a regra promete guardar.
    /// </para>
    /// </summary>
    public static int ApagarAntigos(DateTime antesDe) =>
        ApagarAmostrasAntigas(antesDe) + ApagarUsosAntigos(antesDe);

    /// <summary>Apaga as amostras de sensor mais velhas que o prazo.</summary>
    public static int ApagarAmostrasAntigas(DateTime antesDe)
    {
        long limite = Sqlite.MomentoParaUnix(antesDe);

        return EmBanco(b => b.Executar("DELETE FROM amostras WHERE momento < ?", limite)
            ? b.Alteracoes : 0, 0);
    }

    /// <summary>Apaga os registros de uso mais velhos que o prazo.</summary>
    public static int ApagarUsosAntigos(DateTime antesDe)
    {
        long limite = Sqlite.MomentoParaUnix(antesDe);

        return EmBanco(b => b.Executar("DELETE FROM uso WHERE momento < ?", limite)
            ? b.Alteracoes : 0, 0);
    }

    /// <summary>
    /// Corta o histórico velho, no máximo uma vez por hora. A poda é uma
    /// escrita no banco, e a coleta acontece a cada 5 segundos: fazer isso a
    /// cada amostra só gastaria I/O.
    ///
    /// <para>
    /// Cada tabela é cortada com o seu prazo: as amostras ficam 15 dias e o
    /// histórico de uso fica 30. Cortar o uso junto com as amostras
    /// perderia meio mês de dado que a regra promete guardar.
    /// </para>
    /// </summary>
    private static void PodarSeEstourou()
    {
        lock (Tranca)
        {
            if (DateTime.Now - _ultimaPoda < TimeSpan.FromHours(1)) return;
            _ultimaPoda = DateTime.Now;
        }

        ApagarAmostrasAntigas(DateTime.Now.AddDays(-DiasRetidosAmostras));
        ApagarUsosAntigos(DateTime.Now.AddDays(-DiasRetidosUso));
    }

    // ------------------------------------------------------------------ contagem e tamanho

    /// <summary>Quantas linhas tem cada tabela (para o diagnóstico).</summary>
    public static (long Amostras, long Uso) Contagem()
    {
        return EmBanco(b => (Contar(b, "SELECT COUNT(*) AS total FROM amostras"),
                             Contar(b, "SELECT COUNT(*) AS total FROM uso")), (0L, 0L));
    }

    private static long Contar(Sqlite banco, string sql)
    {
        var linhas = banco.Consultar(sql);
        if (linhas.Count == 0) return 0;

        string coluna = linhas[0].Colunas.Count > 0 ? linhas[0].Colunas[0] : "total";
        return linhas[0].Inteiro(coluna);
    }

    /// <summary>
    /// O tamanho do banco no disco, em bytes (0 quando não existe). Para
    /// a tela mostrar quanto o histórico ocupa.
    ///
    /// <para>
    /// A conta inclui o arquivo de WAL (<c>-wal</c>): em modo WAL os dados
    /// mais recentes ficam nele até o checkpoint, e mostrar só o arquivo
    /// principal diria que o banco está vazio quando não está.
    /// </para>
    /// </summary>
    public static long Tamanho
    {
        get
        {
            try
            {
                long total = 0;

                var info = new FileInfo(Caminho);
                if (info.Exists) total += info.Length;

                var wal = new FileInfo(Caminho + "-wal");
                if (wal.Exists) total += wal.Length;

                return total;
            }
            catch
            {
                return 0;
            }
        }
    }

    /// <summary>
    /// Apaga tudo do banco (as duas tabelas). Usado pelos testes — e pelo
    /// que precisar de um banco limpo.
    /// </summary>
    public static bool Limpar()
    {
        return EmBanco(b => b.EmTransacao(t =>
        {
            t.Executar("DELETE FROM amostras");
            t.Executar("DELETE FROM uso");
            t.Executar("DELETE FROM sqlite_sequence WHERE name = 'uso'");
        }), false);
    }

    // ------------------------------------------------------------------ migração

    /// <summary>
    /// Importa o que ainda está em CSV (uma vez só). Os CSV não são
    /// apagados: o arquivo antigo fica como cópia de segurança.
    ///
    /// <para>
    /// A importação é <b>uma transação só</b> e o <c>user_version</c> só é
    /// gravado quando ela fecha bem. Gravar a versão com a importação
    /// pela metade faria o app nunca mais tentar importar o CSV — e o
    /// histórico antigo ficaria órfão para sempre, sem aviso.
    /// </para>
    /// </summary>
    private static void Migrar(Sqlite banco)
    {
        try
        {
            int versao = VersaoDoEsquema(banco);
            if (versao >= VersaoEsquema) return;

            var amostras = LegadoCsv.LerAmostras();
            var trocas = LegadoCsv.LerTrocas();

            if (amostras.Count > 0 || trocas.Count > 0)
            {
                bool entrouTudo = true;

                bool fechou = banco.EmTransacao(b =>
                {
                    foreach (var amostra in amostras)
                    {
                        if (!b.Executar(
                            @"INSERT OR REPLACE INTO amostras
                              (momento, cpu_temp, cpu_carga, cpu_consumo, cpu_clock,
                               gpu_temp, gpu_carga, gpu_clock, gpu_vram, ram_uso,
                               disco_leitura, disco_escrita)
                              VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
                            Sqlite.MomentoParaUnix(amostra.Momento),
                            amostra.CpuTemp, amostra.CpuCarga, amostra.CpuConsumo, amostra.CpuClock,
                            amostra.GpuTemp, amostra.GpuCarga, amostra.GpuClock, amostra.GpuVram,
                            amostra.RamUso, amostra.DiscoLeitura, amostra.DiscoEscrita))
                            entrouTudo = false;
                    }

                    foreach (var troca in trocas)
                    {
                        if (!b.Executar(
                            "INSERT OR IGNORE INTO uso (momento, processo, titulo) VALUES (?, ?, ?)",
                            Sqlite.MomentoParaUnix(troca.Momento),
                            Higienizar(troca.Processo),
                            Higienizar(troca.Titulo)))
                            entrouTudo = false;
                    }
                });

                if (!fechou || !entrouTudo)
                {
                    // A transação foi desfeita: o banco continua vazio e a
                    // migração é tentada de novo na próxima abertura (a
                    // versão do esquema continua atrás).
                    _estado = "Não foi possível migrar o histórico antigo para o banco de dados.";
                    return;
                }
            }

            banco.Executar($"PRAGMA user_version = {VersaoEsquema}");
        }
        catch
        {
            // Migrar que falha não impede o app de funcionar: o banco fica
            // vazio e o histórico novo começa a ser gravado nele. A migração
            // é tentada de novo na próxima abertura (a versão do esquema
            // continua atrás).
            _estado = "Não foi possível migrar o histórico antigo para o banco de dados.";
        }
    }

    private static int VersaoDoEsquema(Sqlite banco)
    {
        var linhas = banco.Consultar("PRAGMA user_version");
        if (linhas.Count == 0) return 0;

        string coluna = linhas[0].Colunas.Count > 0 ? linhas[0].Colunas[0] : "user_version";
        return (int)linhas[0].Inteiro(coluna);
    }


    // ------------------------------------------------------------------ reparo

    /// <summary>
    /// O resultado de uma tentativa de reparo: o que foi recuperado e o que
    /// não deu para salvar.
    /// </summary>
    /// <param name="Sucesso">True quando o banco ficou íntegro depois.</param>
    /// <param name="Relato">O que aconteceu, em português, para a tela mostrar.</param>
    /// <param name="AmostrasRecuperadas">Quantas amostras de sensor foram salvas.</param>
    /// <param name="UsoRecuperado">Quantos registros de uso foram salvos.</param>
    /// <param name="Copia">A cópia do banco estragado (null quando não houve).</param>
    public sealed record ResultadoReparo(
        bool Sucesso,
        string Relato,
        int AmostrasRecuperadas,
        int UsoRecuperado,
        string? Copia);

    /// <summary>
    /// Verifica o banco e, se ele estiver estragado, reconstrói com o que
    /// der para ler.
    ///
    /// <para>
    /// A reconstrução cria um banco novo, copia as linhas legíveis e troca
    /// o arquivo no lugar. O banco velho <b>não é apagado</b>: ele fica
    /// datado na pasta, como cópia — é o desfazer. O que não pôde ser lido
    /// é perdido, e o relato diz isso, porque perder em silêncio é o que o
    /// app não faz.
    /// </para>
    /// </summary>
    public static ResultadoReparo Reparar()
    {
        lock (Tranca)
        {
            string integridade;
            var amostras = new List<object?[]>();
            var usos = new List<object?[]>();

            // O reparo é uma ação pedida pelo usuário: ele abre o banco na
            // hora, sem a espera da retentativa que o app usa quando o
            // banco falha sozinho.
            var banco = _instancia;
            bool abertoAgora = false;

            if (banco == null)
            {
                if (!File.Exists(Caminho))
                    return new ResultadoReparo(false, "Não há banco de dados para reparar.", 0, 0, null);

                banco = Sqlite.Abrir(Caminho);
                abertoAgora = banco != null;

                if (banco == null)
                {
                    // Não deu para abrir o arquivo: o reparo não pode
                    // trocar um banco que ele não leu.
                    return new ResultadoReparo(false,
                        "Não foi possível abrir o banco de dados, e por isso o reparo foi cancelado: o arquivo foi mantido como está.",
                        0, 0, null);
                }
            }

            integridade = banco.VerificarIntegridade(completo: true);
            if (integridade == "ok")
            {
                if (abertoAgora) banco.Dispose();
                return new ResultadoReparo(true, "O banco de dados está íntegro. Nada precisou ser reparado.", 0, 0, null);
            }

            // Lê o que der para ler. Num banco estragado a consulta pode
            // vir vazia — e aí a reconstrução produz um banco novo e
            // honesto, e o velho fica guardado como cópia.
            amostras = LerLinhas(banco,
                "SELECT momento, cpu_temp, cpu_carga, cpu_consumo, cpu_clock, gpu_temp, gpu_carga, gpu_clock, gpu_vram, ram_uso, disco_leitura, disco_escrita FROM amostras");
            usos = LerLinhas(banco, "SELECT momento, processo, titulo FROM uso");

            // A contagem diz quantas linhas o banco tem; a leitura diz
            // quantas vieram. Se a leitura falhou (ou veio pela metade), o
            // reparo não pode trocar o arquivo achando que leu tudo — e o
            // que faltar tem que aparecer no relato.
            long totalAmostras = ContarParaReparo(banco, "SELECT COUNT(*) AS total FROM amostras");
            long totalUso = ContarParaReparo(banco, "SELECT COUNT(*) AS total FROM uso");

            bool leituraFalhou = totalAmostras < 0 || totalUso < 0;

            // O arquivo que nem é um banco não tem nada a ler: reconstruí-lo
            // é o único caminho. Qualquer outro problema (banco trancado,
            // banco em uso) cancela o reparo, porque trocar o arquivo sem
            // ler é trocar histórico por vazio.
            bool arquivoNaoEBanco = ArquivoEstragado(integridade);

            if (leituraFalhou && !arquivoNaoEBanco)
            {
                banco.Dispose();
                _instancia = null;
                _ultimaTentativa = DateTime.MinValue;

                return new ResultadoReparo(false,
                    "Não foi possível ler o banco de dados, e por isso o reparo foi cancelado: o arquivo foi mantido como está.",
                    0, 0, null);
            }

            banco.Dispose();
            _instancia = null;
            _ultimaTentativa = DateTime.MinValue;

            return Reconstruir(amostras, usos, integridade, totalAmostras, totalUso);
        }
    }

    /// <summary>
    /// Conta as linhas de uma tabela. Devolve -1 quando a consulta falhou
    /// (a tabela vazia devolve 0 — as duas coisas precisam ser diferentes).
    /// </summary>
    private static long ContarParaReparo(Sqlite banco, string sql)
    {
        var linhas = banco.Consultar(sql);
        if (linhas.Count == 0) return banco.UltimoErro.Length > 0 ? -1 : 0;

        string coluna = linhas[0].Colunas.Count > 0 ? linhas[0].Colunas[0] : "total";
        return linhas[0].Inteiro(coluna);
    }

    /// <summary>
    /// True quando o motivo do SQLite diz que o arquivo em si está
    /// estragado (e não que ele estava trancado ou busy). É o que separa
    /// "reconstruir" de "cancelar e manter o arquivo".
    /// </summary>
    private static bool ArquivoEstragado(string integridade)
    {
        string mensagem = integridade.ToLowerInvariant();
        return mensagem.Contains("not a database")
            || mensagem.Contains("malformed")
            || mensagem.Contains("corrupt")
            || mensagem.Contains("encrypted");
    }

    /// <summary>
    /// Traduz o motivo técnico do SQLite para uma frase que o usuário
    /// entende — o detalhe técnico continua no texto, para quem quiser.
    /// </summary>
    private static string TraduzirIntegridade(string mensagem)
    {
        string texto = mensagem.ToLowerInvariant();

        if (texto.Contains("not a database"))
            return "o arquivo não é um banco de dados válido (" + mensagem + ")";
        if (texto.Contains("malformed"))
            return "o arquivo do banco está malformado (" + mensagem + ")";
        if (texto.Contains("locked"))
            return "o banco está em uso por outro programa (" + mensagem + ")";
        if (texto.Contains("busy"))
            return "o banco está ocupado (" + mensagem + ")";

        return mensagem;
    }

    /// <summary>
    /// Lê uma consulta como linhas de valores crus (na ordem das colunas),
    /// para a reconstrução do banco.
    /// </summary>
    private static List<object?[]> LerLinhas(Sqlite banco, string sql)
    {
        var linhas = new List<object?[]>();

        foreach (var linha in banco.Consultar(sql))
        {
            var valores = new object?[linha.Colunas.Count];
            for (int c = 0; c < linha.Colunas.Count; c++)
                valores[c] = linha.Valor(linha.Colunas[c]);
            linhas.Add(valores);
        }

        return linhas;
    }

    /// <summary>
    /// Cria um banco novo e copia o que foi lido do velho. Devolve o
    /// relato do que se salvou.
    /// </summary>
    private static ResultadoReparo Reconstruir(List<object?[]> amostras, List<object?[]> usos, string integridade, long totalAmostras, long totalUso)
    {
        string novoCaminho = Caminho + ".reparo";

        try
        {
            if (File.Exists(novoCaminho)) File.Delete(novoCaminho);
        }
        catch
        {
            // Não conseguiu apagar o banco do reparo anterior: o reparo
            // para aqui, com o relato.
            return new ResultadoReparo(false, "Não foi possível preparar o reparo: o arquivo temporário está em uso.", 0, 0, null);
        }

        int salvasAmostras = 0;
        int salvosUsos = 0;

        try
        {
            using (var novo = Sqlite.Abrir(novoCaminho))
            {
                if (novo == null)
                    return new ResultadoReparo(false, "Não foi possível criar o banco novo durante o reparo.", 0, 0, null);

                // O banco novo fica sem WAL de propósito: assim ele é um
                // arquivo só, e trocar o arquivo no lugar não deixa
                // journaling para trás.
                if (!CriarEsquema(novo))
                {
                    // Sem esquema não cabe linha nenhuma: trocar o arquivo
                    // aqui esvaziaria um banco que talvez ainda tivesse
                    // dado para ler.
                    return new ResultadoReparo(false, "Não foi possível criar o esquema do banco novo: o reparo foi cancelado e o arquivo antigo foi mantido.", 0, 0, null);
                }

                novo.EmTransacao(b =>
                {
                    foreach (var linha in amostras)
                    {
                        if (linha.Length < 12) continue;
                        if (b.Executar(
                            @"INSERT OR REPLACE INTO amostras
                              (momento, cpu_temp, cpu_carga, cpu_consumo, cpu_clock,
                               gpu_temp, gpu_carga, gpu_clock, gpu_vram, ram_uso,
                               disco_leitura, disco_escrita)
                              VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
                            (IReadOnlyList<object?>)linha))
                            salvasAmostras++;
                    }

                    foreach (var linha in usos)
                    {
                        if (linha.Length < 3) continue;
                        if (b.Executar(
                            "INSERT OR IGNORE INTO uso (momento, processo, titulo) VALUES (?, ?, ?)",
                            (IReadOnlyList<object?>)linha))
                            salvosUsos++;
                    }
                });
            }
        }
        catch
        {
            return new ResultadoReparo(false, "Não foi possível reconstruir o banco de dados.", 0, 0, null);
        }

        // Tinha linha para copiar e nenhuma entrou: alguma coisa deu errado
        // (disco cheio, arquivo trancado). Trocar o banco agora trocaria
        // histórico por um banco vazio — o cancelamento é mais honesto.
        int lidas = amostras.Count + usos.Count;
        if (lidas > 0 && salvasAmostras == 0 && salvosUsos == 0)
        {
            return new ResultadoReparo(false,
                $"O reparo foi cancelado: {lidas} linhas foram lidas do banco antigo, mas nenhuma entrou no banco novo. O arquivo antigo foi mantido como está.",
                0, 0, null);
        }

        // O banco estragado vira cópia datada: é o desfazer do reparo.
        string? copia = null;
        try
        {
            if (File.Exists(Caminho))
            {
                copia = Caminho + ".avariado-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
                File.Move(Caminho, copia);

                // O WAL e o índice do banco velho vão junto com ele: a cópia
                // só vale alguma coisa se trouxer os dados que ainda estavam
                // no WAL.
                MoverArquivoDoWal(Caminho + "-wal", copia + "-wal");
                MoverArquivoDoWal(Caminho + "-shm", copia + "-shm");
            }

            ApagarArquivosDoWal(Caminho);

            File.Move(novoCaminho, Caminho);
        }
        catch
        {
            // A troca falhou: se o banco velho foi movido, ele volta para
            // o lugar — é melhor o banco estragado no lugar do que nenhum.
            if (copia != null && !File.Exists(Caminho))
            {
                try { File.Move(copia, Caminho); } catch { /* já foi relatado */ }
            }

            return new ResultadoReparo(false, "Não foi possível trocar o banco de dados pelo novo.", salvasAmostras, salvosUsos, copia);
        }

        lock (Tranca)
        {
            _instancia = null;
            _ultimaTentativa = DateTime.MinValue;
        }

        // O que o banco tinha é o que a contagem diz; o que ficou no banco
        // novo é o que foi salvo. A diferença é o que se perdeu — e ela
        // precisa aparecer no relato.
        long perdidasAmostras = Math.Max(0, totalAmostras - salvasAmostras);
        long perdidosUsos = Math.Max(0, totalUso - salvosUsos);

        string relato =
            $"O banco estava com problema ({TraduzirIntegridade(integridade)}). Foi reconstruído: " +
            $"{salvasAmostras} amostras de sensor e {salvosUsos} registros de uso foram salvos.";

        if (perdidasAmostras > 0 || perdidosUsos > 0)
        {
            relato += $" Se perderam {perdidasAmostras} amostras de sensor e {perdidosUsos} registros de uso.";
            relato += " O banco antigo, com o que ficou nele, está guardado na cópia.";
        }

        if (salvasAmostras == 0 && salvosUsos == 0)
        {
            relato += " Nada pôde ser lido do banco estragado: o banco novo começa vazio.";
            relato += " Se o histórico antigo em CSV ainda estiver no disco, apagar o arquivo do banco faz o app importá-lo de novo.";
        }

        if (copia != null)
            relato += $" O banco antigo ficou guardado em {copia}.";

        return new ResultadoReparo(true, relato, salvasAmostras, salvosUsos, copia);
    }

    /// <summary>
    /// Apaga os arquivos de WAL e de índice do banco (eles pertencem ao
    /// arquivo velho: deixar sobrando é pedir para o SQLite ler o
    /// histórico errado).
    /// </summary>
    private static void ApagarArquivosDoWal(string caminho)
    {
        foreach (string extra in new[] { "-wal", "-shm" })
        {
            try
            {
                string arquivo = caminho + extra;
                if (File.Exists(arquivo)) File.Delete(arquivo);
            }
            catch
            {
                // Arquivo trancado pelo Windows: o banco novo funciona
                // mesmo assim (o SQLite recria o WAL no próximo uso).
            }
        }
    }

    /// <summary>
    /// Move um arquivo do banco velho para junto da cópia datada. Falha não
    /// é problema: a cópia do banco principal já está salva.
    /// </summary>
    private static void MoverArquivoDoWal(string origem, string destino)
    {
        try
        {
            if (File.Exists(origem)) File.Move(origem, destino);
        }
        catch
        {
            // O WAL não foi para a cópia: o arquivo principal do banco
            // estragado continua guardado, que é o que importa.
        }
    }

    /// <summary>
    /// A cópia avariada mais recente (null quando não há nenhuma). É o que
    /// a tela oferece para desfazer o reparo.
    /// </summary>
    public static string? CopiaAvariadaRecente()
    {
        try
        {
            if (!Directory.Exists(Pasta)) return null;

            var copias = new List<string>();
            foreach (string arquivo in Directory.EnumerateFiles(Pasta, NomeArquivo + ".avariado-*"))
                copias.Add(arquivo);

            if (copias.Count == 0) return null;

            copias.Sort((a, b) => string.CompareOrdinal(Path.GetFileName(b), Path.GetFileName(a)));
            return copias[0];
        }
        catch
        {
            // Pasta inacessível: sem cópia para restaurar.
            return null;
        }
    }

    /// <summary>
    /// Desfaz o reparo: coloca de volta a cópia avariada mais recente. O
    /// banco volta ao estado anterior — inclusive ao problema, que é o
    /// ponto: a decisão é do usuário, não do app.
    /// </summary>
    public static ResultadoReparo RestaurarCopia()
    {
        lock (Tranca)
        {
            string? copia = CopiaAvariadaRecente();
            if (copia == null)
                return new ResultadoReparo(false, "Não há cópia avariada para restaurar.", 0, 0, null);

            _instancia?.Dispose();
            _instancia = null;
            _ultimaTentativa = DateTime.MinValue;

            // O banco atual é guardado com um nome FORA do padrão das cópias
            // avariadas: se ele entrasse nesse padrão, a "cópia avariada mais
            // recente" passaria a apontar para o banco bom — e o desfazer
            // viraria um "restaurar por cima de si mesmo".
            string guardar = Caminho + ".substituido-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);

            try
            {
                ApagarArquivosDoWal(Caminho);

                if (File.Exists(Caminho)) File.Move(Caminho, guardar);
                File.Move(copia, Caminho);
            }
            catch
            {
                return new ResultadoReparo(false, "Não foi possível restaurar a cópia avariada.", 0, 0, copia);
            }

            string relato =
                $"A cópia avariada foi restaurada: {copia}. O banco atual ficou guardado em {guardar}.";

            return new ResultadoReparo(true, relato, 0, 0, guardar);
        }
    }

    // ------------------------------------------------------------------ interno

    /// <summary>
    /// Executa uma operação no banco com a trava do banco segurada.
    ///
    /// <para>
    /// A trava existe por causa do reparo: ele fecha a instância e troca o
    /// arquivo no lugar. Uma gravação que estivesse no meio nesse momento
    /// escreveria num banco que já não existe — e o SQLite fechando o
    /// handle por baixo dela derruba o processo.
    /// </para>
    /// </summary>
    private static T EmBanco<T>(Func<Sqlite, T> acao, T padrao)
    {
        lock (Tranca)
        {
            var banco = Instancia;
            if (banco == null) return padrao;

            return acao(banco);
        }
    }

    /// <summary>
    /// Fecha o banco aberto (usado pelo reparo e pelos testes).
    /// </summary>
    internal static void Fechar()
    {
        lock (Tranca)
        {
            _instancia?.Dispose();
            _instancia = null;
            _caminhoInstancia = "";
            _ultimaTentativa = DateTime.MinValue;
            _ultimaPoda = DateTime.MinValue;
            _estado = "";
        }
    }

    /// <summary>
    /// A data/hora do banco em ISO, para o diagnóstico (sem locale).
    /// </summary>
    public static string AgoraEmIso() => DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);
}
