using System.Diagnostics;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Session;

namespace TranalSentinel;

/// <summary>
/// Mede o tráfego de internet por programa, usando a ETW do próprio Windows.
///
/// <para>
/// A sessão clássica do kernel ("NetworkTCPIP") entrega, para cada pacote
/// TCP/UDP que entra ou sai, o tamanho e o processo dono da conexão. É a
/// mesma fonte que o Monitor de Recursos do Windows usa.
///
/// <para>
/// <b>Por que a ETW e não a API de estatísticas do TCP:</b> a API
/// <c>GetPerTcpConnectionEStats</c> foi medida nesta máquina e devolve o
/// número exato, mas só funciona no IPv4 (no IPv6 o
/// <c>SetPerTcp6ConnectionEStats</c> devolve "sem dados") e só cobre TCP — o
/// tráfego de navegador hoje é QUIC, que é UDP. A ETW cobre os dois, nos dois
/// protocolos, e o erro medido é de +0,25% (os cabeçalhos TCP/IP, que
/// também são tráfego).
///
/// <para>
/// <b>Medido em 09/11/2026</b> no PC do dono: 100 MB baixados por HTTPS
/// apareceram como 105.115.183 bytes (+0,25%); em tráfego local e em
/// download de 60 MB o número bateu. O custo foi de 0,83 s de CPU em 24 s
/// de coleta (≈3% de um núcleo) com 100 MB de download.
///
/// <para>
/// A ETW exige administrador. O TRANAL SENTINEL já exige (o manifesto pede
/// <c>requireAdministrator</c> e a tarefa agendada sobe com privilégio
/// máximo), mas se um dia ela subir sem, o coletor não derruba o app: ele
/// guarda o motivo em <see cref="Erro"/> e a tela mostra.
///
/// <para>
/// A sessão é do kernel: ela é global e não pode haver duas ao mesmo tempo.
/// Se outra ferramenta estiver usando (o WPR, por exemplo), a criação falha
/// e o motivo aparece — nunca um número inventado.
/// </para>
///
/// <para>
/// <b>A conta é por dia e por programa:</b> o que o app mede fica separado
/// por dia, e o total de cada dia já salvo no banco entra como base (veja
/// <see cref="Semear"/>). Sem essa base, cada vez que o app abrisse ele
/// começaria do zero e gravaria por cima do total do dia — apagando o que
/// já tinha sido medido antes.
/// </para>
/// </summary>
public sealed class ColetorTrafego : IDisposable
{
    /// <summary>O nome da sessão ETW. Serve para derrubar uma sessão órfã de uma execução anterior.</summary>
    private const string NomeSessao = "TranalSentinel-Rede";

    /// <summary>
    /// De quanto em quanto tempo o nome do dono do processo é conferido de
    /// novo. O Windows reusa o número de processo (PID): sem essa conferência,
    /// um programa novo que pegasse o PID de um velho continuaria sendo
    /// creditado ao nome do velho.
    /// </summary>
    private static readonly TimeSpan IntervaloDoNome = TimeSpan.FromSeconds(5);

    /// <summary>Quanto tempo a coleta espera, ao ser desligada, para os últimos pacotes saírem da fila.</summary>
    private static readonly TimeSpan DrenagemAoParar = TimeSpan.FromMilliseconds(400);

    /// <summary>De quanto em quanto tempo o nome dos processos é conferido de novo.</summary>
    private static readonly TimeSpan IntervaloDaConferencia = TimeSpan.FromSeconds(30);

    private readonly object _tranca = new();

    /// <summary>O que a coleta mediu, por dia e por processo.</summary>
    private readonly Dictionary<DateTime, Dictionary<int, (long Enviados, long Recebidos)>> _medido = new();

    /// <summary>
    /// O que já estava salvo no banco quando o app subiu, por dia e por nome
    /// de programa. É a base da conta do dia: o total de hoje é o que já
    /// estava salvo mais o que a coleta mediu depois.
    /// </summary>
    private readonly Dictionary<DateTime, Dictionary<string, (long Enviados, long Recebidos)>> _base = new();

    /// <summary>O nome do programa dono do processo, e quando foi conferido.</summary>
    private readonly Dictionary<int, (string Nome, DateTime Checado)> _nomes = new();

    private TraceEventSession? _sessao;
    private Thread? _thread;

    /// <summary>A thread de fundo que confere os nomes dos processos.</summary>
    private Thread? _threadConferencia;

    /// <summary>Pede para a conferência de nomes parar.</summary>
    private volatile bool _pararConferencia;

    /// <summary>O dia que a coleta está medindo agora.</summary>
    private DateTime _dia = DateTime.Today;

    /// <summary>Quando o dia foi conferido pela última vez (o dia muda uma vez por dia; conferir a cada pacote seria desperdício).</summary>
    private DateTime _diaChecadoEm = DateTime.MinValue;

    private long _eventosPerdidos;

    /// <summary>
    /// Os pacotes descartados, separados por dia.
    ///
    /// <para>
    /// A sessão do Windows conta os descartes desde que ela subiu, e esse
    /// número só cresce. Usado como está, o dia novo nasceria com os
    /// descartes do dia anterior — e o dia velho receberia os do dia novo.
    /// </para>
    /// </summary>
    private readonly Dictionary<DateTime, long> _perdidosPorDia = new();

    /// <summary>
    /// O total de descartos da sessão na última leitura — é por ele que se
    /// sabe o que é novo desde a leitura passada.
    /// </summary>
    private long _perdidosLidos;

    private string? _erro;

    /// <summary>A coleta está rodando.</summary>
    public bool Ativo => _thread?.IsAlive == true;

    /// <summary>
    /// O motivo de a coleta não estar rodando (ou null quando está).
    /// Vazio é o mesmo que null: a tela só pergunta "tem erro?".
    /// </summary>
    public string? Erro
    {
        get { lock (_tranca) return _erro; }
    }

    /// <summary>
    /// Quantos pacotes a coleta deixou de fora num dia.
    ///
    /// <para>
    /// A fila do log do kernel tem tamanho fixo: quando ela enche, o Windows
    /// descarta o que não coube. O total medido é então <b>menor</b> do que
    /// foi o real — e o cartão avisa, em vez de mostrar um número que parece
    /// completo e não é.
    /// </para>
    ///
    /// <para>
    /// <b>A conta é por dia.</b> A sessão do Windows conta os descartes desde
    /// que ela subiu, e o número só cresce. Cada leitura compara o total da
    /// sessão com o da leitura passada, e a diferença vai para o dia em que
    /// ela foi vista — assim o dia novo não nasce com os descartes do dia
    /// anterior, nem o dia velho recebe os do dia novo.
    /// </para>
    /// </summary>
    /// <param name="dia">O dia que se quer saber.</param>
    public long EventosPerdidosDoDia(DateTime dia)
    {
        lock (_tranca)
        {
            long acumulado = LerEventosPerdidos();
            long novos = acumulado - _perdidosLidos;
            _perdidosLidos = acumulado;

            if (novos > 0)
            {
                DateTime hj = DateTime.Now.Date;
                _perdidosPorDia.TryGetValue(hj, out var doDia);
                _perdidosPorDia[hj] = doDia + novos;
            }

            _perdidosPorDia.TryGetValue(dia.Date, out var valor);
            return valor;
        }
    }

    /// <summary>
    /// O total de descartos da sessão do Windows, do jeito mais alto que já
    /// foi possível ler.
    /// </summary>
    private long LerEventosPerdidos()
    {
        var sessao = _sessao;
        if (sessao != null)
        {
            try
            {
                long perdidos = sessao.EventsLost;
                if (perdidos > _eventosPerdidos) _eventosPerdidos = perdidos;
            }
            catch (Exception)
            {
                // A sessão pode já estar morta: o último valor
                // guardado continua valendo — e é o lado seguro
                // (o número só cresce).
            }
        }

        return _eventosPerdidos;
    }

    /// <summary>O dia que a coleta está medindo agora.</summary>
    public DateTime DiaAtual
    {
        get { lock (_tranca) return DiaDeAgora(); }
    }

    /// <summary>
    /// Junta o que já estava salvo (a base) com o que a coleta mediu.
    ///
    /// <para>
    /// A base e a medição são somadas por nome de programa, e não por PID:
    /// o banco guarda o nome (é o que interessa para a tela), e a coleta
    /// guarda o processo. O mesmo programa com vários processos (o Chrome
    /// abre um por aba) vira uma linha só.
    /// </para>
    /// </summary>
    /// <param name="dia">O dia que se quer somar.</param>
    public IReadOnlyList<TrafegoPrograma> Acumulado(DateTime dia)
    {
        lock (_tranca)
        {
            var porNome = new Dictionary<string, (string Nome, long Enviados, long Recebidos)>(StringComparer.OrdinalIgnoreCase);

            // O que a coleta mediu entra primeiro: o nome que fica na tela é
            // o do processo vivo, e não o do que estava salvo antes.
            if (_medido.TryGetValue(dia.Date, out var medido))
            {
                foreach (var par in medido)
                {
                    string nome = NomeEmCache(par.Key);
                    var atual = porNome.TryGetValue(nome, out var v) ? v : (Nome: nome, Enviados: 0L, Recebidos: 0L);
                    porNome[nome] = (atual.Nome, atual.Enviados + par.Value.Enviados, atual.Recebidos + par.Value.Recebidos);
                }
            }

            // E a base entra por cima: o que já estava salvo antes de o app
            // subir é do mesmo dia e não pode sumir.
            if (_base.TryGetValue(dia.Date, out var baseDia))
            {
                foreach (var par in baseDia)
                {
                    var atual = porNome.TryGetValue(par.Key, out var v) ? v : (Nome: par.Key, Enviados: 0L, Recebidos: 0L);
                    porNome[par.Key] = (atual.Nome, atual.Enviados + par.Value.Enviados, atual.Recebidos + par.Value.Recebidos);
                }
            }

            return porNome.Values
                .Select(p => new TrafegoPrograma(p.Nome, p.Enviados, p.Recebidos))
                .ToList();
        }
    }

    /// <summary>O que a coleta mediu hoje, com a base do banco somada.</summary>
    public IReadOnlyList<TrafegoPrograma> Acumulado() => Acumulado(DiaAtual);

    /// <summary>
    /// A base do dia: o total que já estava salvo no banco quando o app
    /// subiu.
    ///
    /// <para>
    /// A base <b>substitui</b> o que estava na base antes (e não soma): o
    /// banco é a fonte da verdade do que já foi salvo, e somar as duas
    /// colocaria o mesmo byte na conta duas vezes.
    /// </para>
    /// </summary>
    /// <param name="dia">O dia da base.</param>
    /// <param name="itens">O que estava salvo no banco para esse dia.</param>
    public void Semear(DateTime dia, IEnumerable<TrafegoPrograma> itens)
    {
        lock (_tranca)
        {
            var agrupado = new Dictionary<string, (long Enviados, long Recebidos)>(StringComparer.OrdinalIgnoreCase);

            foreach (var item in itens)
            {
                string nome = string.IsNullOrWhiteSpace(item.Processo) ? "(sem nome)" : item.Processo.Trim();
                var atual = agrupado.TryGetValue(nome, out var v) ? v : (0L, 0L);
                agrupado[nome] = (atual.Item1 + Math.Max(0, item.Enviados), atual.Item2 + Math.Max(0, item.Recebidos));
            }

            _base[dia.Date] = agrupado;
        }
    }

    /// <summary>
    /// O nome do programa dono do processo.
    ///
    /// <para>
    /// O nome é resolvido na primeira vez que o processo aparece e guardado:
    /// um programa que já fechou não volta para ser perguntado. De tempos em
    /// tempos a conferência é refeita, porque o Windows reusa o número do
    /// processo — sem isso, um programa novo que pegasse o PID de um velho
    /// seria creditado ao nome do velho.
    /// </para>
    ///
    /// <para>
    /// O nome <b>nunca</b> é nulo: um processo que já morreu antes de ser
    /// perguntado devolve "(encerrado)". Um nome nulo no ranking derrubaria
    /// o app (o dicionário não aceita chave nula) — e foi exatamente o que
    /// aconteceu na prova de 09/11/2026.
    /// </para>
    /// </summary>
    internal string NomeDoProcesso(int pid)
    {
        DateTime agora = DateTime.Now;

        // O nome guardado pode ser NULO: um processo que já morreu antes de
        // ser perguntado deixa o cache sem nome nenhum. Sem esta guarda, o
        // nome nulo ia para o dicionário do ranking — e um dicionário não
        // aceita chave nula: a exceção derrubava o app inteiro.
        string nome = "(encerrado)";

        lock (_tranca)
        {
            if (_nomes.TryGetValue(pid, out var guardado))
            {
                if (agora - guardado.Checado < IntervaloDoNome)
                    return string.IsNullOrEmpty(guardado.Nome) ? "(encerrado)" : guardado.Nome;

                if (!string.IsNullOrEmpty(guardado.Nome)) nome = guardado.Nome;
            }
        }

        // A consulta ao Windows fica FORA da trava: ela é lenta, e dentro
        // da trava pararia a fila de pacotes da coleta (o total sairia
        // menor do que foi o real) e a montagem do cartão na tela.
        try
        {
            using var processo = Process.GetProcessById(pid);
            nome = processo.ProcessName;
        }
        catch (ArgumentException)
        {
            // O processo já terminou entre o evento e a hora de perguntar:
            // o nome que já conhecemos continua sendo o melhor palpite.
        }
        catch (InvalidOperationException)
        {
            // Processo sem handle: mesmo caso do anterior.
        }
        catch (Exception)
        {
            // Qualquer outra falha ao perguntar o nome não pode derrubar a
            // coleta: o nome desconhecido vira "(encerrado)" e o byte
            // continua sendo contado.
        }

        lock (_tranca) _nomes[pid] = (nome, agora);
        return nome;
    }

    /// <summary>
    /// O nome do processo, só do que já está guardado.
    ///
    /// <para>
    /// Não pergunta nada ao Windows: a montagem do cartão acontece na
    /// thread da tela, e perguntar o nome de cada processo do dia a cada
    /// leitura travaria a janela. O nome é resolvido quando o pacote chega
    /// (fora do travamento), e a conferência periódica acontece na thread
    /// de fundo.
    /// </para>
    ///
    /// <para>
    /// O nome <b>nunca</b> é nulo: um processo que ninguém perguntou ainda
    /// vira "(desconhecido)".
    /// </para>
    /// </summary>
    internal string NomeEmCache(int pid)
    {
        lock (_tranca)
        {
            return _nomes.TryGetValue(pid, out var guardado) && !string.IsNullOrEmpty(guardado.Nome)
                ? guardado.Nome
                : "(desconhecido)";
        }
    }

    /// <summary>
    /// Anota o motivo de a coleta não estar funcionando.
    ///
    /// <para>
    /// O motivo fica guardado para a tela mostrar. Um extra que faltou não
    /// pode virar um número inventado — e o app precisa saber que o
    /// cartão não está medindo.
    /// </para>
    /// </summary>
    public void AnotarErro(string motivo)
    {
        lock (_tranca) _erro = motivo;
    }

    /// <summary>
    /// Liga a coleta. Nunca lança: uma ETW que não sobe é um extra que
    /// faltou, não um motivo para derrubar o app.
    /// </summary>
    public void Iniciar()
    {
        if (Ativo) return;

        try
        {
            // A opção Create derruba uma sessão antiga com o mesmo nome
            // (o app fechado à força deixa a sessão viva).
            _sessao = new TraceEventSession(NomeSessao, TraceEventSessionOptions.Create) { StopOnDispose = true };
            _sessao.EnableKernelProvider(KernelTraceEventParser.Keywords.NetworkTCPIP);

            _sessao.Source.Kernel.TcpIpSend += e => Somar(e.ProcessID, e.size, envio: true);
            _sessao.Source.Kernel.TcpIpRecv += e => Somar(e.ProcessID, e.size, envio: false);
            _sessao.Source.Kernel.TcpIpSendIPV6 += e => Somar(e.ProcessID, e.size, envio: true);
            _sessao.Source.Kernel.TcpIpRecvIPV6 += e => Somar(e.ProcessID, e.size, envio: false);
            _sessao.Source.Kernel.UdpIpSend += e => Somar(e.ProcessID, e.size, envio: true);
            _sessao.Source.Kernel.UdpIpRecv += e => Somar(e.ProcessID, e.size, envio: false);
            _sessao.Source.Kernel.UdpIpSendIPV6 += e => Somar(e.ProcessID, e.size, envio: true);
            _sessao.Source.Kernel.UdpIpRecvIPV6 += e => Somar(e.ProcessID, e.size, envio: false);

            // O erro é limpo ANTES de a thread começar: se a sessão morresse
            // logo no primeiro instante, o motivo dela seria gravado
            // primeiro e apagado logo depois pela linha de baixo — e a
            // tela ficaria mostrando que está medindo, sem medir nada.
            lock (_tranca) _erro = null;

            _thread = new Thread(() =>
            {
                try { _sessao!.Source.Process(); }
                catch (Exception ex)
                {
                    // A sessão morreu no meio: o motivo fica para a tela.
                    lock (_tranca) _erro = "A coleta de tráfego parou: " + ex.Message;
                }
            })
            { IsBackground = true, Name = "ETW trafego" };
            _thread.Start();

            // A conferência dos nomes roda na thread de fundo: perguntar ao
            // Windows o nome de cada processo na thread da tela travaria a
            // janela a cada leitura.
            _pararConferencia = false;
            _threadConferencia = new Thread(ConferirNomes)
            { IsBackground = true, Name = "conferencia de nomes" };
            _threadConferencia.Start();
        }
        catch (UnauthorizedAccessException)
        {
            lock (_tranca)
            {
                _erro = "A leitura do tráfego precisa de administrador. O TRANAL SENTINEL já pede administrador ao abrir — se ele foi iniciado sem, o cartão TRANAL REDE fica sem medir o tráfego.";
            }
            DescartarSessao();
        }
        catch (Exception ex)
        {
            lock (_tranca)
            {
                _erro = "Não foi possível ligar a leitura do tráfego: " + ex.Message +
                    " (outra ferramenta usando o log do kernel do Windows também impede a leitura).";
            }
            DescartarSessao();
        }
    }

    /// <summary>Some os bytes de um pacote ao processo que os trocou.</summary>
    private void Somar(int pid, int tamanho, bool envio)
    {
        if (pid <= 0 || tamanho <= 0) return;

        bool nomeDesconhecido;

        lock (_tranca)
        {
            DateTime dia = DiaDeAgora();

            if (!_medido.TryGetValue(dia, out var doDia))
            {
                doDia = new Dictionary<int, (long Enviados, long Recebidos)>();
                _medido[dia] = doDia;
            }

            var atual = doDia.TryGetValue(pid, out var v) ? v : (0L, 0L);

            doDia[pid] = envio
                ? (atual.Item1 + tamanho, atual.Item2)
                : (atual.Item1, atual.Item2 + tamanho);

            // O nome é resolvido na primeira vez (o processo pode fechar
            // depois, e aí não há mais quem pergunte).
            nomeDesconhecido = !_nomes.ContainsKey(pid);
        }

        // A pergunta ao Windows sobre o processo fica FORA do travamento:
        // dentro dele, uma leitura lenta pararia a fila de pacotes da
        // coleta — e o total sairia menor do que foi o real, sem aviso.
        if (nomeDesconhecido) NomeDoProcesso(pid);
    }

    /// <summary>
    /// O dia de agora, conferido uma vez por segundo.
    ///
    /// <para>
    /// Conferir a data a cada pacote seria desperdício: o dia muda uma vez
    /// por dia, e a sessão de ETW passa por centenas de milhares de pacotes
    /// no dia.
    /// </para>
    /// </summary>
    private DateTime DiaDeAgora()
    {
        DateTime agora = DateTime.Now;

        if (agora - _diaChecadoEm >= TimeSpan.FromSeconds(1))
        {
            _diaChecadoEm = agora;
            _dia = agora.Date;
        }

        return _dia;
    }

    /// <summary>
    /// Confere de tempos em tempos o nome dos processos que já foram vistos.
    ///
    /// <para>
    /// O Windows reusa o número do processo: um programa novo que pegasse o
    /// PID de um velho continuaria sendo creditado ao nome do velho. A
    /// conferência acontece na thread de fundo — e só olha os processos do
    /// dia de agora, que são os que ainda podem estar vivos.
    /// </para>
    /// </summary>
    private void ConferirNomes()
    {
        while (!_pararConferencia)
        {
            try { Thread.Sleep(IntervaloDaConferencia); }
            catch (ThreadInterruptedException) { return; }

            if (_pararConferencia) return;

            int[] pids;
            lock (_tranca)
            {
                pids = _medido.TryGetValue(DiaDeAgora(), out var doDia)
                    ? doDia.Keys.ToArray()
                    : Array.Empty<int>();
            }

            foreach (int pid in pids)
            {
                if (_pararConferencia) return;
                NomeDoProcesso(pid);
            }
        }
    }

    /// <summary>Desliga a coleta e fecha a sessão ETW.</summary>
    public void Parar()
    {
        _pararConferencia = true;

        TraceEventSession? sessao = _sessao;
        _sessao = null;
        Thread? thread = _thread;
        _thread = null;

        // A fila do kernel ainda tem pacotes que ainda não foram entregues
        // (medido: sem esta espera, o número da coleta fica menor do que o
        // real). A espera é curta e acontece só quando o app está fechando.
        try { Thread.Sleep(DrenagemAoParar); }
        catch (ThreadInterruptedException)
        {
            // O app está fechando: a drenagem pode ser interrompida.
        }

        try { sessao?.Source.StopProcessing(); }
        catch (Exception) { /* a sessão já pode estar morta: nada a fazer */ }

        // A thread que processa os eventos precisa sair antes de a sessão
        // ser descartada — senão ela continua lendo um handle morto.
        try { thread?.Join(TimeSpan.FromSeconds(2)); }
        catch (Exception) { /* a thread já pode ter terminado */ }

        try { sessao?.Dispose(); }
        catch (Exception) { /* a sessão já pode estar morta: nada a fazer */ }
    }

    private void DescartarSessao()
    {
        try { _sessao?.Dispose(); } catch (Exception) { /* já estava descartada */ }
        _sessao = null;
        _thread = null;
    }

    /// <summary>Fecha a coleta.</summary>
    public void Dispose() => Parar();
}
