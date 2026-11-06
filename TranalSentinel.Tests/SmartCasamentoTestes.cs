using TranalSentinel;
using Xunit;

namespace TranalSentinel.Tests;

/// <summary>
/// O casamento entre o S.M.A.R.T. (que o WMI entrega por "InstanceName") e
/// os discos do Windows (que o Win32_DiskDrive entrega por id do PnP).
///
/// <para>
/// Os nomes abaixo são os reais do PC do dono: o InstanceName vem das classes
/// MSStorageDriver_FailurePredict* do root\wmi e o PNPDeviceID vem do
/// Win32_DiskDrive. Eles são diferentes por construção (o SMART escreve
/// "Ven_SanDisk&Prod_SSD_PLUS_240GB" e o PnP escreve
/// "VEN_SANDISK&PROD_SSD_PLUS_240GB"), então o casamento não pode ser por
/// string igual: tem que ser pelo id do dispositivo ou pelo modelo.
///
/// </para>
/// <para>
/// Disco sem correspondência fica "sem SMART" — o app nunca chuta qual
/// tabela é de qual disco.
/// </para>
/// </summary>
public class SmartCasamentoTestes
{
    private const string Instancia240Gb = @"SCSI\Disk&Ven_SanDisk&Prod_SSD_PLUS_240GB\5&2b637674&0&000000_0";
    private const string Instancia1Tb = @"SCSI\Disk&Ven_SanDisk&Prod_SSD_PLUS_1000GB\5&2b637674&0&010000_0";

    private const string Pnp240Gb = @"SCSI\DISK&VEN_SANDISK&PROD_SSD_PLUS_240GB\5&2B637674&0&000000";
    private const string Pnp1Tb = @"SCSI\DISK&VEN_SANDISK&PROD_SSD_PLUS_1000GB\5&2B637674&0&010000";
    private const string PnpNvme = @"SCSI\DISK&VEN_NVME&PROD_NETAC_NVME_SSD_1\5&10C35458&0&000000";

    private static Dictionary<string, SmartLeitor.InstanciaSmart> Instancias()
    {
        var mapa = new Dictionary<string, SmartLeitor.InstanciaSmart>(StringComparer.OrdinalIgnoreCase)
        {
            [Instancia240Gb] = new SmartLeitor.InstanciaSmart { InstanceName = Instancia240Gb, Dados = SmartReal.Dados240Gb },
            [Instancia1Tb] = new SmartLeitor.InstanciaSmart { InstanceName = Instancia1Tb, Dados = SmartReal.Dados1Tb },
        };
        return mapa;
    }

    [Fact]
    public void O_disco_de_240_gb_casa_com_a_instancia_do_smart_dele()
    {
        var disco = new DiscoFisico { Index = 0, Modelo = "SanDisk SSD PLUS 240GB", PnpDeviceId = Pnp240Gb };

        var instancia = SmartLeitor.InstanciaDoDisco(disco, Instancias());

        Assert.NotNull(instancia);
        Assert.Equal(Instancia240Gb, instancia!.InstanceName);
    }

    [Fact]
    public void O_disco_de_1_tb_casa_com_a_instancia_do_smart_dele()
    {
        var disco = new DiscoFisico { Index = 1, Modelo = "SanDisk SSD PLUS 1000GB", PnpDeviceId = Pnp1Tb };

        var instancia = SmartLeitor.InstanciaDoDisco(disco, Instancias());

        Assert.NotNull(instancia);
        Assert.Equal(Instancia1Tb, instancia!.InstanceName);
    }

    [Fact]
    public void O_disco_nvme_nao_casa_com_nenhuma_instituicao_de_smart()
    {
        // O WMI do Windows não cobre NVMe: este disco fica "sem SMART".
        var disco = new DiscoFisico { Index = 2, Modelo = "Netac NVMe SSD 1TB", PnpDeviceId = PnpNvme, Barramento = "NVMe" };

        var instancia = SmartLeitor.InstanciaDoDisco(disco, Instancias());

        Assert.Null(instancia);
    }

    [Fact]
    public void Sem_id_de_dispositivo_o_casamento_cai_para_o_modelo()
    {
        // Disco com o id do PnP diferente do que o SMART devolveu: quem casa
        // é o modelo ("SSD_PLUS_240GB" dentro de "SanDisk SSD PLUS 240GB").
        var disco = new DiscoFisico { Index = 7, Modelo = "SanDisk SSD PLUS 240GB", PnpDeviceId = @"SCSI\DISK&VEN_X&PROD_Y\9&9&9" };

        var instancia = SmartLeitor.InstanciaDoDisco(disco, Instancias());

        Assert.NotNull(instancia);
        Assert.Equal(Instancia240Gb, instancia!.InstanceName);
    }

    [Fact]
    public void Modelo_que_casa_com_dois_discos_fica_sem_smart_em_vez_de_chutar()
    {
        // Dois discos do mesmo modelo: sem correspondência única, o app não
        // escolhe um dos dois.
        var mapa = new Dictionary<string, SmartLeitor.InstanciaSmart>(StringComparer.OrdinalIgnoreCase)
        {
            [Instancia240Gb] = new SmartLeitor.InstanciaSmart { InstanceName = Instancia240Gb },
            [Instancia1Tb] = new SmartLeitor.InstanciaSmart { InstanceName = Instancia1Tb },
        };

        // Um modelo que bate com os dois discos do fabricante: sem
        // correspondência única, o app não escolhe um dos dois.
        var disco = new DiscoFisico { Index = 7, Modelo = "SanDisk SSD PLUS 1000GB / SanDisk SSD PLUS 240GB", PnpDeviceId = @"SCSI\DISK&VEN_X&PROD_Y\9&9&9" };

        Assert.Null(SmartLeitor.InstanciaDoDisco(disco, mapa));
    }
}
