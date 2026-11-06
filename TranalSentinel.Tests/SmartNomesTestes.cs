using TranalSentinel;
using Xunit;

namespace TranalSentinel.Tests;

/// <summary>
/// Os nomes padrão dos atributos do S.M.A.R.T.
///
/// <para>
/// O app nunca inventa nome de fabricante: id sem nome padrão vira
/// "Atributo 0x..", que é o que o CrystalDiskInfo também faz.
/// </para>
/// </summary>
public class SmartNomesTestes
{
    [Theory]
    [InlineData(1, "Raw_Read_Error_Rate")]
    [InlineData(3, "Spin_Up_Time")]
    [InlineData(4, "Retired_Sector_Ct")]
    [InlineData(5, "Reallocated_Sector_Ct")]
    [InlineData(7, "Seek_Error_Rate")]
    [InlineData(9, "Power_On_Hours")]
    [InlineData(10, "Spin_Retry_Count")]
    [InlineData(12, "Power_Cycle_Count")]
    [InlineData(187, "Reported_Uncorrect")]
    [InlineData(188, "Command_Timeout")]
    [InlineData(189, "High_Fly_Writes")]
    [InlineData(190, "Airflow_Temperature_Cel")]
    [InlineData(191, "G-Sense_Error_Rate")]
    [InlineData(192, "Power-Off_Retract_Count")]
    [InlineData(194, "Temperature_Celsius")]
    [InlineData(197, "Current_Pending_Sector")]
    [InlineData(198, "Offline_Uncorrectable")]
    [InlineData(199, "UDMA_CRC_Error_Count")]
    [InlineData(231, "Temperature_Celsius_Lifecycle")]
    [InlineData(232, "Remaining_Lifetime_Perc")]
    [InlineData(233, "Media_Wearout_Indicator")]
    [InlineData(234, "Die_Temperature_Celsius")]
    [InlineData(235, "Enhanced_Correct_Timeout")]
    public void O_id_padrao_tem_o_nome_da_tabela_ata(int id, string esperado)
    {
        Assert.Equal(esperado, SmartNomes.De(id));
    }

    [Theory]
    [InlineData(0xA5)]
    [InlineData(0xE6)]
    [InlineData(0xF1)]
    [InlineData(200)]
    [InlineData(0)]
    public void O_id_sem_nome_padrao_vira_atributo_hexadecimal(int id)
    {
        Assert.Equal("Atributo 0x" + id.ToString("X2"), SmartNomes.De(id));
    }

    [Fact]
    public void O_id_do_fabricante_nao_recebe_nome_inventado()
    {
        // 0xA5, 0xE6 e 0xF1 são do fabricante nos SanDisk: nome genérico, nunca adivinhado.
        Assert.Equal("Atributo 0xA5", SmartNomes.De(0xA5));
        Assert.Equal("Atributo 0xE6", SmartNomes.De(0xE6));
        Assert.Equal("Atributo 0xF1", SmartNomes.De(0xF1));
    }
}
