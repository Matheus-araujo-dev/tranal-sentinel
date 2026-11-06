using TranalSentinel;
using Xunit;

namespace TranalSentinel.Tests;

/// <summary>
/// A regra de saúde do disco que usa o S.M.A.R.T..
///
/// <para>
/// A regra é pura: o teste monta a <see cref="ColetaSaude"/> na mão (sem
/// Windows, sem WMI) e confere o achado que sai. Disco sem dado de SMART
/// não pode virar achado — a nota de quem não tem SMART não muda.
/// </para>
/// </summary>
public class SaudeRegrasDiscoTestes
{
    [Fact]
    public void A_falha_prevista_do_disco_e_critica()
    {
        var coleta = new ColetaSaude();
        coleta.DiscosSmart.Add(new SmartDisco
        {
            Numero = 0,
            Modelo = "SanDisk SSD PLUS 240GB",
            FalhaPrevista = true,
            Atributos = new List<SmartAtributo> { Atributo(5, 0) },
        });

        var achados = SaudeRegras.AvaliarDiscosSmart(coleta);

        var achado = Assert.Single(achados);
        Assert.Equal(Severidade.Critico, achado.Severidade);
        Assert.Equal("Disco", achado.Area);
        Assert.Contains("SanDisk SSD PLUS 240GB", achado.Mensagem, StringComparison.Ordinal);
    }

    [Fact]
    public void Setores_realocados_e_um_achado_alto()
    {
        var coleta = new ColetaSaude();
        coleta.DiscosSmart.Add(new SmartDisco
        {
            Numero = 1,
            Modelo = "SanDisk SSD PLUS 1000GB",
            Atributos = new List<SmartAtributo> { Atributo(5, 3) },
        });

        var achados = SaudeRegras.AvaliarDiscosSmart(coleta);

        var achado = Assert.Single(achados);
        Assert.Equal(Severidade.Alto, achado.Severidade);
        Assert.Equal("Disco", achado.Area);
        Assert.Contains("3", achado.Mensagem, StringComparison.Ordinal);
    }

    [Fact]
    public void Setores_pendentes_e_um_achado_medio()
    {
        var coleta = new ColetaSaude();
        coleta.DiscosSmart.Add(new SmartDisco
        {
            Numero = 0,
            Modelo = "SanDisk SSD PLUS 240GB",
            Atributos = new List<SmartAtributo> { Atributo(197, 2) },
        });

        var achados = SaudeRegras.AvaliarDiscosSmart(coleta);

        var achado = Assert.Single(achados);
        Assert.Equal(Severidade.Medio, achado.Severidade);
        Assert.Equal("Disco", achado.Area);
    }

    [Fact]
    public void A_vida_util_que_resta_no_ata_e_um_achado_medio()
    {
        // Atributo 232 (Remaining_Lifetime_Perc) com 5% de vida útil: a regra
        // tira a vida útil do próprio atributo quando o disco não traz o número.
        var coleta = new ColetaSaude();
        coleta.DiscosSmart.Add(new SmartDisco
        {
            Numero = 0,
            Modelo = "SanDisk SSD PLUS 240GB",
            Atributos = new List<SmartAtributo> { Atributo(232, 5) },
        });

        var achados = SaudeRegras.AvaliarDiscosSmart(coleta);

        var achado = Assert.Single(achados);
        Assert.Equal(Severidade.Medio, achado.Severidade);
        Assert.Equal("Disco", achado.Area);
        Assert.Contains("5%", achado.Mensagem, StringComparison.Ordinal);
    }

    [Fact]
    public void A_vida_util_que_resta_no_nvme_e_um_achado_medio()
    {
        var coleta = new ColetaSaude();
        coleta.DiscosSmart.Add(new SmartDisco
        {
            Numero = 2,
            Modelo = "Netac NVMe SSD 1TB",
            Tipo = "NVMe",
            VidaRestantePercent = 8,
            Contadores = new List<SmartContador> { new() { Nome = "Vida útil restante", Valor = "8 %" } },
        });

        var achados = SaudeRegras.AvaliarDiscosSmart(coleta);

        var achado = Assert.Single(achados);
        Assert.Equal(Severidade.Medio, achado.Severidade);
        Assert.Equal("Disco", achado.Area);
        Assert.Contains("8%", achado.Mensagem, StringComparison.Ordinal);
    }

    [Fact]
    public void O_disco_saudavel_nao_vira_achado()
    {
        var coleta = new ColetaSaude();
        coleta.DiscosSmart.Add(new SmartDisco
        {
            Numero = 0,
            Modelo = "SanDisk SSD PLUS 240GB",
            Atributos = new List<SmartAtributo> { Atributo(5, 0), Atributo(197, 0), Atributo(232, 100) },
            VidaRestantePercent = 100,
        });

        Assert.Empty(SaudeRegras.AvaliarDiscosSmart(coleta));
    }

    [Fact]
    public void Disco_sem_smart_nao_muda_a_nota_de_saude()
    {
        var coleta = new ColetaSaude();
        coleta.Discos.Add(new DiscoInfo { Modelo = "Disco sem SMART", Status = "OK" });

        // Sem dado de SMART nenhum: a regra não devolve achado nenhum.
        Assert.Empty(SaudeRegras.AvaliarDiscosSmart(coleta));

        // E o diagnóstico completo também não inventa problema de disco.
        var achados = SaudeRegras.Avaliar(coleta);
        Assert.DoesNotContain(achados, a => a.Area == "Disco");
    }

    [Fact]
    public void O_disco_sem_smart_aparece_na_tela_com_a_observacao()
    {
        var disco = new SmartDisco
        {
            Numero = 2,
            Modelo = "Netac NVMe SSD 1TB",
            Tipo = "NVMe",
            Observacao = "precisa de administrador",
        };

        Assert.False(disco.TemDados);
        Assert.Equal("SEM DADOS", disco.Selo);
        Assert.False(disco.Alerta);
        Assert.Empty(SaudeRegras.AvaliarDiscosSmart(new ColetaSaude()));
    }

    /// <summary>Um atributo de SMART com o valor cru informado.</summary>
    private static SmartAtributo Atributo(int id, long valorCru, int valor = 100)
    {
        var cru = new byte[6];
        for (int i = 0; i < 6; i++) cru[i] = (byte)((valorCru >> (8 * i)) & 0xFF);
        return new SmartAtributo
        {
            Id = id,
            Nome = SmartNomes.De(id),
            Flags = 0x0032,
            Valor = valor,
            Pior = valor,
            Cru = cru,
        };
    }
}
