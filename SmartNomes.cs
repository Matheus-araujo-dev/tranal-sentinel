namespace TranalSentinel;

/// <summary>
/// Os nomes padrão dos atributos do S.M.A.R.T. (a tabela da ATA).
///
/// <para>
/// Só entram aqui os ids padronizados da ATA. Os ids na faixa 0xA0 ou
/// acima são específicos do fabricante: quando não há nome padrão, o app
/// mostra "Atributo 0x..". O app nunca inventa nome de fabricante — um
/// nome inventado levaria o usuário a conclusões erradas sobre o disco.
/// </para>
/// </summary>
public static class SmartNomes
{
    /// <summary>
    /// O nome padrão do atributo (ex.: "Reallocated_Sector_Ct").
    /// Id sem nome padrão vira "Atributo 0x.." (nunca um nome inventado).
    /// </summary>
    public static string De(int id) => id switch
    {
        1 => "Raw_Read_Error_Rate",
        3 => "Spin_Up_Time",
        4 => "Retired_Sector_Ct",
        5 => "Reallocated_Sector_Ct",
        7 => "Seek_Error_Rate",
        9 => "Power_On_Hours",
        10 => "Spin_Retry_Count",
        12 => "Power_Cycle_Count",
        187 => "Reported_Uncorrect",
        188 => "Command_Timeout",
        189 => "High_Fly_Writes",
        190 => "Airflow_Temperature_Cel",
        191 => "G-Sense_Error_Rate",
        192 => "Power-Off_Retract_Count",
        194 => "Temperature_Celsius",
        197 => "Current_Pending_Sector",
        198 => "Offline_Uncorrectable",
        199 => "UDMA_CRC_Error_Count",
        231 => "Temperature_Celsius_Lifecycle",
        232 => "Remaining_Lifetime_Perc",
        233 => "Media_Wearout_Indicator",
        234 => "Die_Temperature_Celsius",
        235 => "Enhanced_Correct_Timeout",
        _ => "Atributo 0x" + id.ToString("X2"),
    };
}
