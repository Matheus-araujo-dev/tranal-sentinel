using Xunit;

namespace ResetExplode.Tests;

/// <summary>Testes dos modelos usados pela aba RESET SAÚDE.</summary>
public class SaudeModelosTestes
{
    [Theory]
    [InlineData(Severidade.Critico, "CRÍTICO", true)]
    [InlineData(Severidade.Alto, "ALTO", true)]
    [InlineData(Severidade.Medio, "MÉDIO", false)]
    [InlineData(Severidade.Baixo, "BAIXO", false)]
    [InlineData(Severidade.Informativo, "INFO", false)]
    public void Achado_TemSeloEAlertaCorretos(Severidade severidade, string selo, bool alerta)
    {
        var achado = new Achado { Severidade = severidade };

        Assert.Equal(selo, achado.Selo);
        Assert.Equal(alerta, achado.IsAlerta);
    }

    [Fact]
    public void RelatorioSemAchados_DizQueNaoAchouNada()
    {
        var relatorio = new RelatorioSaude();

        Assert.Equal("Nenhum problema encontrado.", relatorio.Resumo);
        Assert.Equal(0, relatorio.Total);
        Assert.Equal(0, relatorio.Criticos);
        Assert.Equal(0, relatorio.Altos);
    }

    [Fact]
    public void Relatorio_ContaCriticosEAltos()
    {
        var relatorio = new RelatorioSaude();
        relatorio.Achados.Add(new Achado { Severidade = Severidade.Critico });
        relatorio.Achados.Add(new Achado { Severidade = Severidade.Alto });
        relatorio.Achados.Add(new Achado { Severidade = Severidade.Alto });
        relatorio.Achados.Add(new Achado { Severidade = Severidade.Medio });

        Assert.Equal(4, relatorio.Total);
        Assert.Equal(1, relatorio.Criticos);
        Assert.Equal(2, relatorio.Altos);
        Assert.Contains("1 crítico(s)", relatorio.Resumo);
        Assert.Contains("2 alto(s)", relatorio.Resumo);
    }

    [Fact]
    public void TravamentoFormataADataParaALista()
    {
        var travamento = new TravamentoInfo { Data = new DateTime(2026, 10, 7, 23, 43, 38) };

        Assert.Equal("07/10/2026 23:43", travamento.DataFormatada);
    }

    [Fact]
    public void TravamentoSemDataMostraTraco()
    {
        var travamento = new TravamentoInfo();

        Assert.Equal("—", travamento.DataFormatada);
    }

    [Fact]
    public void DiscoNvmeEReconhecidoPeloModelo()
    {
        var disco = new DiscoInfo { Modelo = "Netac NVMe SSD 1TB", PnpDeviceId = @"SCSI\DISK&VEN_NVME&PROD_NETAC" };

        Assert.True(disco.EhNvme);
    }

    [Fact]
    public void DiscoNvmeEReconhecidoPeloPnpMesmoSemNome()
    {
        var disco = new DiscoInfo { Modelo = "KINGSTON SNV3S1000G", PnpDeviceId = @"SCSI\DISK&VEN_NVME&PROD_KINGSTON_SNV3S10" };

        Assert.True(disco.EhNvme);
    }

    [Fact]
    public void SsdComRotaNaoEhNvme()
    {
        var disco = new DiscoInfo { Modelo = "SanDisk SSD PLUS 240GB", PnpDeviceId = @"SCSI\DISK&VEN_SANDISK&PROD_SSD_PLUS_240GB" };

        Assert.False(disco.EhNvme);
    }

    [Fact]
    public void DiscoComStatusOkNaoTemProblema()
    {
        var disco = new DiscoInfo { Modelo = "Netac NVMe SSD 1TB", Status = "OK" };

        Assert.False(disco.EstaComProblema);
    }

    [Fact]
    public void DiscoForaDoArTemProblema()
    {
        var disco = new DiscoInfo { Modelo = "Netac NVMe SSD 1TB", Status = "Erro desconhecido" };

        Assert.True(disco.EstaComProblema);
    }

    [Fact]
    public void ServicoDoWindowsNaoEhBloatDeTerceiros()
    {
        var doSistema = new ServicoInfo { Caminho = @"C:\Windows\System32\svchost.exe" };
        var deTerceiros = new ServicoInfo { Caminho = @"C:\Program Files\Fornecedor\app.exe" };

        Assert.True(doSistema.EhDoSistema);
        Assert.False(deTerceiros.EhDoSistema);
    }

    [Fact]
    public void ModuloDeMemoriaConverteParaGigabytes()
    {
        var modulo = new ModuloMemoria { CapacidadeBytes = 16L * 1024 * 1024 * 1024 };

        Assert.Equal(16.0, modulo.Gigabytes);
    }
}
