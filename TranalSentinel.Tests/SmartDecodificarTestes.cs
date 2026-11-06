using TranalSentinel;
using Xunit;

namespace TranalSentinel.Tests;

/// <summary>
/// O decodificador do S.M.A.R.T. testado com os bytes reais do hardware do
/// dono (dois SanDisk SSD PLUS, SATA), lidos pelas classes WMI do root\wmi.
///
/// <para>
/// A fixture é a evidência: os 512 bytes do SMART Read Data e do SMART
/// Read Thresholds de cada disco, exatamente como o Windows devolveu.
/// </para>
/// </summary>
public class SmartDecodificarTestes
{
    [Fact]
    public void O_primeiro_atributo_do_disco_real_e_o_reallocated_com_valor_100()
    {
        var atributos = SmartLeitor.Decodificar(SmartReal.Dados240Gb, SmartReal.Limiares240Gb);

        Assert.NotEmpty(atributos);
        var primeiro = atributos[0];
        Assert.Equal(5, primeiro.Id);
        Assert.Equal("Reallocated_Sector_Ct", primeiro.Nome);
        Assert.Equal(100, primeiro.Valor);
        Assert.Equal(100, primeiro.Pior);
    }

    [Fact]
    public void A_tabela_real_tem_os_atributos_do_fabricante_e_para_no_id_zero()
    {
        var atributos = SmartLeitor.Decodificar(SmartReal.Dados240Gb, SmartReal.Limiares240Gb);

        // 25 atributos no 240 GB: o registro é preenchido até o id 0x00.
        Assert.Equal(25, atributos.Count);
        Assert.Contains(atributos, a => a.Id == 0x05);   // Reallocated_Sector_Ct
        Assert.Contains(atributos, a => a.Id == 0x09);   // Power_On_Hours
        Assert.Contains(atributos, a => a.Id == 0xE8);   // 232 Remaining_Lifetime_Perc
        Assert.DoesNotContain(atributos, a => a.Id == 0x00);
    }

    [Fact]
    public void O_limiar_vem_casado_pelo_id_do_atributo()
    {
        var umTb = SmartLeitor.Decodificar(SmartReal.Dados1Tb, SmartReal.Limiares1Tb);
        var id5UmTb = umTb.First(a => a.Id == 5);
        Assert.Equal(10, id5UmTb.Limiar);

        var duzentosEQuarenta = SmartLeitor.Decodificar(SmartReal.Dados240Gb, SmartReal.Limiares240Gb);
        var id5_240 = duzentosEQuarenta.First(a => a.Id == 5);

        // Limiar 0 significa "não se aplica": o atributo fica sem limiar.
        Assert.Null(id5_240.Limiar);
    }

    [Fact]
    public void O_limiar_do_atributo_de_vida_util_vem_dos_limiares_reais()
    {
        var umTb = SmartLeitor.Decodificar(SmartReal.Dados1Tb, SmartReal.Limiares1Tb);
        Assert.Equal(4, umTb.First(a => a.Id == 0xE8).Limiar);

        var duzentosEQuarenta = SmartLeitor.Decodificar(SmartReal.Dados240Gb, SmartReal.Limiares240Gb);
        Assert.Equal(5, duzentosEQuarenta.First(a => a.Id == 0xE8).Limiar);
    }

    [Fact]
    public void Registro_truncado_devolve_o_que_deu_sem_quebrar()
    {
        // Corta no meio do segundo atributo: só o primeiro cabe inteiro.
        var cortado = SmartReal.Dados240Gb.Take(20).ToArray();
        var atributos = SmartLeitor.Decodificar(cortado, SmartReal.Limiares240Gb);

        Assert.Single(atributos);
        Assert.Equal(5, atributos[0].Id);
    }

    [Fact]
    public void Registro_curto_demais_nao_quebra()
    {
        Assert.Empty(SmartLeitor.Decodificar(Array.Empty<byte>(), null));
        Assert.Empty(SmartLeitor.Decodificar(new byte[] { 0x01, 0x00 }, null));
    }

    [Fact]
    public void O_nome_padrao_vem_do_id_e_o_desconhecido_vira_atributo_hexadecimal()
    {
        var atributos = SmartLeitor.Decodificar(SmartReal.Dados240Gb, SmartReal.Limiares240Gb);

        Assert.Equal("Reallocated_Sector_Ct", atributos.First(a => a.Id == 5).Nome);
        // 0xA5 é do fabricante: não tem nome padrão, e o app não inventa um.
        Assert.Equal("Atributo 0xA5", atributos.First(a => a.Id == 0xA5).Nome);
    }

    [Fact]
    public void O_valor_cru_sai_em_hexadecimal_e_em_decimal()
    {
        var atributos = SmartLeitor.Decodificar(SmartReal.Dados240Gb, SmartReal.Limiares240Gb);

        // Power_On_Hours do disco de 240 GB: bytes E2 C2 00 00 00 00 = 49.890 horas.
        var horasLigado = atributos.First(a => a.Id == 9);
        Assert.Equal(6, horasLigado.Cru.Length);
        Assert.Equal(new byte[] { 0xE2, 0xC2, 0x00, 0x00, 0x00, 0x00 }, horasLigado.Cru);
        Assert.Contains("0xE2C200000000", horasLigado.CruTexto, StringComparison.Ordinal);
        Assert.Contains("49890", horasLigado.CruTexto, StringComparison.Ordinal);

        // Setores realocados: zero (bytes zerados).
        var realocados = atributos.First(a => a.Id == 5);
        Assert.Contains("0x000000000000", realocados.CruTexto, StringComparison.Ordinal);
        Assert.Contains("0", realocados.CruTexto, StringComparison.Ordinal);
    }

    [Fact]
    public void As_flags_sao_as_16_bits_cruas_do_registro()
    {
        var atributos = SmartLeitor.Decodificar(SmartReal.Dados240Gb, SmartReal.Limiares240Gb);

        Assert.Equal(0x0032, atributos.First(a => a.Id == 5).Flags);
        Assert.Equal(0x0022, atributos.First(a => a.Id == 0xC2).Flags);
        Assert.Equal(0x0033, atributos.First(a => a.Id == 0xE8).Flags);
        Assert.Equal("0x0033", atributos.First(a => a.Id == 0xE8).FlagsTexto);
    }

    [Fact]
    public void So_o_atributo_de_vida_util_e_marcado_como_pre_falha()
    {
        // Descoberta no hardware real: o bit 0 (0x0001) das flags é o bit de
        // pré-falha. Entre os 27 atributos do 240 GB e os 27 do 1 TB, ele
        // aparece só no 0xE8 (232 = Remaining_Lifetime_Perc, flags 0x0033) —
        // exatamente o atributo que avisa que o disco está no fim da vida.
        var comPreFail = SmartLeitor.Decodificar(SmartReal.Dados240Gb, SmartReal.Limiares240Gb)
            .Where(a => a.PreFail)
            .Select(a => a.Id)
            .ToList();
        Assert.Equal(new[] { 232 }, comPreFail);

        var comPreFail1Tb = SmartLeitor.Decodificar(SmartReal.Dados1Tb, SmartReal.Limiares1Tb)
            .Where(a => a.PreFail)
            .Select(a => a.Id)
            .ToList();
        Assert.Equal(new[] { 232 }, comPreFail1Tb);

        // Os contadores do fabricante e os contadores de uso não são de pré-falha.
        var realocados = SmartLeitor.Decodificar(SmartReal.Dados240Gb, SmartReal.Limiares240Gb)
            .First(a => a.Id == 5);
        Assert.False(realocados.PreFail);
    }

    [Fact]
    public void Sem_limiares_todo_atributo_fica_sem_limiar()
    {
        var atributos = SmartLeitor.Decodificar(SmartReal.Dados240Gb, null);

        Assert.NotEmpty(atributos);
        Assert.All(atributos, a => Assert.Null(a.Limiar));
    }
}
