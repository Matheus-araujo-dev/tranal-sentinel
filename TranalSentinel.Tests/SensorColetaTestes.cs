using System;
using System.Collections.Generic;
using LibreHardwareMonitor.Hardware;
using TranalSentinel;
using Xunit;

namespace TranalSentinel.Tests;

/// <summary>
/// Testes da volta da coleta.
///
/// <para>
/// O que estes testes cobrem: o motivo de um hardware que falhou não pode
/// sumir na volta seguinte (o hardware foi desligado, então não volta — e sem
/// o motivo guardado o app voltaria a culpar o driver sem saber de nada), e a
/// falha da CPU tem de aparecer separada, para o aviso mostrar o motivo real.
/// </para>
/// </summary>
public class SensorColetaTestes
{
    /// <summary>Hardware de mentira: só o que a coleta usa.</summary>
    private sealed class HardwareFalso : HardwareColeta
    {
        private readonly Exception? _erro;

        public HardwareFalso(string id, string nome, HardwareType tipo, Exception? erro = null,
                             IReadOnlyList<HardwareColeta>? subs = null)
        {
            Id = id;
            Nome = nome;
            Tipo = tipo;
            _erro = erro;
            Subs = subs ?? Array.Empty<HardwareColeta>();
        }

        public string Id { get; }
        public string Nome { get; }
        public HardwareType Tipo { get; }
        public IReadOnlyList<HardwareColeta> Subs { get; }

        public IReadOnlyList<SensorReading> Ler()
        {
            if (_erro is not null) throw _erro;
            return Array.Empty<SensorReading>();
        }
    }

    [Fact]
    public void HardwareQueFalha_RegistraOMotivo()
    {
        var desligados = new Dictionary<string, string>();
        var cpu = new HardwareFalso("/cpu", "AMD Ryzen 7 5700X", HardwareType.Cpu, new InvalidOperationException("acesso negado"));

        var volta = SensorColeta.MontarVolta(new[] { (HardwareColeta)cpu }, desligados, CancellationToken.None);

        Assert.Equal("acesso negado", volta.FalhaCpu);
        Assert.Contains("AMD Ryzen 7 5700X: acesso negado", volta.Falhas);
        Assert.Equal("acesso negado", desligados["/cpu"]);
    }

    [Fact]
    public void HardwareQueFalha_NaVoltaSeguinte_OMotivoContinua()
    {
        // A regressão: o hardware desligado não volta, e o motivo sumia junto
        // — o app voltava a dizer "o driver não respondeu" sem explicar nada.
        var desligados = new Dictionary<string, string>();
        var cpu = new HardwareFalso("/cpu", "AMD Ryzen 7 5700X", HardwareType.Cpu, new InvalidOperationException("acesso negado"));

        var primeira = SensorColeta.MontarVolta(new[] { (HardwareColeta)cpu }, desligados, CancellationToken.None);
        var segunda = SensorColeta.MontarVolta(new[] { (HardwareColeta)cpu }, desligados, CancellationToken.None);

        Assert.Equal("acesso negado", primeira.FalhaCpu);
        Assert.Equal("acesso negado", segunda.FalhaCpu);
        Assert.Contains("AMD Ryzen 7 5700X: acesso negado", segunda.Falhas);
    }

    [Fact]
    public void HardwareQueFalha_NaoTemCpu_OMotivoDaCpuFicaVazio()
    {
        // Uma GPU que falha não é motivo para reclamar da CPU.
        var desligados = new Dictionary<string, string>();
        var gpu = new HardwareFalso("/gpu", "AMD Radeon RX 9060 XT", HardwareType.GpuAmd, new InvalidOperationException("driver ocupado"));

        var volta = SensorColeta.MontarVolta(new[] { (HardwareColeta)gpu }, desligados, CancellationToken.None);

        Assert.Null(volta.FalhaCpu);
        Assert.Contains("AMD Radeon RX 9060 XT: driver ocupado", volta.Falhas);
    }

    [Fact]
    public void HardwareQueFalha_OsOutrosContinuam()
    {
        var desligados = new Dictionary<string, string>();
        var gpu = new HardwareFalso("/gpu", "AMD Radeon RX 9060 XT", HardwareType.GpuAmd, new InvalidOperationException("driver ocupado"));
        var cpu = new HardwareFalso("/cpu", "AMD Ryzen 7 5700X", HardwareType.Cpu);

        var volta = SensorColeta.MontarVolta(new[] { (HardwareColeta)gpu, (HardwareColeta)cpu }, desligados, CancellationToken.None);

        Assert.Null(volta.FalhaCpu);
        Assert.Single(volta.Falhas);
    }

    [Fact]
    public void HardwareQueNaoFalha_NaoTemFalhaNemMotivo()
    {
        var desligados = new Dictionary<string, string>();
        var cpu = new HardwareFalso("/cpu", "AMD Ryzen 7 5700X", HardwareType.Cpu);

        var volta = SensorColeta.MontarVolta(new[] { (HardwareColeta)cpu }, desligados, CancellationToken.None);

        Assert.Null(volta.FalhaCpu);
        Assert.Empty(volta.Falhas);
        Assert.Empty(desligados);
    }

    [Fact]
    public void SubHardwareQueFalha_NaoDerrubaOPai()
    {
        var desligados = new Dictionary<string, string>();
        var nucleo = new HardwareFalso("/cpu/core0", "Core #1", HardwareType.Cpu, new InvalidOperationException("nucleo travado"));
        var cpu = new HardwareFalso("/cpu", "AMD Ryzen 7 5700X", HardwareType.Cpu, null, new HardwareColeta[] { nucleo });

        var volta = SensorColeta.MontarVolta(new[] { (HardwareColeta)cpu }, desligados, CancellationToken.None);

        // O pai continua de pé (não foi desligado) e o motivo é do sub.
        Assert.False(desligados.ContainsKey("/cpu"));
        Assert.Equal("nucleo travado", desligados["/cpu/core0"]);
        Assert.Equal("nucleo travado", volta.FalhaCpu);
    }
}
