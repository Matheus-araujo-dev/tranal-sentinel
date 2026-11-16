using System.Globalization;
using Xunit;

namespace TranalSentinel.Tests;

/// <summary>
/// Testes da regra e da vigia da tecla presa.
///
/// <para>
/// A regra é pura e a vigia recebe tudo por função (relógio, leitura do
/// teclado, silêncio, repetição e o "soltar"), então dá para medir o
/// comportamento inteiro sem Windows em cima.
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
    public void TextoDoAviso_NaoDependeDaCulturaDoWindows()
    {
        var cultura = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("en-US");
            string texto = TeclaPresaRegras.TextoDoAviso("Shift", TimeSpan.FromSeconds(90));

            // O número sai pelo lugar único (NumeroTexto), com vírgula
            // decimal e ponto de milhar, aconteça o que acontecer com a cultura.
            Assert.Contains("90", texto);
            Assert.Contains("Shift", texto);
        }
        finally
        {
            CultureInfo.CurrentCulture = cultura;
        }
    }

    // ---------------------------------------------------------------- vigia

    private sealed class Ambiente
    {
        public DateTime Agora = Inicio;
        public IReadOnlyList<Modificadora> Estado = Array.Empty<Modificadora>();
        public TimeSpan? Silencio = TimeSpan.FromMinutes(10);
        public bool Repeticao = true;
        public bool LerFalha;
        public bool SoltarFalha;
        public List<int> Soltadas = new();
        public List<TeclaPresa> Corrigidas = new();
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
                    Estado = Array.Empty<Modificadora>();
                });
            Monitor.Corrigida += tecla => Corrigidas.Add(tecla);
        }

        /// <summary>Avança o relógio e roda um ciclo da vigia.</summary>
        public void Ciclo(int segundos)
        {
            Agora = Agora.AddSeconds(segundos);
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

        Assert.Equal(new[] { 0xA1 }, ambiente.Soltadas);
        Assert.Single(ambiente.Corrigidas);
        Assert.Equal("Shift", ambiente.Corrigidas[0].Familia);
        // A tecla foi vista pela primeira vez no primeiro ciclo (5 s):
        // de lá até a correção, 31 s.
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
        var ambiente = new Ambiente { Estado = new[] { Shift() } };

        ambiente.Ciclo(5);
        ambiente.Ciclo(31);
        Assert.Single(ambiente.Corrigidas);

        // A tecla continua presa (o "soltar" não resolveu): a vigia espera
        // antes de avisar de novo, para o aviso não virar spam.
        ambiente.Estado = new[] { Shift() };
        ambiente.Ciclo(31);
        Assert.Single(ambiente.Corrigidas);

        // Passados os 5 minutos, a vigia tenta de novo.
        ambiente.Estado = new[] { Shift() };
        ambiente.Ciclo(5 * 60 + 31);
        Assert.Equal(2, ambiente.Corrigidas.Count);
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
    public void Vigia_SoltaTodasAsTeclasDaFamiliaQueEstavamCaidas()
    {
        // O Windows marca a modificadora genérica junto com a lateral: as
        // duas precisam do "soltar" para o estado interno largar a tecla.
        var ambiente = new Ambiente
        {
            Estado = new[]
            {
                new Modificadora(0x10, "Shift", "Shift"),
                new Modificadora(0xA1, "Shift direito", "Shift"),
            }
        };

        ambiente.Ciclo(5);
        ambiente.Ciclo(31);

        Assert.Equal(new[] { 0x10, 0xA1 }, ambiente.Soltadas);
        Assert.Single(ambiente.Corrigidas);
        Assert.Equal("Shift", ambiente.Corrigidas[0].Familia);
    }
}
