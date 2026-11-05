using System.Text.Json;
using TranalSentinel;
using Xunit;

namespace TranalSentinel.Tests;

/// <summary>
/// Testes do aviso de sensor da CPU.
///
/// <para>
/// O defeito que estes testes cobrem: a coleta parava e o app acusava o
/// PawnIO ("o PawnIO está instalado, mas a leitura falhou") sem nunca dizer
/// o motivo real. Agora o erro da coleta é registrado no snapshot e aparece
/// no aviso — e o motivo conhecido (sem elevação, sem driver) continua
/// valendo mais que o erro registrado.
/// </para>
/// </summary>
public class SensorAvisoTestes
{
    [Fact]
    public void Resumo_ComTodosOsMotivos_TemTextoParaCadaUm()
    {
        Assert.Equal("Sem permissão de administrador.",
            SensorMonitor.ResumoCpuSensor(SensorMonitor.CpuSensorProblem.NotElevated));
        Assert.Equal("Falta o driver PawnIO.",
            SensorMonitor.ResumoCpuSensor(SensorMonitor.CpuSensorProblem.PawnIOMissing));
        Assert.Equal("O driver não respondeu.",
            SensorMonitor.ResumoCpuSensor(SensorMonitor.CpuSensorProblem.ReadFailed));
        Assert.Equal("", SensorMonitor.ResumoCpuSensor(SensorMonitor.CpuSensorProblem.None));
    }

    [Fact]
    public void Aviso_ComSensorBom_NaoMostraNada()
    {
        Assert.Equal("", SensorMonitor.TextoAvisoCpuSensor(SensorMonitor.CpuSensorProblem.None, "qualquer erro"));
    }

    [Fact]
    public void Aviso_SemErroRegistrado_UsaOMotivoGenerico()
    {
        Assert.Equal("O driver não respondeu.",
            SensorMonitor.TextoAvisoCpuSensor(SensorMonitor.CpuSensorProblem.ReadFailed, null));
        Assert.Equal("O driver não respondeu.",
            SensorMonitor.TextoAvisoCpuSensor(SensorMonitor.CpuSensorProblem.ReadFailed, "   "));
    }

    [Fact]
    public void Aviso_ComErroRegistrado_MostraOMotivoReal()
    {
        string aviso = SensorMonitor.TextoAvisoCpuSensor(
            SensorMonitor.CpuSensorProblem.ReadFailed, "ArgumentException: o disco sumiu");

        Assert.Equal("Falha na coleta: ArgumentException: o disco sumiu", aviso);
    }

    [Fact]
    public void Aviso_SemAdministrador_NaoSeMexeNoMotivoConhecido()
    {
        // O motivo conhecido (não está elevado) é mais exato que um erro antigo
        // que sobrou no arquivo.
        Assert.Equal("Sem permissão de administrador.",
            SensorMonitor.TextoAvisoCpuSensor(SensorMonitor.CpuSensorProblem.NotElevated, "erro antigo"));
        Assert.Equal("Falta o driver PawnIO.",
            SensorMonitor.TextoAvisoCpuSensor(SensorMonitor.CpuSensorProblem.PawnIOMissing, "erro antigo"));
    }

    [Fact]
    public void Explicacao_ComErroRegistrado_ConcatenaOMotivoReal()
    {
        string texto = SensorMonitor.TextoExplicacaoCpuSensor(
            SensorMonitor.CpuSensorProblem.ReadFailed, "falhou o acesso");

        Assert.Contains("O PawnIO está instalado, mas a leitura falhou", texto);
        Assert.Contains("A coleta parou com este erro: falhou o acesso.", texto);
    }

    [Fact]
    public void Explicacao_SemErroRegistrado_FicaNoGenerico()
    {
        string texto = SensorMonitor.TextoExplicacaoCpuSensor(SensorMonitor.CpuSensorProblem.ReadFailed, null);

        Assert.Contains("O PawnIO está instalado, mas a leitura falhou", texto);
        Assert.DoesNotContain("A coleta parou com este erro", texto);
    }

    [Fact]
    public void Explicacao_ComSensorBom_NaoTemTexto()
    {
        Assert.Equal("", SensorMonitor.TextoExplicacaoCpuSensor(SensorMonitor.CpuSensorProblem.None, "erro"));
    }

    [Fact]
    public void Snapshot_ComErroGeral_SobreviveAoRoundTrip()
    {
        var foto = new SensorSnapshot(
            Array.Empty<SensorReading>(), Array.Empty<string>(), DateTime.Now,
            ErroGeral: "ArgumentException: infinito não é JSON");

        string json = JsonSerializer.Serialize(foto, SensorValor.OpcoesJson);
        var lida = JsonSerializer.Deserialize<SensorSnapshot>(json, SensorValor.OpcoesJson);

        Assert.NotNull(lida);
        Assert.Equal("ArgumentException: infinito não é JSON", lida!.ErroGeral);
        Assert.Empty(lida.Leituras);
        Assert.Empty(lida.Falhas);
    }

    [Fact]
    public void Snapshot_DeVersaoAntiga_LeSemErroGeral()
    {
        // Arquivo gravado pela versão anterior (sem o campo novo) não pode
        // derrubar a leitura — o app tem que abrir e seguir funcionando.
        const string jsonAntigo = """
            {"Leituras":[],"Falhas":[],"Momento":"2026-11-04T12:00:00"}
            """;

        var lida = JsonSerializer.Deserialize<SensorSnapshot>(jsonAntigo, SensorValor.OpcoesJson);

        Assert.NotNull(lida);
        Assert.Null(lida!.ErroGeral);
        Assert.Empty(lida.Leituras);
    }
}
