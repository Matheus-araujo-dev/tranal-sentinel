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
    private static readonly DateTime Inicio = new(2026, 11, 16, 12, 0, 0, DateTimeKind.Utc);

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

        /// <summary>Relógio de parede (só para carimbar o diário).</summary>
        public DateTime Relogio = Inicio;

        public IReadOnlyList<Modificadora> Estado = Array.Empty<Modificadora>();
        public TimeSpan? Silencio = TimeSpan.FromMinutes(10);
        public bool Repeticao = true;
        public bool LerFalha;
        public bool SoltarFalha;

        /// <summary>Simula o "soltar" que não limpa o estado (tecla continua presa).</summary>
        public bool SoltarLimpaEstado = true;

        public List<int> Soltadas = new();
        public List<TeclaPresa> Corrigidas = new();
        public List<TeclaPresa> SemSucesso = new();
        public MonitorTeclaPresa Monitor = null!;

        /// <summary>O gancho de teclado está vivo?</summary>
        public bool GanchoVivo = true;

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
                    if (SoltarLimpaEstado) Estado = Array.Empty<Modificadora>();
                },
                () => Relogio,
                () => GanchoVivo);
            Monitor.Corrigida += tecla => Corrigidas.Add(tecla);
            Monitor.NaoConseguiu += tecla => SemSucesso.Add(tecla);
        }

        /// <summary>Avança o relógio e roda um ciclo da vigia.</summary>
        public void Ciclo(int segundos)
        {
            Agora += segundos * 1000L;
            Relogio = Relogio.AddSeconds(segundos);
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
        // justamente aí que a espera de 5 min entra (com a tecla sempre caída,
        // a condição dos 30 s continua valendo). O app avisa que tentou
        // e não conseguiu — não que soltou.
        var ambiente = new Ambiente { Estado = new[] { Shift() }, SoltarLimpaEstado = false };

        ambiente.Ciclo(5);
        ambiente.Ciclo(31);
        Assert.Single(ambiente.SemSucesso);

        ambiente.Ciclo(31);
        Assert.Single(ambiente.SemSucesso);

        // Passados os 5 minutos, a vigia tenta de novo.
        ambiente.Ciclo(5 * 60);
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
    public void Vigia_FalhaAoSoltarNaoAvisa()
    {
        var ambiente = new Ambiente { Estado = new[] { Shift() }, SoltarFalha = true };

        ambiente.Ciclo(5);
        ambiente.Ciclo(31);

        Assert.Empty(ambiente.Corrigidas);
        Assert.Empty(ambiente.SemSucesso);
        Assert.Empty(ambiente.Soltadas);
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
    public void Vigia_AEsperaDe5MinutosMORREComAFamiliaCerta()
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
        var ambiente = new Ambiente { Estado = new[] { Shift() } };

        ambiente.Ciclo(5);
        ambiente.Relogio = ambiente.Relogio.AddHours(-3);   // o relógio de parede volta
        ambiente.Ciclo(31);

        Assert.Single(ambiente.Corrigidas);
        Assert.Equal(TimeSpan.FromSeconds(31), ambiente.Corrigidas[0].HaQuanto);
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
        // O mesmo evento não pode sair com dois números (o aviso arredonda e
        // o diário trunca). O número vem de um lugar só.
        Assert.Equal(31, TeclaPresaRegras.SegundosDoAviso(TimeSpan.FromSeconds(30.6)));
        Assert.Equal(30, TeclaPresaRegras.SegundosDoAviso(TimeSpan.FromSeconds(30.4)));

        string texto = TeclaPresaRegras.TextoDoAviso(new[] { "Shift direito" }, TimeSpan.FromSeconds(30.6));
        Assert.Contains("31 s", texto);
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
            if (!vigia.Instalar()) return;      // o Windows recusou: não há o que medir

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
        // Tecla que não solta: o aviso não pode virar spam (a cada 5 minutos,
        // para sempre). Depois de três avisos o app cala — e continua
        // tentando soltar.
        var ambiente = new Ambiente { Estado = new[] { Shift() }, SoltarLimpaEstado = false };

        ambiente.Ciclo(5);
        ambiente.Ciclo(31);
        Assert.Single(ambiente.SemSucesso);

        ambiente.Ciclo(5 * 60);
        Assert.Equal(2, ambiente.SemSucesso.Count);

        ambiente.Ciclo(5 * 60);
        Assert.Equal(3, ambiente.SemSucesso.Count);

        ambiente.Ciclo(5 * 60);
        Assert.Equal(3, ambiente.SemSucesso.Count);      // o aviso para

        // ... mas o app continua tentando soltar (a família inteira: 3 teclas).
        Assert.Equal(12, ambiente.Soltadas.Count);
        Assert.Equal(4, ambiente.Monitor.CorrecoesFeitas);
    }
}
