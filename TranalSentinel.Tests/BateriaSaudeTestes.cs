using Xunit;

namespace TranalSentinel.Tests;

/// <summary>
/// Testes da saúde da bateria: a leitura do XML do powercfg e os
/// textos da tela.
/// </summary>
public class BateriaSaudeTestes
{
    private const string XmlComBateria = @"<?xml version=""1.0""?>
<BatteryReport>
  <Battery>
    <Id>DELL ABC123</Id>
    <DesignCapacity>50000</DesignCapacity>
    <FullChargeCapacity>42000</FullChargeCapacity>
    <CycleCount>312</CycleCount>
  </Battery>
</BatteryReport>";

    private const string XmlSemBateria = @"<?xml version=""1.0""?>
<BatteryReport>
</BatteryReport>";

    [Fact]
    public void LerXml_ComBateria_ExtraiDados()
    {
        var r = BateriaSaude.LerXml(XmlComBateria);

        Assert.NotNull(r);
        Assert.Equal("DELL ABC123", r.NomeBateria);
        Assert.Equal(50000, r.CapacidadeOriginalMwh);
        Assert.Equal(42000, r.CapacidadeAtualMwh);
        Assert.Equal(312, r.CiclosEstimados);
        Assert.Equal(84f, r.SaudePercentual, 0.1f);
    }

    [Fact]
    public void LerXml_SemBateria_DevolveNulo()
    {
        var r = BateriaSaude.LerXml(XmlSemBateria);
        Assert.Null(r);
    }

    [Fact]
    public void LerXml_XmlInvalido_DevolveNulo()
    {
        var r = BateriaSaude.LerXml("lixo");
        Assert.Null(r);
    }

    [Fact]
    public void LerXml_Vazio_DevolveNulo()
    {
        Assert.Null(BateriaSaude.LerXml(""));
        Assert.Null(BateriaSaude.LerXml(null!));
    }

    [Fact]
    public void TextoSaude_BateriaBoa()
    {
        var b = new BateriaRetrato("Test", 50000, 45000, 100, 90f, DateTime.Now);
        string t = BateriaSaude.TextoSaude(b);
        Assert.Contains("BOA", t);
        Assert.Contains("90", t);
    }

    [Fact]
    public void TextoSaude_BateriaDegradada()
    {
        var b = new BateriaRetrato("Test", 50000, 35000, 500, 70f, DateTime.Now);
        string t = BateriaSaude.TextoSaude(b);
        Assert.Contains("DEGRADADA", t);
    }

    [Fact]
    public void TextoSaude_BateriaCritica()
    {
        var b = new BateriaRetrato("Test", 50000, 20000, 1200, 40f, DateTime.Now);
        string t = BateriaSaude.TextoSaude(b);
        Assert.Contains("CRÍTICA", t);
    }

    [Fact]
    public void TextoEstado_Categorias()
    {
        Assert.Equal("Bateria saudável", BateriaSaude.TextoEstado(90f));
        Assert.Equal("Bateria degradada", BateriaSaude.TextoEstado(70f));
        Assert.Equal("Bateria crítica", BateriaSaude.TextoEstado(40f));
    }
}
