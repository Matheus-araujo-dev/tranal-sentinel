using System.Globalization;
using Xunit;

namespace TranalSentinel.Tests;

/// <summary>
/// Testes da regra e da vigia da tecla presa.
///
/// <para>
/// A regra é pura e a vigia recebe tudo por função (relógio monotônico,
/// leitura do teclado, silêncio, repetição e o "soltar"), então dá para
/// medir o comportamento inteiro sem Windows em cima.
/// </para>
/// </summary>
public class TeclaPresaTestes
{
    private static Modificadora Shift() => new(0xA1, "Shift direito", "Shift");

    private static Modificadora Ctrl() => new(0x11, "Ctrl", "Ctrl");

    // ------------------------------------------------------------------ regra

    [Fact]
    public void TeclaNaoPressionada_NaoEstaPresa()
    {
        Assert.False(TeclaPresaRegras.EstaPresa(
            pressionada: false,
            repeticaoDoTecladoLigada: true,
            atividadeConhecida: true,
            haQuantoEstaCaiada: TimeSpan.FromMinutes(10),
            semAtividadeDeTeclado: TimeSpan.FromMinutes(10)));
    }

    [Fact]
    public void SemAtividadeConhecida_NaoCorrige()
    {
        // Sem o gancho não se sabe se o silêncio é real: soltar a tecla que o
        // dono está segurando é pior do que a tecla presa.
        Assert.False(TeclaPresaRegras.EstaPresa(
            pressionada: true,
            repeticaoDoTecladoLigada: true,
            atividadeConhecida: false,
            haQuantoEstaCaiada: TimeSpan.FromMinutes(10),
            semAtividadeDeTeclado: TimeSpan.FromMinutes(10)));
    }

    [Fact]
    public void RepeticaoDoTecladoDesligada_NaoCorrige()
    {
        // Sem repetição automática, alguém segurando a tecla também não gera
        // evento: silêncio não distingue "preso" de "segurando".
        Assert.False(TeclaPresaRegras.EstaPresa(
            pressionada: true,
            repeticaoDoTecladoLigada: false,
            atividadeConhecida: true,
            haQuantoEstaCaiada: TimeSpan.FromMinutes(10),
            semAtividadeDeTeclado: TimeSpan.FromMinutes(10)));
    }

    [Fact]
    public void TeclaCaiadaHaPocoTempo_NaoEstaPresa()
    {
        Assert.False(TeclaPresaRegras.EstaPresa(
            pressionada: true,
            repeticaoDoTecladoLigada: true,
            atividadeConhecida: true,
            haQuantoEstaCaiada: TimeSpan.FromSeconds(29),
            semAtividadeDeTeclado: TimeSpan.FromMinutes(10)));
    }

    [Fact]
    public void TecladoComAtividadeRecente_NaoEstaPresa()
    {
        // Quem está segurando a tecla continua mandando repetição: o teclado
        // não está em silêncio, logo a tecla não está presa.
        Assert.False(TeclaPresaRegras.EstaPresa(
            pressionada: true,
            repeticaoDoTecladoLigada: true,
            atividadeConhecida: true,
            haQuantoEstaCaiada: TimeSpan.FromMinutes(10),
            semAtividadeDeTeclado: TimeSpan.FromSeconds(3)));
    }

    [Fact]
    public void TeclaCaiadaComTecladoEmSilencio_EstaPresa()
    {
        Assert.True(TeclaPresaRegras.EstaPresa(
            pressionada: true,
            repeticaoDoTecladoLigada: true,
            atividadeConhecida: true,
            haQuantoEstaCaiada: TimeSpan.FromSeconds(31),
            semAtividadeDeTeclado: TimeSpan.FromSeconds(31)));
    }

    [Fact]
    public void NoLimiteExato_EstaPresa()
    {
        // 30 s é o combinado: no limite, a tecla conta como presa.
        Assert.True(TeclaPresaRegras.EstaPresa(
            pressionada: true,
            repeticaoDoTecladoLigada: true,
            atividadeConhecida: true,
            haQuantoEstaCaiada: TimeSpan.FromSeconds(TeclaPresaRegras.SegundosMinimos),
            semAtividadeDeTeclado: TimeSpan.FromSeconds(TeclaPresaRegras.SegundosMinimos)));
    }

    [Fact]
    public void AsOnzeModificadoresEstaoNoCatalogo()
    {
        // A genérica e as duas laterais de cada família: o "soltar" precisa
        // chegar em todas para o estado interno largar a tecla.
        Assert.Equal(11, TeclaPresaRegras.Modificadoras.Count);
        Assert.Contains(TeclaPresaRegras.Modificadoras, m => m.Vk == 0x10 && m.Familia == "Shift");
        Assert.Contains(TeclaPresaRegras.Modificadoras, m => m.Vk == 0xA0 && m.Familia == "Shift");
        Assert.Contains(TeclaPresaRegras.Modificadoras, m => m.Vk == 0xA1 && m.Familia == "Shift");
        Assert.Contains(TeclaPresaRegras.Modificadoras, m => m.Vk == 0x11 && m.Familia == "Ctrl");
        Assert.Contains(TeclaPresaRegras.Modificadoras, m => m.Vk == 0xA2 && m.Familia == "Ctrl");
        Assert.Contains(TeclaPresaRegras.Modificadoras, m => m.Vk == 0xA3 && m.Familia == "Ctrl");
        Assert.Contains(TeclaPresaRegras.Modificadoras, m => m.Vk == 0x12 && m.Familia == "Alt");
        Assert.Contains(TeclaPresaRegras.Modificadoras, m => m.Vk == 0xA4 && m.Familia == "Alt");
        Assert.Contains(TeclaPresaRegras.Modificadoras, m => m.Vk == 0xA5 && m.Familia == "Alt");
        Assert.Contains(TeclaPresaRegras.Modificadoras, m => m.Vk == 0x5B && m.Familia == "Win");
        Assert.Contains(TeclaPresaRegras.Modificadoras, m => m.Vk == 0x5C && m.Familia == "Win");
    }

    [Fact]
    public void VksDaFamilia_PegaAGenericaEAsLaterais()
    {
        // O Windows marca a genérica junto com a lateral: soltar só uma
        // delas deixa o estado interno marcado.
        Assert.Equal(new[] { 0x10, 0xA0, 0xA1 }, TeclaPresaRegras.VksDaFamilia("Shift"));
        Assert.Equal(new[] { 0x11, 0xA2, 0xA3 }, TeclaPresaRegras.VksDaFamilia("Ctrl"));
    }

    [Fact]
    public void Familias_UneasSemRepetir()
    {
        var teclas = new[]
        {
            new Modificadora(0x10, "Shift", "Shift"),
            new Modificadora(0xA1, "Shift direito", "Shift"),
            new Modificadora(0x11, "Ctrl", "Ctrl"),
        };

        Assert.Equal(new[] { "Shift", "Ctrl" }, TeclaPresaRegras.ListaDeFamilias(teclas));
        Assert.Equal("Shift e Ctrl", TeclaPresaRegras.Families(teclas));
    }

    [Fact]
    public void TextoDaCorrecao_ConcordaComONumeroDeTeclas()
    {
        Assert.Equal("A tecla Shift direito ficou presa e foi solta",
            TeclaPresaRegras.TextoDaCorrecao(new[] { "Shift direito" }));

        Assert.Equal("As teclas Shift direito e Ctrl ficaram presas e foram soltas",
            TeclaPresaRegras.TextoDaCorrecao(new[] { "Shift direito", "Ctrl" }));
    }

    [Fact]
    public void TextoDoAviso_NaoDependeDaCulturaDoWindows()
    {
        var cultura = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("en-US");

            // 1.234 s: com o milhar trocado à mão (o NumeroTexto), o texto sai
            // igual em qualquer cultura. Com o formato da cultura, sairia
            // "1,234" — e o teste pegaria.
            string texto = TeclaPresaRegras.TextoDoAviso(new[] { "Shift" }, TimeSpan.FromSeconds(1234));

            Assert.Contains("1.234", texto);
            Assert.Contains("Shift", texto);
        }
        finally
        {
            CultureInfo.CurrentCulture = cultura;
        }
    }

    [Fact]
    public void RepeticaoDoTeclado_SemValorNoRegistro_ValhaOPadraoDoWindows()
    {
        // O Windows nem sempre grava o valor (no PC do dono ele só estava no
        // perfil padrão). Sem valor, vale o padrão: a repetição ligada.
        Assert.True(TeclaPresaRegras.RepeticaoDoTecladoLigada(null));
    }

    [Fact]
    public void RepeticaoDoTeclado_DesligadaNoRegistro_NaoAge()
    {
        Assert.False(TeclaPresaRegras.RepeticaoDoTecladoLigada(0));
    }

    [Fact]
    public void RepeticaoDoTeclado_LigadaNoRegistro_Age()
    {
        Assert.True(TeclaPresaRegras.RepeticaoDoTecladoLigada(31));
    }

    // ---------------------------------------------------------------- vigia

    private sealed class Ambiente
    {
        /// <summary>Relógio monotônico da vigia, em milissegundos.</summary>
        public long Agora;

        public IReadOnlyList<Modificadora> Estado = Array.Empty<Modificadora>();
        public TimeSpan? Silencio = TimeSpan.FromMinutes(10);
        public bool Repeticao = true;
        public bool LerFalha;
        public bool SoltarFalha;

        /// <summary>Simula o "soltar" que não limpa o estado (tecla continua presa).</summary>
        public bool SoltarLimpaEstado = true;

        /// <summary>
        /// Regras de "soltar" por tecla: a que devolver true é solta, a outra
        /// continua presa (o "soltar" pode limpar uma família e não a outra).
        /// </summary>
        public Func<int, bool>? SoltarLimpaPorVk;

        public List<int> Soltadas = new();
        public List<TeclaPresa> Corrigidas = new();
        public List<TeclaPresa> SemSucesso = new();
        public MonitorTeclaPresa Monitor = null!;

        /// <summary>O gancho de teclado está vivo?</summary>
        public bool GanchoVivo = true;

        /// <summary>O dono ainda quer a correção automática?</summary>
        public bool PodeAgir = true;

        public Ambiente()
        {
            Monitor = new MonitorTeclaPresa(
                () => LerFalha ? throw new InvalidOperationException("falhou") : Estado,
                () => Agora,
                () => Silencio,
                () => Repeticao,
                vks =>
                {
                    if (SoltarFalha) throw new InvalidOperationException("falhou ao soltar");
                    Soltadas.AddRange(vks);
                    if (SoltarLimpaPorVk is not null)
                    {
                        Estado = Estado.Where(m => !SoltarLimpaPorVk(m.Vk)).ToList();
                        return;
                    }
                    if (SoltarLimpaEstado) Estado = Array.Empty<Modificadora>();
                },
                () => GanchoVivo,
                () => PodeAgir);
            Monitor.Corrigida += tecla => Corrigidas.Add(tecla);
            Monitor.NaoConseguiu += tecla => SemSucesso.Add(tecla);
        }

        /// <summary>Avança o relógio e roda um ciclo da vigia.</summary>
        public void Ciclo(int segundos)
        {
            Agora += segundos * 1000L;
            Monitor.Verificar();
        }
    }

    [Fact]
    public void Vigia_NaoSoltaTeclaRecemCaida()
    {
        var ambiente = new Ambiente { Estado = new[] { Shift() } };

        ambiente.Ciclo(5);

        Assert.Empty(ambiente.Soltadas);
        Assert.Empty(ambiente.Corrigidas);
    }

    [Fact]
    public void Vigia_SoltaATeclaPresaEAvisa()
    {
        var ambiente = new Ambiente { Estado = new[] { Shift() } };

        ambiente.Ciclo(5);
        ambiente.Ciclo(31);

        // O "soltar" chega nas três teclas da família (a genérica e as duas
        // laterais): soltar só a que foi vista deixa o estado marcado.
        Assert.Equal(new[] { 0x10, 0xA0, 0xA1 }, ambiente.Soltadas);
        Assert.Single(ambiente.Corrigidas);
        Assert.Equal(new[] { "Shift" }, ambiente.Corrigidas[0].Familias);
        Assert.Equal(TimeSpan.FromSeconds(31), ambiente.Corrigidas[0].HaQuanto);
    }

    [Fact]
    public void Vigia_NaoSoltaQuandoOTecladoTeveAtividade()
    {
        var ambiente = new Ambiente { Estado = new[] { Shift() }, Silencio = TimeSpan.FromSeconds(2) };

        ambiente.Ciclo(5);
        ambiente.Ciclo(31);

        Assert.Empty(ambiente.Soltadas);
        Assert.Empty(ambiente.Corrigidas);
    }

    [Fact]
    public void Vigia_NaoSoltaQuandoAAtividadeNaoEDeSabida()
    {
        var ambiente = new Ambiente { Estado = new[] { Shift() }, Silencio = null };

        ambiente.Ciclo(5);
        ambiente.Ciclo(31);

        Assert.Empty(ambiente.Soltadas);
    }

    [Fact]
    public void Vigia_NaoSoltaQuandoARepeticaoEstaDesligada()
    {
        var ambiente = new Ambiente { Estado = new[] { Shift() }, Repeticao = false };

        ambiente.Ciclo(5);
        ambiente.Ciclo(31);

        Assert.Empty(ambiente.Soltadas);
    }

    [Fact]
    public void Vigia_TeclaQueSubiuNaoECorrigida()
    {
        var ambiente = new Ambiente { Estado = new[] { Shift() } };

        ambiente.Ciclo(5);
        ambiente.Estado = Array.Empty<Modificadora>();
        ambiente.Ciclo(31);

        Assert.Empty(ambiente.Soltadas);
        Assert.Empty(ambiente.Corrigidas);
    }

    [Fact]
    public void Vigia_NaoRepeteACorrecaoLogoEmSeguida()
    {
        // O "soltar" não resolve de primeira: a tecla continua presa. É
        // justamente aí que a espera de 1 min entra (com a tecla sempre caída,
        // a condição dos 30 s continua valendo). O app avisa que tentou
        // e não conseguiu — não que soltou.
        var ambiente = new Ambiente { Estado = new[] { Shift() }, SoltarLimpaEstado = false };

        ambiente.Ciclo(5);
        ambiente.Ciclo(31);
        Assert.Single(ambiente.SemSucesso);

        ambiente.Ciclo(31);
        Assert.Single(ambiente.SemSucesso);

        // Passados 51 s da tentativa, a espera de 1 min ainda segura.
        ambiente.Ciclo(20);
        Assert.Single(ambiente.SemSucesso);

        // Passado o minuto, a vigia tenta de novo.
        ambiente.Ciclo(20);
        Assert.Equal(2, ambiente.SemSucesso.Count);
    }

    [Fact]
    public void Vigia_QuandoOSoltarNaoLimpaNaoDizQueSoltou()
    {
        // O app não pode anunciar "soltou a tecla" quando o estado continua
        // marcado: ele diz que tentou e não conseguiu.
        var ambiente = new Ambiente { Estado = new[] { Shift() }, SoltarLimpaEstado = false };

        ambiente.Ciclo(5);
        ambiente.Ciclo(31);

        Assert.Empty(ambiente.Corrigidas);
        Assert.Single(ambiente.SemSucesso);
        Assert.Equal(new[] { "Shift" }, ambiente.SemSucesso[0].Familias);
    }

    [Fact]
    public void Vigia_FalhaEmLerONaoDerruba()
    {
        var ambiente = new Ambiente { LerFalha = true };

        ambiente.Ciclo(5);
        ambiente.Ciclo(31);

        Assert.Empty(ambiente.Soltadas);
    }

    [Fact]
    public void Vigia_FalhaAoSoltarAvisaEEspera()
    {
        // O "soltar" lançou exceção: o app não diz que soltou, avisa que
        // não conseguiu e espera como nas outras tentativas — senão ele
        // tentaria a cada 5 segundos, para sempre, sem ninguém ver.
        var ambiente = new Ambiente { Estado = new[] { Shift() }, SoltarFalha = true };

        ambiente.Ciclo(5);
        ambiente.Ciclo(31);

        Assert.Empty(ambiente.Corrigidas);
        Assert.Single(ambiente.SemSucesso);
        Assert.Empty(ambiente.Soltadas);

        // A espera de 1 minuto vale para as tentativas que falharam também.
        ambiente.Ciclo(31);
        Assert.Single(ambiente.SemSucesso);
    }

    [Fact]
    public void Vigia_NaoSoltaQuandoODonoDesligou()
    {
        // O dono desliga a correção enquanto a vigia espera a prova do
        // gancho: na hora de soltar, o app pergunta de novo e obedece.
        var ambiente = new Ambiente { Estado = new[] { Shift() } };

        ambiente.Ciclo(5);
        ambiente.PodeAgir = false;
        ambiente.Ciclo(31);

        Assert.Empty(ambiente.Soltadas);
        Assert.Empty(ambiente.Corrigidas);
        Assert.Empty(ambiente.SemSucesso);
    }

    [Fact]
    public void OTextoDoAvisoConcordaComODNumeroDeTeclas()
    {
        string uma = TeclaPresaRegras.TextoDoAviso(new[] { "Shift direito" }, TimeSpan.FromSeconds(31));
        Assert.Contains("a tecla Shift direito pressionada", uma);
        Assert.Contains("soltou a tecla", uma);

        string duas = TeclaPresaRegras.TextoDoAviso(new[] { "Shift direito", "Ctrl" }, TimeSpan.FromSeconds(31));
        Assert.Contains("as teclas Shift direito e Ctrl pressionadas", duas);
        Assert.Contains("soltou as teclas", duas);
    }

    [Fact]
    public void Vigia_ContaAsCorrecoes()
    {
        var ambiente = new Ambiente { Estado = new[] { Shift() } };

        ambiente.Ciclo(5);
        ambiente.Ciclo(31);

        Assert.Equal(1, ambiente.Monitor.CorrecoesFeitas);
    }

    [Fact]
    public void Vigia_DepoisDeSoltarComSucessoUmNovoTravamentoNaoEsperaOMinuto()
    {
        // Medido no PC do dono em 16/11/2026: a segunda travada, logo depois
        // da primeira, ficava 5 minutos sem tratamento nenhum — a família
        // ficava esperando e o mouse continuava com o Shift grudado. A
        // espera de 1 min vale para as tentativas que FALHARAM.
        var ambiente = new Ambiente { Estado = new[] { Shift() } };

        ambiente.Ciclo(5);
        ambiente.Ciclo(31);
        Assert.Single(ambiente.Corrigidas);

        // A tecla trava de novo: o app tem que soltar de novo no tempo
        // normal (30 s de tecla caída), sem esperar o minuto de espera.
        ambiente.Estado = new[] { Shift() };
        ambiente.Ciclo(31);
        Assert.Single(ambiente.Corrigidas);   // ainda dentro dos 30 s

        ambiente.Ciclo(31);
        Assert.Equal(2, ambiente.Corrigidas.Count);
    }

    [Fact]
    public void Vigia_AEsperaDe1MinutoMORREComAFamiliaCerta()
    {
        // Corrigir o Shift não pode prender o Ctrl: a espera é por família.
        var ambiente = new Ambiente { Estado = new[] { Shift() }, SoltarLimpaEstado = false };

        ambiente.Ciclo(5);
        ambiente.Ciclo(31);
        Assert.Single(ambiente.SemSucesso);

        // Agora é o Ctrl que está preso: ele não espera a espera do Shift.
        ambiente.Estado = new[] { new Modificadora(0x11, "Ctrl", "Ctrl") };
        ambiente.Ciclo(31);      // o app vê o Ctrl pela primeira vez
        ambiente.Ciclo(31);      // 31 s depois: a correção

        Assert.Equal(2, ambiente.SemSucesso.Count);
        Assert.Equal(new[] { "Ctrl" }, ambiente.SemSucesso[1].Familias);
    }

    [Fact]
    public void Vigia_OTempoVemDoRelogioMonotonico()
    {
        // O relógio de parede (que salta quando a hora é corrigida) não entra
        // na conta do tempo: a vigia mede pelo relógio que só anda para frente.
        // Por isso o registro do evento não carrega nenhum instante de parede:
        // se um dia ele voltar, a volta da hora voltaria junto.
        Assert.DoesNotContain(
            typeof(TeclaPresa).GetProperties(),
            p => p.PropertyType == typeof(DateTime) || p.PropertyType == typeof(DateTime?));
    }

    // ------------------------------------------------------- o que o dono lê

    [Fact]
    public void OTextoDoAvisoDizQueOTempoEUmPiso()
    {
        // O app começa a vigiar depois que a tecla ficou presa: o tempo que
        // ele viu é o mínimo, não o tempo real.
        string texto = TeclaPresaRegras.TextoDoAviso(new[] { "Shift direito" }, TimeSpan.FromSeconds(31));

        Assert.Contains("pelo menos", texto);
        Assert.Contains("31 s", texto);
    }

    [Fact]
    public void OTextoDoAvisoNomeiaATeclaQueFicouPresa()
    {
        // No PC do dono a tecla que grudou foi a **direita**: o aviso tem que
        // dizer qual foi.
        string texto = TeclaPresaRegras.TextoDoAviso(new[] { "Shift direito" }, TimeSpan.FromSeconds(31));

        Assert.Contains("Shift direito", texto);
        Assert.DoesNotContain("\"", texto);
    }

    [Fact]
    public void OAvisoEDiarioUsamOMesmoNumero()
    {
        // O mesmo evento não pode sair com dois números. O número vem de um
        // lugar só — e é o PISO do tempo, porque o texto diz "pelo menos":
        // "pelo menos 31 s" seria falso para uma tecla caída há 30,6 s.
        Assert.Equal(30, TeclaPresaRegras.SegundosDoAviso(TimeSpan.FromSeconds(30.6)));
        Assert.Equal(30, TeclaPresaRegras.SegundosDoAviso(TimeSpan.FromSeconds(30.4)));

        string texto = TeclaPresaRegras.TextoDoAviso(new[] { "Shift direito" }, TimeSpan.FromSeconds(30.6));
        Assert.Contains("30 s", texto);
        Assert.DoesNotContain("31 s", texto);
    }

    [Fact]
    public void OTextoDaTentativaNomeiaATecla()
    {
        Assert.Equal("Tentativa de soltar a tecla Shift direito, que continuou presa",
            TeclaPresaRegras.TextoDaTentativa(new[] { "Shift direito" }));
    }

    // ---------------------------------------------------------- o gancho

    [Fact]
    public void AsEstendidasSaoSoAsQuatroDeVerdade()
    {
        // O Shift direito NÃO é estendida (scancode 0x36, sem o prefixo E0):
        // colocá-lo na lista quebraria justamente o caso que o app existe
        // para resolver (o "soltar" viraria E0 36, que não é tecla nenhuma).
        Assert.True(TeclasWindows.EhEstendida(0xA3));   // Ctrl direito
        Assert.True(TeclasWindows.EhEstendida(0xA5));   // Alt direito
        Assert.True(TeclasWindows.EhEstendida(0x5B));   // Win
        Assert.True(TeclasWindows.EhEstendida(0x5C));   // Win direito

        Assert.False(TeclasWindows.EhEstendida(0xA1));  // Shift direito
        Assert.False(TeclasWindows.EhEstendida(0xA0));  // Shift esquerdo
        Assert.False(TeclasWindows.EhEstendida(0x10));  // Shift
        Assert.False(TeclasWindows.EhEstendida(0x11));  // Ctrl
        Assert.False(TeclasWindows.EhEstendida(0x12));  // Alt
    }

    [Fact]
    public void OGanchoDeTecladoInstalaESai()
    {
        var vigia = new VigiaTeclado();
        try
        {
            bool instalado = vigia.Instalar();
            Assert.Equal(instalado, vigia.Instalado);

            // O Windows pode recusar o gancho (aí não há o que medir).
            if (!instalado) return;

            Assert.NotNull(vigia.Silencio);
        }
        finally
        {
            vigia.Remover();
        }

        // O gancho não pode ficar instalado depois de sair.
        Assert.False(vigia.Instalado);
        Assert.Null(vigia.Silencio);
    }

    [Fact]
    public void ASondaDeVidaDescobreQueOGanchoEstaVivo()
    {
        // A sonda injeta o "soltar" de F24 (tecla que ninguém usa) e confere
        // se o gancho viu o evento. Se o Windows matou o gancho, a sonda não
        // é vista — e o app repara o gancho em vez de soltar a tecla que o
        // dono está segurando.
        var vigia = new VigiaTeclado();
        try
        {
            // O Windows pode recusar o gancho (num ambiente sem sessão de
            // desktop, por exemplo). Aí não há o que medir — e o caminho
            // real da sonda é provado pelo harness de ponta a ponta.
            if (!vigia.Instalar()) return;

            Assert.True(vigia.EstaVivo());

            // A segunda chamada usa o resultado guardado (a sonda não é
            // repetida a cada 5 segundos).
            Assert.True(vigia.EstaVivo());
        }
        finally
        {
            vigia.Remover();
        }

        // Sem gancho, a sonda não tem como dizer que ele está vivo.
        Assert.False(vigia.EstaVivo());
    }

    [Fact]
    public void OReparoDoGanchoInstalaUmGanchoNovo()
    {
        // O caminho do gancho morto: o app desinstala e instala de novo, no
        // mesmo objeto. O gancho novo precisa estar vivo (é ele que vai
        // medir o silêncio de novo).
        var vigia = new VigiaTeclado();
        try
        {
            if (!vigia.Instalar()) return;

            vigia.Remover();
            Assert.False(vigia.Instalado);

            Assert.True(vigia.Instalar());
            Assert.True(vigia.Instalado);
            Assert.True(vigia.EstaVivo());
        }
        finally
        {
            vigia.Remover();
        }
    }

    // ------------------------------------------------------- a vigia

    [Fact]
    public void Vigia_NaoSoltaComOGanchoMorto()
    {
        // O Windows remove um gancho de baixo nível em silêncio quando o PC
        // trava: aí o "silêncio" do teclado é falso e o app soltaria a tecla
        // que o dono está segurando. Sem prova de vida, o app se cala.
        var ambiente = new Ambiente { Estado = new[] { Shift() }, GanchoVivo = false };

        ambiente.Ciclo(5);
        ambiente.Ciclo(31);

        Assert.Empty(ambiente.Soltadas);
        Assert.Empty(ambiente.Corrigidas);
        Assert.Empty(ambiente.SemSucesso);
    }

    [Fact]
    public void ONomeDaTeclaNaoRepeteAGenerica()
    {
        // O Windows marca o Shift genérico junto com o direito: o aviso
        // tem que dizer "Shift direito", e não "Shift e Shift direito".
        var ambiente = new Ambiente
        {
            Estado = new[] { new Modificadora(0x10, "Shift", "Shift"), Shift() }
        };

        ambiente.Ciclo(5);
        ambiente.Ciclo(31);

        Assert.Single(ambiente.Corrigidas);
        Assert.Equal(new[] { "Shift direito" }, ambiente.Corrigidas[0].Teclas);
    }

    [Fact]
    public void Vigia_DizQualTeclaFicouPresa()
    {
        var ambiente = new Ambiente { Estado = new[] { Shift() } };

        ambiente.Ciclo(5);
        ambiente.Ciclo(31);

        Assert.Single(ambiente.Corrigidas);
        Assert.Equal(new[] { "Shift direito" }, ambiente.Corrigidas[0].Teclas);
        Assert.Equal(new[] { "Shift" }, ambiente.Corrigidas[0].Familias);
    }

    [Fact]
    public void Vigia_ComDuasTeclasUsaOTempoMaisConservador()
    {
        // O Shift caiu há 40 s e o Ctrl há 30 s: o aviso diz "pelo menos
        // 30 s" — o número precisa valer para as duas teclas do aviso.
        var ambiente = new Ambiente { Estado = new[] { Shift() }, Silencio = TimeSpan.FromSeconds(2) };

        ambiente.Ciclo(5);                       // o Shift aparece (t = 5)
        ambiente.Estado = new[] { Shift(), Ctrl() };
        ambiente.Silencio = TimeSpan.FromSeconds(20);
        ambiente.Ciclo(10);                      // o Ctrl aparece (t = 15), silêncio curto
        ambiente.Silencio = TimeSpan.FromSeconds(60);
        ambiente.Ciclo(30);                      // t = 45: as duas caíram bastante

        Assert.Single(ambiente.Corrigidas);
        Assert.Equal(new[] { "Shift direito", "Ctrl" }, ambiente.Corrigidas[0].Teclas);
        Assert.Equal(TimeSpan.FromSeconds(30), ambiente.Corrigidas[0].HaQuanto);
    }

    [Fact]
    public void Vigia_AvisaNoMaximoTresVezes()
    {
        // Tecla que não solta: o aviso não pode virar spam (a cada minuto,
        // para sempre). Depois de três avisos o app cala — e continua
        // tentando soltar. O que cala é o aviso na tela: o diário continua
        // registrando (o histórico é dele).
        var ambiente = new Ambiente { Estado = new[] { Shift() }, SoltarLimpaEstado = false };

        ambiente.Ciclo(5);
        ambiente.Ciclo(31);
        Assert.Single(ambiente.SemSucesso);
        Assert.True(ambiente.SemSucesso[0].Avisar);

        ambiente.Ciclo(60);
        Assert.Equal(2, ambiente.SemSucesso.Count);
        Assert.True(ambiente.SemSucesso[1].Avisar);

        ambiente.Ciclo(60);
        Assert.Equal(3, ambiente.SemSucesso.Count);
        Assert.True(ambiente.SemSucesso[2].Avisar);

        ambiente.Ciclo(60);
        Assert.Equal(4, ambiente.SemSucesso.Count);          // o evento sai sempre
        Assert.False(ambiente.SemSucesso[3].Avisar);         // mas o aviso para

        // ... mas o app continua tentando soltar (a família inteira: 3 teclas).
        Assert.Equal(12, ambiente.Soltadas.Count);
        Assert.Equal(4, ambiente.Monitor.CorrecoesFeitas);
    }

    [Fact]
    public void Vigia_NaoDespejaAvisoQuandoATeclaVoltaATravarSozinho()
    {
        // Caso patológico (medido pelo red team em 16/11/2026): o Windows
        // re-marca a tecla logo depois de o app soltar. O app continua
        // soltando (a tecla presa precisa ser solta), mas o aviso na tela
        // tem teto — senão era um aviso a cada 35 segundos, durante a
        // sessão inteira.
        var ambiente = new Ambiente { Estado = new[] { Shift() } };

        ambiente.Ciclo(5);
        ambiente.Ciclo(31);
        Assert.True(ambiente.Corrigidas[0].Avisar);

        // A tecla volta a travar logo depois, cinco vezes.
        for (int i = 0; i < 5; i++)
        {
            ambiente.Estado = new[] { Shift() };
            ambiente.Ciclo(31);      // o app vê a tecla de novo
            ambiente.Ciclo(31);      // 31 s depois: solta de novo
        }

        Assert.Equal(6, ambiente.Corrigidas.Count);                // o app continua soltando
        Assert.Equal(3, ambiente.Corrigidas.Count(t => t.Avisar));  // mas o aviso para
    }

    [Fact]
    public void Vigia_DepoisDeUmaFalhaEUmSucessoATeclaQueVoltaNaoEspera()
    {
        // A tentativa falhou (a família ficou esperando). Passado o minuto
        // de espera, a tentativa seguinte deu certo — e a tecla voltou a
        // travar antes da rodada seguinte (o app nunca a viu subir). O
        // travamento novo tem que ser tratado no tempo normal: a falha
        // antiga não pode continuar esperando.
        var ambiente = new Ambiente { Estado = new[] { Shift() }, SoltarLimpaEstado = false };

        ambiente.Ciclo(5);
        ambiente.Ciclo(31);
        Assert.Single(ambiente.SemSucesso);

        // Passado o minuto de espera, a vigia tenta de novo e agora solta.
        ambiente.SoltarLimpaEstado = true;
        ambiente.Ciclo(61);
        Assert.Single(ambiente.Corrigidas);

        // A tecla volta a travar antes da rodada seguinte.
        ambiente.Estado = new[] { Shift() };
        ambiente.Ciclo(31);      // o app vê a tecla de novo (nunca a viu subir)
        ambiente.Ciclo(31);      // 31 s depois: tem que soltar de novo
        Assert.Equal(2, ambiente.Corrigidas.Count);
    }

    [Fact]
    public void Vigia_OAvisoVoltaDepoisDaJanela()
    {
        // O teto de avisos é por janela: depois de um tempo sem tentativa
        // (o travamento acabou de verdade), a janela recomeça e o dono
        // volta a ser avisado.
        var ambiente = new Ambiente { Estado = new[] { Shift() } };

        ambiente.Ciclo(5);
        ambiente.Ciclo(31);
        Assert.True(ambiente.Corrigidas[0].Avisar);

        // Dois travamentos logo em seguida: ainda avisa (o teto é 3).
        for (int i = 0; i < 2; i++)
        {
            ambiente.Estado = new[] { Shift() };
            ambiente.Ciclo(31);
            ambiente.Ciclo(31);
        }
        Assert.Equal(3, ambiente.Corrigidas.Count);
        Assert.Equal(3, ambiente.Corrigidas.Count(t => t.Avisar));

        // O quarto, sem pausa: o aviso cala (o app continua soltando).
        ambiente.Estado = new[] { Shift() };
        ambiente.Ciclo(31);
        ambiente.Ciclo(31);
        Assert.Equal(4, ambiente.Corrigidas.Count);
        Assert.Equal(3, ambiente.Corrigidas.Count(t => t.Avisar));

        // Depois da janela (15 min depois do último aviso), o aviso volta.
        ambiente.Estado = new[] { Shift() };
        ambiente.Ciclo(15 * 60);
        ambiente.Ciclo(31);
        Assert.Equal(5, ambiente.Corrigidas.Count);
        Assert.Equal(4, ambiente.Corrigidas.Count(t => t.Avisar));
    }

    [Fact]
    public void Vigia_AJanelaDoAvisoContaDoUltimoAviso()
    {
        // O teto de 3 avisos não pode calar o dono para sempre. A janela
        // conta do Último AVISO, e não da última tentativa: se contasse da
        // tentativa, um travamento que se repete renovaria a janela a cada
        // rodada e o dono nunca mais ouviria falar do problema.
        //
        // O relégio da vigia é o do ambiente de teste (ms), então a conta
        // abaixo é exata:
        //   t=36 s    aviso 1 (correção 1)
        //   t=67 s    aviso 2 (correção 2)
        //   t=160 s   aviso 3 (correção 3)
        //   t=191 s   sem aviso (teto de 3) — a correção continua
        //   t=1074 s  aviso 4 (correção 5): 15 min e 14 s depois do aviso 3
        //            e ainda dentro dos 15 min contados da última tentativa
        //            (t=191 s) — é o caso que a contagem pela tentativa
        //            deixaria calado.
        var ambiente = new Ambiente { Estado = new[] { Shift() } };

        ambiente.Ciclo(5);
        ambiente.Ciclo(31);
        Assert.True(ambiente.Corrigidas[0].Avisar);

        // Segundo travamento: ainda avisa.
        ambiente.Estado = new[] { Shift() };
        ambiente.Ciclo(31);
        ambiente.Ciclo(31);
        Assert.Equal(2, ambiente.Corrigidas.Count);
        Assert.True(ambiente.Corrigidas[1].Avisar);

        // Terceiro travamento: ainda avisa (o teto é 3).
        ambiente.Estado = new[] { Shift() };
        ambiente.Ciclo(31);
        ambiente.Ciclo(31);
        Assert.Equal(3, ambiente.Corrigidas.Count);
        Assert.True(ambiente.Corrigidas[2].Avisar);

        // Quarto travamento: o aviso cala, a correção continua.
        ambiente.Estado = new[] { Shift() };
        ambiente.Ciclo(31);
        ambiente.Ciclo(31);
        Assert.Equal(4, ambiente.Corrigidas.Count);
        Assert.False(ambiente.Corrigidas[3].Avisar);
        Assert.Equal(3, ambiente.Corrigidas.Count(t => t.Avisar));

        // A tecla sobe de verdade: a vigia vê que o episódio acabou.
        ambiente.Ciclo(31);

        // 15 min depois do último aviso (e antes dos 15 min contados da
        // última tentativa): o aviso tem que voltar.
        ambiente.Estado = new[] { Shift() };
        ambiente.Ciclo(13 * 60 + 10);
        ambiente.Ciclo(31);
        Assert.Equal(5, ambiente.Corrigidas.Count);
        Assert.True(ambiente.Corrigidas[4].Avisar);
        Assert.Equal(4, ambiente.Corrigidas.Count(t => t.Avisar));
    }

    [Fact]
    public void Vigia_AEsperaMorreComOFimDoEpisodio()
    {
        // A espera da tentativa que falhou não pode sobreviver ao fim do
        // episódio: se a tecla sobe (o travamento acabou) e volta a cair, o app
        // trata no tempo normal. É o defeito medido no PC do dono em
        // 16/11/2026: a segunda travada ficava 5 minutos sem tratamento.
        var ambiente = new Ambiente { Estado = new[] { Shift() }, SoltarLimpaEstado = false };

        ambiente.Ciclo(5);
        ambiente.Ciclo(31);                       // t=36 s: a tentativa falha
        Assert.Single(ambiente.SemSucesso);

        ambiente.Estado = Array.Empty<Modificadora>();
        ambiente.Ciclo(1);                        // t=37 s: o episódio acabou (a tecla subiu)

        ambiente.Estado = new[] { Shift() };
        ambiente.SoltarLimpaEstado = true;
        ambiente.Ciclo(1);                        // t=38 s: a vigia vê a tecla cair de novo
        ambiente.Ciclo(30);                       // t=68 s: 30 s de tecla caída -> corrige
        Assert.Single(ambiente.Corrigidas);       // 32 s depois da tentativa: sem esperar o minuto
    }

    [Fact]
    public void Vigia_OsOrcamentosDeSucessoEFalhaSaoSeparados()
    {
        // Três correções que deram certo não podem calar o aviso de FALHA: a
        // falha é o aviso que o dono mais precisa ver (a tecla continua
        // presa). O orçamento de sucesso e o de falha são contadores
        // separados.
        var falhar = false;
        var ambiente = new Ambiente
        {
            Estado = new[] { Shift() },
            SoltarLimpaPorVk = vk => !falhar,
        };

        ambiente.Ciclo(5);
        ambiente.Ciclo(31);                      // t=36 s: a primeira correção (dá certo)
        Assert.Single(ambiente.Corrigidas);
        Assert.True(ambiente.Corrigidas[0].Avisar);

        // Duas correções que deram certo: o aviso de sucesso chega a 3.
        for (int i = 0; i < 2; i++)
        {
            ambiente.Estado = new[] { Shift() };
            ambiente.Ciclo(31);                  // a vigia vê a tecla cair de novo
            ambiente.Ciclo(31);                  // 31 s depois: corrige de novo
        }
        Assert.Equal(3, ambiente.Corrigidas.Count);
        Assert.Equal(3, ambiente.Corrigidas.Count(t => t.Avisar));

        // Agora a tentativa falha: o aviso de FALHA tem que sair mesmo depois
        // das três correções que deram certo.
        falhar = true;
        ambiente.Estado = new[] { Shift() };
        ambiente.Ciclo(31);
        ambiente.Ciclo(31);
        Assert.Single(ambiente.SemSucesso);
        Assert.True(ambiente.SemSucesso[0].Avisar);
    }

    [Fact]
    public void Vigia_ComDuasFamiliasJulgaCadaUmaPeloSeuEstado()
    {
        // O "soltar" pode limpar o Shift e não limpar o Ctrl (o Windows
        // re-marca a tecla). O aviso tem que falar só do Ctrl — e o Shift,
        // que foi solto, não pode ficar esperando o minuto de espera à toa.
        var ambiente = new Ambiente
        {
            Estado = new[] { new Modificadora(0x10, "Shift", "Shift"), new Modificadora(0x11, "Ctrl", "Ctrl") },
            SoltarLimpaPorVk = vk => vk != 0x11,
        };

        ambiente.Ciclo(5);
        ambiente.Ciclo(31);

        Assert.Empty(ambiente.Corrigidas);
        Assert.Single(ambiente.SemSucesso);
        Assert.Equal(new[] { "Ctrl" }, ambiente.SemSucesso[0].Familias);
        Assert.Equal(new[] { "Ctrl" }, ambiente.SemSucesso[0].Teclas);

        // O Shift foi solto de verdade: um novo travamento dele é tratado no
        // tempo normal (sem esperar o minuto de espera da família que
        // falhou). O passo de 1 s no começo é o que garante isto: o Shift
        // fica pronto antes de a espera do Ctrl vencer.
        ambiente.Estado = new[] { new Modificadora(0x10, "Shift", "Shift"), new Modificadora(0x11, "Ctrl", "Ctrl") };
        ambiente.Ciclo(1);       // o app vê o Shift de novo (o Ctrl continua esperando)
        ambiente.Ciclo(31);      // 31 s depois: o Shift é solto

        Assert.Single(ambiente.Corrigidas);
        Assert.Equal(new[] { "Shift" }, ambiente.Corrigidas[0].Familias);
        Assert.DoesNotContain(ambiente.Estado, m => m.Vk == 0x10);
        Assert.Single(ambiente.SemSucesso);      // o Ctrl continua preso e esperando
    }

    [Fact]
    public void Vigia_DepoisDeUmaTentativaQueFalhouUmTravamentoNovoEhTratado()
    {
        // A tentativa falhou (a tecla não solta) e a espera de 1 min começou.
        // Quando o episódio acaba (a tecla sobe) e ela trava de novo, o app
        // trata no tempo normal: senão a segunda travada ficava 5 minutos
        // sem tratamento nenhum (medido no PC do dono em 16/11/2026).
        var ambiente = new Ambiente { Estado = new[] { Shift() }, SoltarLimpaEstado = false };

        ambiente.Ciclo(5);
        ambiente.Ciclo(31);
        Assert.Single(ambiente.SemSucesso);

        // O episódio acabou: a tecla subiu.
        ambiente.Estado = Array.Empty<Modificadora>();
        ambiente.Ciclo(31);

        // A tecla trava de novo: agora o "soltar" funciona.
        ambiente.Estado = new[] { Shift() };
        ambiente.SoltarLimpaEstado = true;
        ambiente.Ciclo(31);      // o app vê a tecla pela primeira vez
        ambiente.Ciclo(31);      // 31 s depois: a correção

        Assert.Single(ambiente.Corrigidas);
        Assert.Equal(new[] { "Shift" }, ambiente.Corrigidas[0].Familias);
    }
}
