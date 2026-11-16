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
        Assert.Equal("A tecla Shift ficou presa e foi solta",
            TeclaPresaRegras.TextoDaCorrecao(new[] { "Shift" }));

        Assert.Equal("As teclas Shift e Ctrl ficaram presas e foram soltas",
            TeclaPresaRegras.TextoDaCorrecao(new[] { "Shift", "Ctrl" }));
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
                () => Relogio);
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
}
