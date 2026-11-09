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

    /// <summary>
    /// O INSERT das amostras. É o mesmo em toda gravação — a coleta, a
    /// migração do CSV antigo e a reconstrução do reparo gravam a mesma
    /// linha, na mesma ordem de colunas.
    /// </summary>
    private const string InsertAmostra = @"
            INSERT OR REPLACE INTO amostras
              (momento, cpu_temp, cpu_carga, cpu_consumo, cpu_clock,
               gpu_temp, gpu_carga, gpu_clock, gpu_vram, ram_uso,
               disco_leitura, disco_escrita)
              VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)";

    /// <summary>
    /// O INSERT dos registros de uso. O mesmo em toda gravação.
    /// </summary>
    private const string InsertUso =
        "INSERT OR IGNORE INTO uso (momento, processo, titulo) VALUES (?, ?, ?)";

    private static readonly object Tranca = new();

    private static Sqlite? _instancia;
    private static string _caminhoInstancia = "";
    private static DateTime _ultimaTentativa = DateTime.MinValue;
    private static DateTime _ultimaPoda = DateTime.MinValue;
    private static string _estado = "";

    /// <summary>
    /// Um aviso sobre a última abertura do banco — a migração do histórico
    /// antigo ficou incompleta, por exemplo. Vazio quando não há nada para
    /// avisar.
    ///
    /// <para>
    /// O aviso é separado do estado porque os dois são coisas diferentes:
    /// o estado diz se o banco está de pé, o aviso diz o que ficou
    /// pendente. Um aviso que mora no estado desaparece na primeira vez
    /// que o estado é reescrito — e foi exatamente o que aconteceu com a
    /// migração do CSV.
    /// </para>
    /// </summary>
    private static string _aviso = "";

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
    ///
    /// <para>
    /// A verificação acontece com a trava do banco: o reparo fecha a
    /// instância e troca o arquivo, e uma verificação fora da trava leria
    /// um arquivo que está sendo trocado por baixo. O custo é medido e é
    /// pequeno: num banco com 258 mil amostras e 18,6 MB de arquivo
    /// principal, o <c>quick_check</c> e o <c>integrity_check</c> levam
    /// cerca de 60 ms cada (medido em 09/11/2026, mediana de 15 medidas
    /// com a ordem alternada) — o completo <b>não</b> é mais rápido, e a
    /// diferença entre os dois fica dentro do ruído da medição. A coleta
    /// grava de 5 em 5 segundos: uma pausa de 60 ms não aparece para o
    /// usuário.
    /// </para>
    /// </summary>
    private static string EstadoDaIntegridade(bool completo)
    {
        // A leitura acontece com a trava do banco: o reparo fecha a
        // instância e troca o arquivo, e uma verificação fora da trava
        // leria um arquivo que está sendo trocado por baixo.
        string estado = EmBanco(b => IntegridadeInterna(b, completo), "");

        if (estado.Length > 0) return estado;

        return EstadoSemInstancia();
    }

    /// <summary>
    /// O estado do banco já aberto, com a verificação de integridade
    /// escolhida.
    /// </summary>
    private static string IntegridadeInterna(Sqlite banco, bool completo)
    {
        string integridade = banco.VerificarIntegridade(completo);
        return integridade == "ok"
            ? "ok"
            : "O banco de dados precisa de reparo: " + TraduzirIntegridade(integridade);
    }

    /// <summary>
    /// O estado quando não há instância aberta: ou o arquivo não pôde ser
    /// aberto (e precisa de reparo), ou o banco não está disponível.
    /// </summary>
    private static string EstadoSemInstancia()
    {
        if (File.Exists(Caminho))
            return "O banco de dados precisa de reparo: o arquivo não pôde ser aberto.";

        return _estado.Length > 0 ? _estado : "O banco de dados não está disponível.";
    }

    /// <summary>
    /// O retrato quando não há instância aberta: sem banco não há contagem
    /// a fazer, e o retrato diz que o banco não está disponível. Com o
    /// arquivo no lugar (e ele não pôde ser aberto), a contagem não deu
    /// para fazer — e "0 amostras" seria mentira.
    /// </summary>
    private static Diagnostico SemInstancia()
    {
        bool temArquivo = File.Exists(Caminho);

        return new Diagnostico(
            EstadoSemInstancia(),
            temArquivo ? TamanhoDoArquivo() : 0,
            temArquivo ? -1 : 0,
            temArquivo ? -1 : 0,
            Caminho,
            CopiaAvariadaRecente(),
            _aviso,
            0);
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
    /// <param name="Aviso">
    /// Um aviso sobre o banco que não o impede de funcionar (a migração do
    /// histórico antigo ficou incompleta, por exemplo). Vazio quando não
    /// há nada a avisar.
    /// </param>
    /// <param name="DiasComUso">
    /// Quantos dias têm histórico de uso — contado no mesmo retrato das
    /// outras linhas, para o relatório não misturar dois bancos.
    /// </param>
    public sealed record Diagnostico(
        string Estado,
        long TamanhoBytes,
        long Amostras,
        long Uso,
        string Caminho,
        string? CopiaAvariada,
        string Aviso = "",
        int DiasComUso = 0);

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
            // O retrato inteiro sai de UMA leitura com a trava segurada: a
            // contagem, a integridade e o tamanho vinham de momentos
            // diferentes, e o reparo troca o arquivo no meio — o retrato
            // acabava misturando o banco velho com o tamanho do novo.
            //
            // A contagem de dias com histórico entra no mesmo retrato: a
            // linha de comando mostra os dois no mesmo relatório, e dois
            // momentos diferentes podiam dar dois bancos diferentes.
            return EmBanco(b => new Diagnostico(
                    IntegridadeInterna(b, completo),
                    TamanhoDoArquivo(),
                    Contar(b, "SELECT COUNT(*) AS total FROM amostras"),
                    Contar(b, "SELECT COUNT(*) AS total FROM uso"),
                    Caminho,
                    CopiaAvariadaRecente(),
                    _aviso,
                    DiasComUsoEm(b).Count),
                SemInstancia());
        }
        catch
        {
            // O retrato é informativo: se ele falhar, a tela mostra que o
            // banco não pôde ser lido.
            return new Diagnostico(
                "O banco de dados não pôde ser lido.",
                0, 0, 0, Caminho, null, _aviso, 0);
        }
    }

    /// <summary>
    /// Abre (ou cria) o banco, cria o esquema e migra o que for preciso.
    /// </summary>
    private static Sqlite? Abrir()
    {
        // Cada abertura começa sem aviso: um aviso de uma tentativa
        // anterior não pode sobreviver para a próxima.
        _aviso = "";

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

        // O aviso da migração viaja num canal só dele (_aviso). O estado
        // (_estado) diz se o banco está de pé; o aviso diz o que ficou
        // pendente. Quando os dois moravam no mesmo lugar, o "ok"
        // escrito aqui apagava a falha da migração duas linhas depois —
        // e uma migração pela metade passava em silêncio.
        _aviso = Migrar(banco);
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
        bool ok = EmBanco(b => b.Executar(InsertAmostra, ValoresDaAmostra(amostra)), false);

        if (ok) PodarSeEstourou();
        return ok;
    }

    /// <summary>
    /// Os valores de uma amostra, na ordem das colunas da tabela.
    /// </summary>
    private static IReadOnlyList<object?> ValoresDaAmostra(AmostraSensor amostra) => new object?[]
    {
        Sqlite.MomentoParaUnix(amostra.Momento),
        amostra.CpuTemp, amostra.CpuCarga, amostra.CpuConsumo, amostra.CpuClock,
        amostra.GpuTemp, amostra.GpuCarga, amostra.GpuClock, amostra.GpuVram,
        amostra.RamUso, amostra.DiscoLeitura, amostra.DiscoEscrita
    };

    /// <summary>
    /// Os valores de uma troca, na ordem das colunas da tabela.
    /// </summary>
    private static IReadOnlyList<object?> ValoresDaTroca(TrocaUso troca) => new object?[]
    {
        Sqlite.MomentoParaUnix(troca.Momento),
        Higienizar(troca.Processo),
        Higienizar(troca.Titulo)
    };

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
            // A identidade de uma troca é (instante, programa, título) — é
            // a chave UNIQUE da tabela. Duas trocas diferentes nunca têm
            // a mesma chave: a mesma chave É a mesma troca. Por isso o
            // IGNORE abaixo nunca descarta uma troca real — ele só impede
            // que a mesma troca seja gravada duas vezes.
            if (!b.Executar(
                    "INSERT OR IGNORE INTO uso (momento, processo, titulo) VALUES (?, ?, ?)",
                    momento, processo, titulo))
                return false;

            // A linha entrou agora.
            if (b.Alteracoes > 0) return true;

            // O INSERT foi IGNORADO: a chave já existe no banco — e a
            // troca JÁ está registrada (por esta gravação ou por uma
            // anterior). O amostrador não precisa ficar tentando de novo
            // (e adiar a troca seguinte).
            var mesma = b.Consultar(
                "SELECT 1 AS ok FROM uso WHERE momento = ? AND processo = ? AND titulo = ?",
                momento, processo, titulo);

            // Se a chave não está lá, a troca NÃO está registrada: o
            // IGNORE veio de outra coisa. Devolver false faz o amostrador
            // tentar de novo, em vez de dar a troca por gravada — senão o
            // programa anterior ficaria com o tempo dela, e a troca real
            // nunca entraria no histórico.
            return mesma.Count > 0;
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
            //
            // A ordem é estável (OrderBy): o List.Sort não é, e duas
            // trocas no mesmo instante podiam sair trocadas de ordem a
            // cada leitura — a mesma sessão, com durações diferentes.
            return trocas.OrderBy(t => t.Momento).ToList();
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

        // A ordem é estável (OrderBy): o List.Sort não é, e duas trocas no
        // mesmo instante podiam sair trocadas de ordem a cada leitura.
        return trocas.OrderBy(t => t.Momento).ToList();
    }

    /// <summary>Lista os dias que têm histórico, do mais novo para o mais antigo.</summary>
    public static List<DateTime> DiasComUso()
    {
        // A leitura inteira (inclusive as contagens) acontece com a trava do
        // banco: o reparo pode trocar o arquivo no meio, e ler um arquivo
        // que está sendo trocado dá número errado na tela.
        return EmBanco(b => DiasComUsoEm(b), new List<DateTime>());
    }

    /// <summary>
    /// Os dias que têm histórico, contados num banco já aberto.
    ///
    /// <para>
    /// É o núcleo da contagem: a lista pública e o retrato do banco usam
    /// a mesma conta. A linha de comando mostra a contagem de dias ao
    /// lado da contagem de linhas — e os dois números precisam vir do
    /// mesmo banco, senão o relatório mente.
    /// </para>
    /// </summary>
    private static List<DateTime> DiasComUsoEm(Sqlite banco)
    {
        var dias = new List<DateTime>();

        var limites = banco.Consultar("SELECT MIN(momento) AS primeiro, MAX(momento) AS ultimo FROM uso");
        if (limites.Count == 0) return dias;

        long primeiro = limites[0].Inteiro("primeiro");
        long ultimo = limites[0].Inteiro("ultimo");
        if (primeiro <= 0 || ultimo <= 0) return dias;

        DateTime dia = Sqlite.UnixParaMomento(ultimo).Date;
        DateTime fim = Sqlite.UnixParaMomento(primeiro).Date;

        // O dia local é uma faixa de instantes — e a faixa muda no
        // horário de verão. Por isso a conta é feita aqui, com a data
        // local do .NET, e não no SQLite (lá o 'localtime' depois do
        // 'unixepoch' não devolve nada na biblioteca do Windows).
        //
        // O limite é de dias COM HISTÓRICO, e a varredura vai do mais
        // novo para o mais antigo. Começando pelo mais antigo, um
        // histórico longo (dois anos de CSV importado) consumia o
        // limite nos dias velhos — e os dias recentes sumiam da
        // lista, que é justamente o que a tela mostra primeiro.
        int conferidos = 0;
        while (dia >= fim && dias.Count < 800 && conferidos < 5000)
        {
            long inicio = Sqlite.MomentoParaUnix(dia);
            long fimDia = Sqlite.MomentoParaUnix(dia.AddDays(1));

            var existe = banco.Consultar(
                "SELECT 1 AS dia FROM uso WHERE momento >= ? AND momento < ? LIMIT 1",
                inicio, fimDia);

            if (existe.Count > 0) dias.Add(dia);

            dia = dia.AddDays(-1);
            conferidos++;
        }

        // Do mais novo para o mais antigo (a lista da tela começa por hoje).
        return dias.OrderByDescending(d => d).ToList();
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

    /// <summary>
    /// Quantas linhas tem cada tabela (para o diagnóstico).
    ///
    /// <para>
    /// Devolve -1 na tabela cuja contagem não deu para fazer (o banco está
    /// estragado): número errado é pior que número ausente, e "0
    /// amostras" num banco com milhares seria mentira. A tela e a linha de
    /// comando mostram "não deu para contar" nesse caso.
    /// </para>
    /// </summary>
    public static (long Amostras, long Uso) Contagem()
    {
        // Sem instância e com arquivo no lugar: o banco existe, mas não
        // pôde ser aberto — a contagem não deu para fazer. "0" seria
        // mentira (o banco pode ter milhares de linhas).
        return EmBanco(b => (Contar(b, "SELECT COUNT(*) AS total FROM amostras"),
                             Contar(b, "SELECT COUNT(*) AS total FROM uso")),
            File.Exists(Caminho) ? (-1L, -1L) : (0L, 0L));
    }

    /// <summary>
    /// Conta as linhas de uma tabela. Devolve -1 quando a consulta falhou
    /// (a tabela vazia devolve 0 — as duas coisas precisam ser diferentes).
    /// </summary>
    private static long Contar(Sqlite banco, string sql)
    {
        var linhas = banco.Consultar(sql);
        if (linhas.Count == 0) return banco.UltimoErro.Length > 0 ? -1 : 0;

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
    public static long Tamanho => TamanhoDoArquivo();

    /// <summary>
    /// O tamanho do banco no disco, em bytes (0 quando não existe).
    ///
    /// <para>
    /// A conta inclui o arquivo de WAL (<c>-wal</c>): em modo WAL os dados
    /// mais recentes ficam nele até o checkpoint, e mostrar só o arquivo
    /// principal diria que o banco está vazio quando não está.
    /// </para>
    /// </summary>
    private static long TamanhoDoArquivo()
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

    /// <summary>
    /// Apaga tudo do banco (as duas tabelas). Usado pelos testes — e pelo
    /// que precisar de um banco limpo.
    /// </summary>
    public static bool Limpar()
    {
        // Cada DELETE é conferido: um que falha desfaz a transação
        // inteira, e o banco ficaria como estava — ou pior, meio
        // apagado.
        return EmBanco(b => b.EmTransacao(t =>
        {
            if (!t.Executar("DELETE FROM amostras"))
                throw new InvalidOperationException("não foi possível apagar as amostras");

            if (!t.Executar("DELETE FROM uso"))
                throw new InvalidOperationException("não foi possível apagar os registros de uso");

            // A tabela sqlite_sequence só existe depois que um AUTOINCREMENT
            // foi usado. Apagar dela quando ela não existe faria a
            // transação falhar — e o DELETE das amostras seria desfeito
            // junto, sem ninguém ver.
            var temSequencia = t.Consultar(
                "SELECT name FROM sqlite_master WHERE type = 'table' AND name = 'sqlite_sequence'");

            if (temSequencia.Count > 0 && !t.Executar("DELETE FROM sqlite_sequence WHERE name = 'uso'"))
                throw new InvalidOperationException("não foi possível reiniciar a sequência do histórico de uso");
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
    private static string Migrar(Sqlite banco)
    {
        try
        {
            int versao = VersaoDoEsquema(banco);
            if (versao >= VersaoEsquema) return "";

            // Só entra no banco o que ainda está dentro do prazo de cada
            // tabela: o app promete guardar 15 dias de amostras e 30 de
            // uso. Importar anos de histórico encheria o banco com o que
            // seria podado em seguida — e a abertura do app ficava
            // esperando. O CSV antigo continua no disco, com tudo.
            var amostras = LegadoCsv.LerAmostras(DateTime.Now.AddDays(-DiasRetidosAmostras));
            var trocas = LegadoCsv.LerTrocas(DateTime.Now.AddDays(-DiasRetidosUso));

            // A leitura do CSV pode parar no meio (o arquivo trancado, o
            // disco cheio). Uma leitura pela metade NÃO é uma importação
            // completa: o que faltou nunca entraria no banco — e, como a
            // contagem do que entrou bate, ninguém saberia que faltou.
            bool leituraCompleta = amostras.Completa && trocas.Completa;

            if (amostras.Linhas.Count > 0 || trocas.Linhas.Count > 0)
            {
                bool fechou = banco.EmTransacao(b =>
                {
                    // A versão é conferida de novo dentro da transação: o
                    // app e o processo de coleta abrem o banco ao mesmo
                    // tempo, e os dois podem tentar migrar. Quem entra
                    // depois não precisa importar de novo — o histórico
                    // já está no banco.
                    if (VersaoDoEsquema(b) >= VersaoEsquema) return;

                    // A importação vai em lote, com o comando preparado
                    // reaproveitado: preparar e finalizar um comando por
                    // linha custava 23 segundos em 260 mil linhas
                    // (medido).
                    int entrouAmostras = b.ExecutarEmLote(InsertAmostra, amostras.Linhas.Select(ValoresDaAmostra));
                    if (entrouAmostras != amostras.Linhas.Count)
                        throw new InvalidOperationException("a importação das amostras parou na linha " + entrouAmostras);

                    int entraramTrocas = b.ExecutarEmLote(InsertUso, trocas.Linhas.Select(ValoresDaTroca));
                    if (entraramTrocas != trocas.Linhas.Count)
                        throw new InvalidOperationException("a importação do histórico de uso parou na linha " + entraramTrocas);

                    // A versão do esquema só é gravada quando a LEITURA foi
                    // completa: a versão diz "o CSV já entrou". Com a
                    // leitura pela metade, ela diria que entrou tudo — e
                    // o resto do CSV ficaria órfão para sempre, sem
                    // aviso.
                    if (leituraCompleta && !b.Executar($"PRAGMA user_version = {VersaoEsquema}"))
                        throw new InvalidOperationException("não foi possível gravar a versão do esquema");
                });

                if (!fechou)
                {
                    // A transação foi desfeita: o banco continua vazio e a
                    // migração é tentada de novo na próxima abertura (a
                    // versão do esquema continua atrás).
                    return "Não foi possível migrar o histórico antigo para o banco de dados.";
                }
            }
            else if (leituraCompleta)
            {
                // Não havia o que importar: a versão é gravada fora da
                // transação (não há transação para fechar).
                banco.Executar($"PRAGMA user_version = {VersaoEsquema}");
            }

            if (!leituraCompleta)
            {
                // O que deu para ler entrou no banco; o que faltou fica
                // para a próxima abertura (a versão do esquema continua
                // atrás, porque a importação não acabou). O aviso diz
                // isso: uma leitura parcial é o tipo de coisa que o
                // usuário precisa saber, não o tipo de coisa que pode
                // passar em silêncio.
                return "A leitura do histórico antigo (CSV) parou no meio: parte do histórico pode não ter sido importado. O app tenta de novo na próxima abertura.";
            }

            return "";
        }
        catch
        {
            // Migrar que falha não impede o app de funcionar: o banco fica
            // vazio e o histórico novo começa a ser gravado nele. A migração
            // é tentada de novo na próxima abertura (a versão do esquema
            // continua atrás).
            return "Não foi possível migrar o histórico antigo para o banco de dados.";
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
        long AmostrasRecuperadas,
        long UsoRecuperado,
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

            // A contagem e a leitura acontecem na MESMA transação de
            // leitura: em instantes diferentes, elas podem ver arquivos
            // diferentes (o reparo troca o arquivo no meio), e o relato da
            // perda sairia errado — diria que não se perdeu nada quando
            // se perdeu.
            long totalAmostras = -1;
            long totalUso = -1;
            bool falhouAmostras = false;
            bool falhouUsos = false;

            // A versão do esquema do banco velho é lida ANTES de
            // descartar a instância: o banco novo precisa da mesma
            // versão. Sem ela, a próxima abertura importaria o CSV de
            // novo — por cima do histórico que o reparo acabou de
            // recuperar.
            int versaoEsquema = 0;

            bool leu = banco.EmTransacaoLeitura(b =>
            {
                versaoEsquema = VersaoDoEsquema(b);
                totalAmostras = ContarParaReparo(b, "SELECT COUNT(*) AS total FROM amostras");
                totalUso = ContarParaReparo(b, "SELECT COUNT(*) AS total FROM uso");

                amostras = LerLinhas(b,
                    "SELECT momento, cpu_temp, cpu_carga, cpu_consumo, cpu_clock, gpu_temp, gpu_carga, gpu_clock, gpu_vram, ram_uso, disco_leitura, disco_escrita FROM amostras",
                    out falhouAmostras);
                usos = LerLinhas(b, "SELECT momento, processo, titulo FROM uso", out falhouUsos);
            });

            // A leitura falhou (ou a transação): o reparo não pode trocar
            // o arquivo achando que leu tudo — e o que faltar tem que
            // aparecer no relato.
            bool leituraFalhou = !leu || falhouAmostras || falhouUsos;

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

            return Reconstruir(amostras, usos, integridade, totalAmostras, totalUso, versaoEsquema);
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
    ///
    /// <para>
    /// <paramref name="falhou"/> diz se a consulta quebrou. Uma tabela
    /// vazia e uma consulta que falhou dão a mesma lista vazia — e a
    /// diferença é o que separa "não tinha nada" de "não deu para ler".
    /// </para>
    /// </summary>
    private static List<object?[]> LerLinhas(Sqlite banco, string sql, out bool falhou)
    {
        falhou = false;
        var linhas = new List<object?[]>();

        var brutas = banco.Consultar(sql);
        if (brutas.Count == 0 && banco.UltimoErro.Length > 0)
        {
            falhou = true;
            return linhas;
        }

        foreach (var linha in brutas)
        {
            var valores = new object?[linha.Colunas.Count];
            for (int c = 0; c < linha.Colunas.Count; c++)
                valores[c] = linha.Valor(linha.Colunas[c]);
            linhas.Add(valores);
        }

        return linhas;
    }

    /// <summary>
    /// As linhas de uma tabela que podem ser gravadas de volta.
    ///
    /// <para>
    /// Uma linha com valor nulo numa coluna <b>obrigatória</b> não pode
    /// ser gravada: o esquema não aceita nulo ali, e o INSERT seria
    /// ignorado em silêncio — a linha sumiria do banco novo sem que
    /// ninguém contasse isso. Essas linhas ficam de fora, e o relato do
    /// reparo diz quantas se perderam.
    /// </para>
    /// </summary>
    /// <param name="linhas">As linhas lidas do banco velho.</param>
    /// <param name="colunas">Quantas colunas a tabela tem.</param>
    /// <param name="obrigatorias">
    /// Quantas das primeiras colunas não aceitam nulo (as de chave).
    /// </param>
    private static List<IReadOnlyList<object?>> LinhasGravaveis(List<object?[]> linhas, int colunas, int obrigatorias)
    {
        var gravaveis = new List<IReadOnlyList<object?>>();

        foreach (var linha in linhas)
        {
            if (linha.Length < colunas) continue;

            bool valida = true;
            for (int c = 0; c < obrigatorias; c++)
            {
                if (linha[c] is not null) continue;

                valida = false;
                break;
            }

            if (valida) gravaveis.Add(linha);
        }

        return gravaveis;
    }

    /// <summary>
    /// Cria um banco novo e copia o que foi lido do velho. Devolve o
    /// relato do que se salvou.
    /// </summary>
    private static ResultadoReparo Reconstruir(List<object?[]> amostras, List<object?[]> usos, string integridade, long totalAmostras, long totalUso, int versaoEsquema)
    {
        string novoCaminho = Caminho + ".reparo";

        try
        {
            if (File.Exists(novoCaminho)) File.Delete(novoCaminho);
            ApagarArquivosDoWal(novoCaminho);
        }
        catch
        {
            // Não conseguiu apagar o banco do reparo anterior: o reparo
            // para aqui, com o relato.
            return new ResultadoReparo(false, "Não foi possível preparar o reparo: o arquivo temporário está em uso.", 0, 0, null);
        }

        // O que ficou no banco novo é o que a CONTAGEM do banco novo diz —
        // e não quantas operações de INSERT foram feitas: duas linhas com
        // o mesmo instante viram uma só no banco novo, e o contador de
        // operações mentia para cima.
        long salvasAmostras = 0;
        long salvosUsos = 0;
        string? motivo = null;

        // As amostras têm colunas que podem ser nulas (o sensor que não
        // existia), mas o instante não. O histórico de uso tem três
        // colunas obrigatórias.
        var amostrasGravaveis = LinhasGravaveis(amostras, 12, 1);
        var usosGravaveis = LinhasGravaveis(usos, 3, 3);

        try
        {
            using (var novo = Sqlite.Abrir(novoCaminho))
            {
                if (novo == null)
                {
                    ApagarBancoNovo(novoCaminho);
                    return new ResultadoReparo(false, "Não foi possível criar o banco novo durante o reparo.", 0, 0, null);
                }

                // O banco novo fica sem WAL de propósito: assim ele é um
                // arquivo só, e trocar o arquivo no lugar não deixa
                // journaling para trás.
                if (!CriarEsquema(novo))
                {
                    // Sem esquema não cabe linha nenhuma: trocar o arquivo
                    // aqui esvaziaria um banco que talvez ainda tivesse
                    // dado para ler.
                    ApagarBancoNovo(novoCaminho);
                    return new ResultadoReparo(false, "Não foi possível criar o esquema do banco novo: o reparo foi cancelado e o arquivo antigo foi mantido.", 0, 0, null);
                }

                bool fechou = novo.EmTransacao(b =>
                {
                    // A gravação é conferida: um INSERT que falha (disco
                    // cheio, arquivo trancado) desfaz a transação toda.
                    // Sem essa conferência, o banco novo ficava pela
                    // metade e entrava no lugar do banco antigo — trocando
                    // histórico por histórico incompleto.
                    int salvas = b.ExecutarEmLote(InsertAmostra, amostrasGravaveis);
                    if (salvas != amostrasGravaveis.Count)
                    {
                        motivo = "só " + salvas + " de " + amostrasGravaveis.Count + " amostras entraram no banco novo";
                        throw new InvalidOperationException(motivo);
                    }

                    int salvos = b.ExecutarEmLote(InsertUso, usosGravaveis);
                    if (salvos != usosGravaveis.Count)
                    {
                        motivo = "só " + salvos + " de " + usosGravaveis.Count + " registros de uso entraram no banco novo";
                        throw new InvalidOperationException(motivo);
                    }

                    // A versão do esquema vai junto com as linhas: sem
                    // ela, a próxima abertura acharia que o CSV nunca
                    // entrou e o importaria de novo — por cima do
                    // histórico que o reparo acabou de recuperar.
                    //
                    // A versão do banco VELHO só é aproveitada quando
                    // alguma linha foi recuperada. Se nada entrou (o
                    // arquivo nem era um banco), o banco novo começa
                    // sem a marca de "o CSV já entrou" — e o histórico
                    // antigo volta do CSV na próxima abertura, em vez
                    // de ficar para sempre fora do app.
                    bool recuperouAlgo = amostrasGravaveis.Count > 0 || usosGravaveis.Count > 0;
                    int versaoFinal = recuperouAlgo ? versaoEsquema : 0;

                    if (!b.Executar($"PRAGMA user_version = {versaoFinal}"))
                    {
                        motivo = "não foi possível gravar a versão do esquema no banco novo";
                        throw new InvalidOperationException(motivo);
                    }
                });

                if (!fechou)
                {
                    ApagarBancoNovo(novoCaminho);
                    return new ResultadoReparo(false,
                        "Não foi possível gravar todo o histórico no banco novo" +
                        (motivo != null ? " (" + motivo + ")" : "") +
                        ", e por isso o reparo foi cancelado: o arquivo antigo foi mantido como está.",
                        0, 0, null);
                }

                salvasAmostras = Contar(novo, "SELECT COUNT(*) AS total FROM amostras");
                salvosUsos = Contar(novo, "SELECT COUNT(*) AS total FROM uso");
            }
        }
        catch (Exception ex)
        {
            ApagarBancoNovo(novoCaminho);
            return new ResultadoReparo(false, "Não foi possível reconstruir o banco de dados: " + ex.Message, 0, 0, null);
        }

        // Tinha linha para copiar e nenhuma entrou: alguma coisa deu errado
        // (disco cheio, arquivo trancado). Trocar o banco agora trocaria
        // histórico por um banco vazio — o cancelamento é mais honesto.
        int lidas = amostras.Count + usos.Count;
        if (lidas > 0 && salvasAmostras == 0 && salvosUsos == 0)
        {
            ApagarBancoNovo(novoCaminho);
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
            //
            // O WAL dele volta junto: a cópia só vale alguma coisa se
            // trouxer os dados que ainda estavam no WAL. Sem ele, o banco
            // voltaria ao lugar mais velho do que estava — e o histórico
            // das últimas gravações sumiria sem aviso.
            bool restaurado = false;
            if (copia != null && !File.Exists(Caminho))
            {
                try
                {
                    File.Move(copia, Caminho);
                    restaurado = true;
                }
                catch
                {
                    // Já foi relatado: o banco velho continua guardado na
                    // cópia datada, que é o desfazer.
                }

                if (restaurado) MoverArquivoDoWal(copia + "-wal", Caminho + "-wal");
            }

            // O banco novo não entrou no lugar: ele não serve para nada
            // aqui e ocupa espaço (dezenas de megabytes num histórico
            // grande). Sem esta limpeza, cada tentativa de reparo com o
            // app aberto deixava um arquivo órfão na pasta do histórico.
            ApagarBancoNovo(novoCaminho);

            string relatoTroca =
                "Não foi possível trocar o arquivo do banco de dados pelo novo" +
                " (o arquivo está em uso por outro programa — o TRANAL SENTINEL aberto, por exemplo)." +
                " O banco antigo foi mantido como está.";

            // A cópia só vale como desfazer quando ela ainda está no
            // lugar: um banco que voltou para o caminho original não
            // pode ser apontado como "cópia avariada" — o botão de
            // restaurar ofereceria um arquivo que não existe.
            return new ResultadoReparo(false, relatoTroca, salvasAmostras, salvosUsos, restaurado ? null : copia);
        }

        lock (Tranca)
        {
            _instancia = null;
            _ultimaTentativa = DateTime.MinValue;
        }

        // O que o banco tinha é o que a contagem diz; o que ficou no banco
        // novo é o que a contagem do banco novo diz. A diferença é o que
        // se perdeu — e ela precisa aparecer no relato.
        //
        // Quando a contagem do banco velho não pôde ser feita, a perda
        // não é calculável: o relato diz isso, em vez de dizer que não
        // se perdeu nada. Perder em silêncio é o que o app não faz.
        long perdidasAmostras = totalAmostras >= 0 ? Math.Max(0, totalAmostras - salvasAmostras) : 0;
        long perdidosUsos = totalUso >= 0 ? Math.Max(0, totalUso - salvosUsos) : 0;

        string relato =
            $"O banco estava com problema ({TraduzirIntegridade(integridade)}). Foi reconstruído: " +
            $"{salvasAmostras} amostras de sensor e {salvosUsos} registros de uso foram salvos.";

        if (totalAmostras < 0)
            relato += " Não deu para contar as amostras do banco antigo, então não é possível dizer quantas se perderam.";

        if (totalUso < 0)
            relato += " Não deu para contar os registros de uso do banco antigo, então não é possível dizer quantos se perderam.";

        if (totalAmostras >= 0 && totalUso >= 0 && (perdidasAmostras > 0 || perdidosUsos > 0))
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
    /// Apaga um arquivo. Falha não é problema: quem chama já decidiu
    /// cancelar a operação, e o arquivo que sobrou não muda nada.
    /// </summary>
    private static void ApagarArquivo(string caminho)
    {
        try
        {
            if (File.Exists(caminho)) File.Delete(caminho);
        }
        catch
        {
            // Arquivo trancado pelo Windows: o reparo já foi cancelado, e
            // o arquivo temporário que ficou não entra no padrão das
            // cópias avariadas (o próximo reparo tenta apagá-lo de novo).
        }
    }

    /// <summary>
    /// Apaga o banco novo do reparo e os arquivos de journaling que
    /// ficaram junto dele. Falha não é problema: quem chama já decidiu
    /// cancelar a operação, e o arquivo que sobrou não muda nada.
    ///
    /// <para>
    /// Apagar os dois importa: o banco novo de um histórico grande ocupa
    /// dezenas de megabytes, e deixá-lo na pasta a cada reparo cancelado
    /// enchia o disco do usuário com arquivos órfãos que ninguém sabe
    /// o que são.
    /// </para>
    /// </summary>
    private static void ApagarBancoNovo(string caminho)
    {
        ApagarArquivo(caminho);
        ApagarArquivosDoWal(caminho);
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
            foreach (string arquivo in Directory.EnumerateFiles(Pasta, PrefixoCopia + "*"))
            {
                if (EhCopiaAvariada(Path.GetFileName(arquivo)))
                    copias.Add(arquivo);
            }

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
    /// O prefixo do nome das cópias avariadas (é o desfazer do reparo).
    /// </summary>
    private const string PrefixoCopia = NomeArquivo + ".avariado-";

    /// <summary>
    /// True quando o nome é de uma cópia avariada de verdade.
    ///
    /// <para>
    /// O nome tem que ser EXATAMENTE "historico.db.avariado-" seguido de 15
    /// caracteres (yyyymmdd-hhmmss). Sem essa exigência, o "-wal" e o "-shm"
    /// que acompanham a cópia entrariam na lista — e o desfazer poderia
    /// mover o WAL para o lugar do banco, o que destrói o histórico em vez
    /// de trazê-lo de volta.
    /// </para>
    /// </summary>
    private static bool EhCopiaAvariada(string nomeArquivo)
    {
        if (!nomeArquivo.StartsWith(PrefixoCopia, StringComparison.Ordinal)) return false;

        string resto = nomeArquivo[PrefixoCopia.Length..];
        if (resto.Length != 15) return false;

        return DateTime.TryParseExact(resto, "yyyyMMdd-HHmmss",
            CultureInfo.InvariantCulture, DateTimeStyles.None, out _);
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

                // O WAL da cópia vai junto: a cópia só vale alguma coisa
                // se trouxer os dados que ainda estavam nele — sem ele, o
                // banco restaurado perderia as últimas gravações. O
                // índice de memória (-shm) é reconstruído pelo próprio
                // SQLite, e um índice velho só causaria confusão.
                MoverArquivoDoWal(copia + "-wal", Caminho + "-wal");
                ApagarArquivo(copia + "-shm");
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
            _aviso = "";
        }
    }

    /// <summary>
    /// A data/hora do banco em ISO, para o diagnóstico (sem locale).
    /// </summary>
    public static string AgoraEmIso() => DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);
}
