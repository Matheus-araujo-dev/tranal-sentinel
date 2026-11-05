using Xunit;

namespace TranalSentinel.Tests;

/// <summary>
/// Testes das regras do diagnóstico de saúde.
///
/// As regras são funções puras: recebem a fotografia do sistema
/// (ColetaSaude) e devolvem achados. Nenhum Windows é tocado aqui.
/// </summary>
public class SaudeRegrasTestes
{
    private static ColetaSaude Coleta() => new();

    private static TravamentoInfo Travamento(string bugcheck = "0", string detalhe = "Sem botão de energia (travou sozinho)") =>
        new() { Data = new DateTime(2026, 10, 7, 23, 43, 38), Bugcheck = bugcheck, Detalhe = detalhe };

    private static Achado? AchadoComArea(List<Achado> achados, string area) =>
        achados.FirstOrDefault(a => a.Area == area);

    // ---------------------------------------------------------------- travamentos

    [Fact]
    public void SistemaLimpo_NaoGeraNenhumAchado()
    {
        var achados = SaudeRegras.Avaliar(Coleta());
        Assert.Empty(achados);
    }

    [Fact]
    public void TravamentosSemBugcheckESemMinidump_GeraCritico()
    {
        var dados = Coleta();
        dados.Travamentos.Add(Travamento());
        dados.Travamentos.Add(Travamento());

        var achados = SaudeRegras.Avaliar(dados);

        var achado = AchadoComArea(achados, "Travamentos");
        Assert.NotNull(achado);
        Assert.Equal(Severidade.Critico, achado.Severidade);
        Assert.Contains("2 travamentos", achado.Mensagem);
    }

    [Fact]
    public void TravamentosComBugcheck_GeraAltoEmVezDeCritico()
    {
        var dados = Coleta();
        dados.Travamentos.Add(Travamento(bugcheck: "0x00000139"));

        var achados = SaudeRegras.Avaliar(dados);

        var achado = AchadoComArea(achados, "Travamentos");
        Assert.NotNull(achado);
        Assert.Equal(Severidade.Alto, achado.Severidade);
        Assert.Contains("bugcheck", achado.Mensagem);
    }

    [Fact]
    public void MinidumpExistenteBaixaTravamentoDeCriticoParaAlto()
    {
        var dados = Coleta();
        dados.Travamentos.Add(Travamento());
        dados.Minidumps = 1;

        var achado = AchadoComArea(SaudeRegras.Avaliar(dados), "Travamentos");
        Assert.NotNull(achado);
        Assert.Equal(Severidade.Alto, achado.Severidade);
    }

    [Fact]
    public void AppQueFalhaEmTodosOsTravamentos_GeraInformativo()
    {
        var dados = Coleta();
        dados.Travamentos.Add(Travamento());
        dados.Travamentos.Add(Travamento());
        dados.AppsQueFalharam.Add(new AppFalhouInfo { Nome = "Kalkan.exe", Vezes = 2 });

        var achados = SaudeRegras.Avaliar(dados);
        var achado = achados.FirstOrDefault(a => a.Mensagem.Contains("Kalkan.exe"));

        Assert.NotNull(achado);
        Assert.Equal(Severidade.Informativo, achado.Severidade);
    }

    [Fact]
    public void AppDesinstalado_NAoMandaVerificarSeAindaCrasha()
    {
        var dados = Coleta();
        dados.Travamentos.Add(Travamento());
        dados.Travamentos.Add(Travamento());
        dados.AppsQueFalharam.Add(new AppFalhouInfo { Nome = "Kalkan.exe", Vezes = 2, AindaInstalado = false });

        var achado = SaudeRegras.Avaliar(dados).FirstOrDefault(a => a.Mensagem.Contains("Kalkan.exe"));

        Assert.NotNull(achado);
        Assert.Contains("Não encontrei mais", achado.Acao);
        Assert.DoesNotContain("reexecuta no boot", achado.Acao);
    }

    [Fact]
    public void AppAindaInstalado_ContinuaMandandoVerificarOBoot()
    {
        var dados = Coleta();
        dados.Travamentos.Add(Travamento());
        dados.Travamentos.Add(Travamento());
        dados.AppsQueFalharam.Add(new AppFalhouInfo { Nome = "Kalkan.exe", Vezes = 2, AindaInstalado = true });

        var achado = SaudeRegras.Avaliar(dados).FirstOrDefault(a => a.Mensagem.Contains("Kalkan.exe"));

        Assert.NotNull(achado);
        Assert.Contains("reexecuta no boot", achado.Acao);
    }

    [Fact]
    public void AppQueFalhaEmPoucosTravamentos_NaoGeraAchado()
    {
        var dados = Coleta();
        dados.Travamentos.Add(Travamento());
        dados.Travamentos.Add(Travamento());
        dados.Travamentos.Add(Travamento());
        dados.AppsQueFalharam.Add(new AppFalhouInfo { Nome = "Kalkan.exe", Vezes = 1 });

        var achados = SaudeRegras.Avaliar(dados);
        Assert.DoesNotContain(achados, a => a.Mensagem.Contains("Kalkan.exe"));
    }

    // ------------------------------------------------------------------ hardware

    [Fact]
    public void EventosWhea_GeraAchadoAlto()
    {
        var dados = Coleta();
        dados.EventosWhea = 4;

        var achado = AchadoComArea(SaudeRegras.Avaliar(dados), "Hardware");
        Assert.NotNull(achado);
        Assert.Equal(Severidade.Alto, achado.Severidade);
        Assert.Contains("4 eventos WHEA", achado.Mensagem);
    }

    [Fact]
    public void Minidumps_GeraAchadoAlto()
    {
        var dados = Coleta();
        dados.Minidumps = 3;

        var achado = AchadoComArea(SaudeRegras.Avaliar(dados), "Hardware");
        Assert.NotNull(achado);
        Assert.Equal(Severidade.Alto, achado.Severidade);
    }

    [Fact]
    public void ResetsDeVideo_GeraAchadoMedio()
    {
        var dados = Coleta();
        dados.ResetsDriverVideo = 2;

        var achado = AchadoComArea(SaudeRegras.Avaliar(dados), "Hardware");
        Assert.NotNull(achado);
        Assert.Equal(Severidade.Medio, achado.Severidade);
    }

    // ---------------------------------------------------------------------- PCIe

    [Fact]
    public void AspmDesligado_NaoGeraAchadoDeAspm()
    {
        var dados = Coleta();
        dados.AspmAc = 0;
        dados.AspmDc = 0;

        var achados = SaudeRegras.Avaliar(dados);
        Assert.DoesNotContain(achados, a => a.Area == "PCIe/ASPM");
    }

    [Fact]
    public void AspmEmEconomiaMaxima_GeraAchadoAlto()
    {
        var dados = Coleta();
        dados.AspmAc = 2;

        var achado = AchadoComArea(SaudeRegras.Avaliar(dados), "PCIe/ASPM");
        Assert.NotNull(achado);
        Assert.Equal(Severidade.Alto, achado.Severidade);
        Assert.Contains("Economia máxima", achado.Mensagem);
    }

    [Fact]
    public void NvmeEmAdaptador_GeraAchadoAltoIndependenteDoAspm()
    {
        var dados = Coleta();
        dados.AspmAc = 0;
        dados.Nvmes.Add(new InfoNvme
        {
            Nome = "Netac NVMe SSD 1TB",
            Controller = "PCI\\VEN_126F&DEV_2262",
            PaiDoController = "PCI\\VEN_1B21&DEV_2262",
            Localizacao = "PCIROOT(0)#PCI(0101)#PCI(0000)#PCI(0000)"
        });

        var achados = SaudeRegras.Avaliar(dados);
        Assert.Contains(achados, a => a.Area == "PCIe/ASPM" && a.Mensagem.Contains("adaptadora"));
    }

    [Fact]
    public void NvmeNoSoqueteDaPlacaNaoGeraAchadoDeAdaptador()
    {
        // Topologia real desta maquina: NVMe no slot M.2 da propria placa
        // (ponte do chipset AMD, 3 degraus no caminho PCI).
        var dados = Coleta();
        dados.AspmAc = 0;
        dados.Nvmes.Add(new InfoNvme
        {
            Nome = "Netac NVMe SSD 1TB",
            Controller = "PCI\\VEN_126F&DEV_2262&SUBSYS_2262126F&REV_03\\4&2B5FE750&0&0009",
            PaiDoController = "PCI\\VEN_1022&DEV_1483&SUBSYS_12341022&REV_00\\3&11583659&0&09",
            Localizacao = "PCIROOT(0)#PCI(0101)#PCI(0000)"
        });

        var achados = SaudeRegras.Avaliar(dados);
        Assert.DoesNotContain(achados, a => a.Area == "PCIe/ASPM" && a.Mensagem.Contains("adaptadora"));
    }

    [Theory]
    // 3 degraus (PCIROOT/PCI/PCI) = direto num soquete da placa-mãe.
    [InlineData("PCIROOT(0)#PCI(0101)#PCI(0000)", "PCI\\VEN_1022&DEV_1483", false)]
    // 4 degraus = atrás de um dispositivo PCIe (placa adaptadora).
    [InlineData("PCIROOT(0)#PCI(0101)#PCI(0000)#PCI(0000)", "PCI\\VEN_1B21&DEV_1170", true)]
    // Sem caminho de localizacao: decide pela ponte (VEN_1022 = placa da AMD).
    [InlineData("", "PCI\\VEN_1022&DEV_1483", false)]
    [InlineData("", "PCI\\VEN_1B21&DEV_1170", true)]
    // Sem nenhum dos dois: não dá para afirmar nada.
    [InlineData("", "", false)]
    public void DeteccaoDeAdaptadorUsaTopologiaReal(string localizacao, string paiDoController, bool esperado)
    {
        Assert.Equal(esperado, SaudeRegras.NvmeEhAdaptador(paiDoController, localizacao));
    }

    [Fact]
    public void CaminhoDeLocalizacaoComAcpINaoContaComoDegrau()
    {
        // O caminho ACPI paralelo nao descreve a topologia PCIe.
        string caminho = "PCIROOT(0)#PCI(0101)#PCI(0000); ACPI(_SB_)#ACPI(PCI0)#ACPI(GPP0)#ACPI(D047)";
        Assert.False(SaudeRegras.NvmeEhAdaptador("PCI\\VEN_1022&DEV_1483", caminho));
    }

    [Theory]
    [InlineData(0, "Desligado")]
    [InlineData(1, "Economia moderada")]
    [InlineData(2, "Economia máxima")]
    public void NomeAspm_TemOsValoresEsperados(int indice, string esperado)
    {
        Assert.Equal(esperado, SaudeRegras.NomeAspm(indice));
    }

    // -------------------------------------------------------------------- discos

    [Fact]
    public void NvmeForaDoAr_GeraCritico()
    {
        var dados = Coleta();
        dados.Discos.Add(new DiscoInfo { Modelo = "Netac NVMe SSD 1TB", Status = "Erro desconhecido" });

        var achado = AchadoComArea(SaudeRegras.Avaliar(dados), "Disco");
        Assert.NotNull(achado);
        Assert.Equal(Severidade.Critico, achado.Severidade);
    }

    [Fact]
    public void NvmePronto_NaoGeraAchadoCritico()
    {
        var dados = Coleta();
        dados.Discos.Add(new DiscoInfo { Modelo = "Netac NVMe SSD 1TB", Status = "OK" });

        var achados = SaudeRegras.Avaliar(dados);
        Assert.DoesNotContain(achados, a => a.Area == "Disco" && a.Severidade == Severidade.Critico);
    }

    [Fact]
    public void SsdComProblemaNaoEOrfaoMasDiscoNaoProntoTambemNaoGeraCritico()
    {
        // Só o NVMe em estado ruim é tratado como crítico; outros discos ficam de fora.
        var dados = Coleta();
        dados.Discos.Add(new DiscoInfo { Modelo = "SanDisk SSD PLUS 240GB", Status = "Erro desconhecido" });

        var achados = SaudeRegras.Avaliar(dados);
        Assert.DoesNotContain(achados, a => a.Area == "Disco" && a.Severidade == Severidade.Critico);
    }

    [Fact]
    public void OrfaoDeArmazenamento_GeraAchadoMedio()
    {
        var dados = Coleta();
        dados.Orfaos.Add(new DispositivoOrfao { Nome = "KINGSTON SNV3S1000G", CodigoErro = 8 });

        var achado = AchadoComArea(SaudeRegras.Avaliar(dados), "Disco");
        Assert.NotNull(achado);
        Assert.Equal(Severidade.Medio, achado.Severidade);
        Assert.Contains("KINGSTON", achado.Mensagem);
    }

    // ---------------------------------------------------------------- segurança

    [Fact]
    public void DefenderParadoSemFiltroDeKernel_GeraCritico()
    {
        var dados = Coleta();
        dados.ModoDefender = "Not running";
        dados.EstadoDriverAntivirus = null;

        var achado = AchadoComArea(SaudeRegras.Avaliar(dados), "Segurança");
        Assert.NotNull(achado);
        Assert.Equal(Severidade.Critico, achado.Severidade);
    }

    [Fact]
    public void DefenderParadoComAntivirusDeTerceiros_GeraInformativo()
    {
        var dados = Coleta();
        dados.ModoDefender = "Not running";
        dados.EstadoDriverAntivirus = "Running";
        dados.AntivirusRegistrado = "McAfee";

        var achado = AchadoComArea(SaudeRegras.Avaliar(dados), "Segurança");
        Assert.NotNull(achado);
        Assert.Equal(Severidade.Informativo, achado.Severidade);
        Assert.Contains("McAfee", achado.Mensagem);
    }

    [Fact]
    public void DefenderAtivo_NaoGeraAchadoDeSeguranca()
    {
        var dados = Coleta();
        dados.ModoDefender = "Active";

        var achados = SaudeRegras.Avaliar(dados);
        Assert.DoesNotContain(achados, a => a.Area == "Segurança");
    }

    [Fact]
    public void DefinicoesDeAntivirusAntigas_GeraAlto()
    {
        var dados = Coleta();
        dados.DefinicoesAntivirusDias = 25;
        dados.DefinicoesAntivirusData = "10/10/2026";

        var achado = AchadoComArea(SaudeRegras.Avaliar(dados), "Segurança");
        Assert.NotNull(achado);
        Assert.Equal(Severidade.Alto, achado.Severidade);
        Assert.Contains("25 dias", achado.Mensagem);
    }

    [Fact]
    public void DefinicoesDeAntivirusRecentes_NaoGeraAchado()
    {
        var dados = Coleta();
        dados.DefinicoesAntivirusDias = 2;

        var achados = SaudeRegras.Avaliar(dados);
        Assert.DoesNotContain(achados, a => a.Area == "Segurança");
    }

    [Fact]
    public void AntivirusComendoMemoria_GeraMedio()
    {
        var dados = Coleta();
        dados.AntivirusMemoriaMb = 1280;

        var achado = AchadoComArea(SaudeRegras.Avaliar(dados), "Lentidão");
        Assert.NotNull(achado);
        Assert.Equal(Severidade.Medio, achado.Severidade);
    }

    // ------------------------------------------------------------------- memória

    [Fact]
    public void CompressaoDeMemoriaAlta_GeraAlto()
    {
        var dados = Coleta();
        dados.MemoriaCompressaoMb = 1952;

        var achado = AchadoComArea(SaudeRegras.Avaliar(dados), "Lentidão");
        Assert.NotNull(achado);
        Assert.Equal(Severidade.Alto, achado.Severidade);
    }

    [Fact]
    public void CompressaoDeMemoriaBaixa_NaoGeraAchado()
    {
        var dados = Coleta();
        dados.MemoriaCompressaoMb = 800;

        var achados = SaudeRegras.Avaliar(dados);
        Assert.DoesNotContain(achados, a => a.Mensagem.Contains("Compressão de memória"));
    }

    [Fact]
    public void RamAcimaDoSpecDoRyzen5000_GeraMedio()
    {
        var dados = Coleta();
        dados.ModulosMemoria.Add(new ModuloMemoria
        {
            Slot = "DIMM_A2",
            VelocidadeConfigMHz = 3466,
            CapacidadeBytes = 16L * 1024 * 1024 * 1024
        });

        var achado = AchadoComArea(SaudeRegras.Avaliar(dados), "Estabilidade");
        Assert.NotNull(achado);
        Assert.Equal(Severidade.Medio, achado.Severidade);
        Assert.Contains("3466 MHz", achado.Mensagem);
    }

    [Fact]
    public void RamNoSpecOficial_NaoGeraAchado()
    {
        var dados = Coleta();
        dados.ModulosMemoria.Add(new ModuloMemoria { Slot = "DIMM_A2", VelocidadeConfigMHz = 3200 });

        var achados = SaudeRegras.Avaliar(dados);
        Assert.DoesNotContain(achados, a => a.Area == "Estabilidade");
    }

    // ------------------------------------------------------------------ lentidão

    [Fact]
    public void DozeProgramasNoAutostart_GeraMedio()
    {
        var dados = Coleta();
        dados.AutostartAtivos = 12;

        var achado = AchadoComArea(SaudeRegras.Avaliar(dados), "Lentidão");
        Assert.NotNull(achado);
        Assert.Equal(Severidade.Medio, achado.Severidade);
        Assert.Contains("12 programas", achado.Mensagem);
    }

    [Fact]
    public void OnzeProgramasNoAutostart_NaoGeraAchado()
    {
        var dados = Coleta();
        dados.AutostartAtivos = 11;

        var achados = SaudeRegras.Avaliar(dados);
        Assert.DoesNotContain(achados, a => a.Mensagem.Contains("inicialização automática"));
    }

    [Fact]
    public void ServicosDeTerceirosAcimaDeDez_GeraBaixo()
    {
        var dados = Coleta();
        for (int i = 0; i < 10; i++)
            dados.ServicosAuto.Add(new ServicoInfo { Nome = $"App{i}", Caminho = @"C:\Program Files\App\app.exe" });

        var achado = AchadoComArea(SaudeRegras.Avaliar(dados), "Lentidão");
        Assert.NotNull(achado);
        Assert.Equal(Severidade.Baixo, achado.Severidade);
        Assert.Contains("10 serviços", achado.Mensagem);
    }

    [Fact]
    public void ServicosDoSistemaNaoContamComoBloat()
    {
        var dados = Coleta();
        for (int i = 0; i < 30; i++)
            dados.ServicosAuto.Add(new ServicoInfo { Nome = $"Servico{i}", Caminho = @"C:\Windows\System32\servico.exe" });

        var achados = SaudeRegras.Avaliar(dados);
        Assert.DoesNotContain(achados, a => a.Mensagem.Contains("serviços automáticos"));
    }

    [Fact]
    public void HypervisorPresente_GeraInformativo()
    {
        var dados = Coleta();
        dados.HypervisorAtivo = true;

        var achado = AchadoComArea(SaudeRegras.Avaliar(dados), "Lentidão");
        Assert.NotNull(achado);
        Assert.Equal(Severidade.Informativo, achado.Severidade);
    }

    // -------------------------------------------------------------------- geral

    [Fact]
    public void Achados_VemOrdenadosDoMaisGraveParaOMenosGrave()
    {
        var dados = Coleta();
        dados.Travamentos.Add(Travamento());
        dados.AspmAc = 2;
        dados.MemoriaCompressaoMb = 3000;
        dados.HypervisorAtivo = true;

        var achados = SaudeRegras.Avaliar(dados);

        Assert.True(achados.Count >= 4);
        for (int i = 1; i < achados.Count; i++)
            Assert.True(SaudeRegras.Ordem(achados[i - 1].Severidade) <= SaudeRegras.Ordem(achados[i].Severidade),
                $"Achado {i} ({achados[i].Severidade}) veio antes de {achados[i - 1].Severidade}.");
    }

    [Fact]
    public void AvaliarNaoMutaAColeta()
    {
        var dados = Coleta();
        dados.AspmAc = 2;
        var antes = dados.AspmAc;

        SaudeRegras.Avaliar(dados);

        Assert.Equal(antes, dados.AspmAc);
        Assert.Empty(dados.Travamentos);
    }
}
