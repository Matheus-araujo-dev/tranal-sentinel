# TRANAL PAINEL (v0.31) — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Uma janela flutuante no desktop, arrastável, que mostra CPU, GPU, RAM e ventoinha com os números do instante.

**Architecture:** Um núcleo puro novo (`SensorTexto`) para as unidades dos sensores e outro (`PainelRegras`) que monta as quatro linhas do painel a partir de um retrato. Uma janela WPF nova (`PainelWindow`) consome as linhas; o `MainWindow` liga e desliga o painel e alimenta o retrato com a mesma leitura que o overlay usa (`BottleneckAnalyzer.Sample`).

**Tech Stack:** C# / .NET 10 / WPF (`net10.0-windows`), self-contained single-file. Sem PackageReference novo. Sem WinForms.

**Spec:** `docs/superpowers/specs/2026-11-09-painel-flutuante-design.md`

## Global Constraints

- Idioma **pt-BR** em comentários, XML docs, textos de tela e mensagens.
- **Regra em um lugar só**: núcleo puro; o resto consome. `catch { }` vazio só com comentário explicando por que é seguro.
- **Número errado é pior que número ausente**: valor ausente vira "—", nunca zero e nunca um valor velho.
- Todo número visível sai do `NumeroTexto` (vírgula decimal, ponto de milhar, independente da cultura do Windows). As **unidades** ("°C", "%", "GB", "MHz", "GHz", "W", "V", "RPM") saem do `SensorTexto`.
- **Não adicionar PackageReference novo**. `net10.0-windows`. Sem WinForms.
- **Baseline de avisos: 16 no app, 0 no instalador**. Qualquer aviso novo = regressão.
- Fim de linha por arquivo novo: **LF**. Não converter arquivos existentes.
- `git commit -F <arquivo UTF-8 sem BOM>` (a mensagem não pode começar com "-" nem conter palavra em inglês).
- **Nenhum processo TRANAL pode estar rodando durante os testes** (trava de instância única → 7 testes falham). Fechar com `workspace\sair.ps1` (elevado).
- O app **vive na bandeja**: fechar a janela minimiza; só o "Sair" encerra. O botão "×" do painel **esconde o painel**, não encerra o app.
- Comandos:
  ```
  dotnet build "E:\projetos\tranal-sentinel\TranalSentinel.csproj" -c Release -t:Rebuild
  dotnet test  "E:\projetos\tranal-sentinel\TranalSentinel.Tests\TranalSentinel.Tests.csproj" -c Release
  dotnet test  "E:\projetos\tranal-sentinel\Tranal.Setup.Tests\Tranal.Setup.Tests.csproj" -c Release
  dotnet publish "E:\projetos\tranal-sentinel\TranalSentinel.csproj" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -o "E:\projetos\tranal-sentinel\release-prova-v031"
  ```

---

## Arbitragens do owner (09/11/2026)

A Task 1 expôs uma contradição dentro do próprio plano: o bloco de restrições
diz que todo número visível sai do `NumeroTexto`, mas o código de implementação
da Task 1 usava `NumeroTexto.Numero(...)` — que corta os zeros à direita
(formato "0.##") e, portanto, faz o próprio teste do plano ("8,0 GB", "1,234 V")
falhar. O owner arbitrou em 09/11/2026:

1. **As casas decimais fixas moram no `NumeroTexto`.** Ele ganha a sobrecarga
   `NumeroTexto.Numero(double valor, int casas)`; o `SensorTexto` só consome.
   Continua valendo uma casa só de formatação no app.
2. **O texto do overlay pode mudar no refactor.** O overlay passa a usar o
   `SensorTexto` (°C com uma casa, ponto de milhar em MHz/RPM) e o **GHz entra
   no `SensorTexto`** — o `:0.00` do clock da CPU dependia da cultura do
   Windows. A afirmação "o texto visível não muda" valia para o cartão de
   ventoinha, não para o overlay.
3. **Um jeito só de escrever gigabytes.** `MemoriaTexto.Bytes` passa a delegar
   ao `SensorTexto.Gigabytes` ("31,9 GB" em todo lugar).

Além disso: um valor de sensor que não cabe em um inteiro exato tem que virar
"—", e não um número de 19 dígitos na tela (número errado é pior que número
ausente).

A revisão da Task 1 (09/11/2026) achou mais duas divergências de unidade, do
mesmo tipo da que o owner já arbitrou para gigabytes — e o owner arbitrou de
novo:

4. **"%" é inteiro em todo lugar.** `NumeroTexto.Percentual` (que vivia em
   produção no alerta "41,18% abaixo do recorde") é **removido**, sem
   compatibilidade artificial: o `SensorTexto.Porcentual` passa a ser o único
   "%" do app, e o alerta passa a dizer "41% abaixo do recorde".
5. **"V" tem um formatador só, com a casa certa por grandeza.**
   `SensorTexto.Volts(double? valor, int casas = 3)`; a tensão do módulo de
   memória pede 2 casas ("1,35 V") e a do núcleo da GPU mantém 3 ("1,234 V").

A dívida antiga de "%" fora do núcleo (`UsoTexto.Percentual`, que escreve
"42,4 %", e o `BatteryInfo`, que escreve "50%") fica **fora da v0.31**: é
dívida de antes, em outro subsistema, e mexer nela aqui seria ampliar o
escopo sem plano.

---

### Task 1: `SensorTexto` — o núcleo puro das unidades dos sensores

O overlay (`MainWindow.Overlay.cs:84`) formata os números com interpolação crua (`$"{x:0.0}"`), ou seja, **depende da cultura do Windows**. O painel novo precisa dos mesmos números. Este núcleo é o único lugar onde "temperatura", "porcentagem", "gigabytes" etc. viram texto — e ele é testável sem WPF.

**Files:**
- Create: `SensorTexto.cs`
- Modify: `NumeroTexto.cs` (a sobrecarga `Numero(valor, casas)` — arbitragem 1; e o `Percentual` sai — arbitragem 4)
- Modify: `VentoinhaRegras.cs` (a linha do alerta de degradação passa a usar o `SensorTexto.Porcentual` — arbitragem 4)
- Test: `TranalSentinel.Tests/SensorTextoTestes.cs`
- Test: `TranalSentinel.Tests/NumeroTextoTestes.cs` (a sobrecarga nova)
- Test: `TranalSentinel.Tests/VentoinhaRegrasTestes.cs` (o percentual do alerta passa a ser "41%")

**Interfaces:**
- Produces: `public static class SensorTexto` com
  - `string Temperatura(double? valor)` → "50,4 °C" / "—"
  - `string Porcentual(double? valor)` → "47%" / "—"
  - `string Gigabytes(double? valor)` → "8,0 GB" / "—"
  - `string Mhz(double? valor)` → "2.790 MHz" / "—"
  - `string Gigahertz(double? valor)` → "4,45 GHz" / "—"
  - `string Watts(double? valor)` → "120 W" / "—"
  - `string Volts(double? valor)` → "1,234 V" / "—"
  - `string Rpm(double? valor)` → "3.409 RPM" / "—"

- [ ] **Step 1: Escreva o teste que falha**

Crie `TranalSentinel.Tests/SensorTextoTestes.cs`:

```csharp
using System.Globalization;
using Xunit;

namespace TranalSentinel.Tests;

/// <summary>
/// Testes do formatador de unidades dos sensores.
///
/// <para>
/// O mesmo número tem que sair do mesmo jeito em PCs de culturas
/// diferentes: vírgula decimal e ponto de milhar, sempre.
/// </para>
/// </summary>
public class SensorTextoTestes
{
    [Fact]
    public void Temperatura_ComValor_ArredondaParaUmaCasa()
    {
        Assert.Equal("50,4 °C", SensorTexto.Temperatura(50.35));
        Assert.Equal("50 °C", SensorTexto.Temperatura(50.0));
    }

    [Fact]
    public void Porcentual_ComValor_ArredondaParaInteiro()
    {
        Assert.Equal("47%", SensorTexto.Porcentual(47.4));
        Assert.Equal("48%", SensorTexto.Porcentual(47.6));
    }

    [Fact]
    public void Gigabytes_ComValor_ArredondaParaUmaCasa()
    {
        Assert.Equal("8,0 GB", SensorTexto.Gigabytes(8.04));
    }

    [Fact]
    public void Mhz_ComValor_UsaPontoDeMilhar()
    {
        Assert.Equal("2.790 MHz", SensorTexto.Mhz(2790));
    }

    [Fact]
    public void Rpm_ComValor_UsaPontoDeMilhar()
    {
        Assert.Equal("3.409 RPM", SensorTexto.Rpm(3409.4));
    }

    [Fact]
    public void ValorAusente_ViraEmDash()
    {
        Assert.Equal("—", SensorTexto.Temperatura(null));
        Assert.Equal("—", SensorTexto.Porcentual(null));
        Assert.Equal("—", SensorTexto.Gigabytes(null));
        Assert.Equal("—", SensorTexto.Mhz(null));
        Assert.Equal("—", SensorTexto.Watts(null));
        Assert.Equal("—", SensorTexto.Volts(null));
        Assert.Equal("—", SensorTexto.Rpm(null));
    }

    [Fact]
    public void ValorNaoFinito_ViraEmDash()
    {
        // O sensor pode devolver infinito ou NaN. O numero texto cuida
        // disso quando o valor vira texto decimal, mas a conversao para
        // inteiro nao: sem a guarda, o infinito viraria um numero
        // gigante de 19 digitos na tela.
        Assert.Equal("—", SensorTexto.Temperatura(double.NaN));
        Assert.Equal("—", SensorTexto.Porcentual(double.NaN));
        Assert.Equal("—", SensorTexto.Porcentual(double.PositiveInfinity));
        Assert.Equal("—", SensorTexto.Mhz(double.PositiveInfinity));
        Assert.Equal("—", SensorTexto.Watts(double.NaN));
        Assert.Equal("—", SensorTexto.Volts(double.NaN));
        Assert.Equal("—", SensorTexto.Rpm(double.PositiveInfinity));
    }

    [Fact]
    public void OsNumerosNaoDependemDaCulturaDoWindows()
    {
        var cultura = CultureInfo.CurrentCulture;

        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("en-US");

            Assert.Equal("50,4 °C", SensorTexto.Temperatura(50.35));
            Assert.Equal("8,0 GB", SensorTexto.Gigabytes(8.04));
            Assert.Equal("3.409 RPM", SensorTexto.Rpm(3409.4));
        }
        finally
        {
            CultureInfo.CurrentCulture = cultura;
        }
    }

    [Fact]
    public void Gigahertz_ComValor_UsaDuasCasas()
    {
        Assert.Equal("4,45 GHz", SensorTexto.Gigahertz(4.451));
    }

    [Fact]
    public void Volts_ComDuasCasas_UsasAsDuasCasas()
    {
        // A casa e um parametro porque a grandeza manda: o modulo de
        // memoria tem duas casas, o nucleo da GPU tem tres.
        Assert.Equal("1,35 V", SensorTexto.Volts(1.35, 2));
        Assert.Equal("1,234 V", SensorTexto.Volts(1.234));
    }

    [Fact]
    public void ZeroNegativo_NaoViraSinal()
    {
        // O arredondamento pode devolver um zero com sinal: "-0 °C" na tela
        // seria numero errado.
        Assert.Equal("0 °C", SensorTexto.Temperatura(-0.04));
        Assert.Equal("0,0 GB", SensorTexto.Gigabytes(-0.02));
        Assert.Equal("0,000 V", SensorTexto.Volts(-0.0004));
    }

    [Fact]
    public void ValorQueNaoCabeEmUmInteiro_ViraEmDash()
    {
        // Um sensor que devolvesse 1e30 faria o cast saturar em
        // long.MaxValue e a tela mostraria 19 digitos. Numero errado e
        // pior que numero ausente.
        Assert.Equal("—", SensorTexto.Rpm(1e30));
        Assert.Equal("—", SensorTexto.Porcentual(1e30));
        Assert.Equal("—", SensorTexto.Mhz(1e30));
        Assert.Equal("—", SensorTexto.Gigabytes(1e30));
        Assert.Equal("—", SensorTexto.Volts(1e30));
        Assert.Equal("—", SensorTexto.Gigahertz(1e30));
    }
}
```

E, no arquivo novo `TranalSentinel.Tests/NumeroTextoTestes.cs`, o teste da
sobrecarga que nasce na Task 1 (as casas fixas moram no `NumeroTexto`, e não
dentro do `SensorTexto`):

```csharp
using System.Globalization;
using Xunit;

namespace TranalSentinel.Tests;

/// <summary>
/// Testes do formatador de números da casa.
/// </summary>
public class NumeroTextoTestes
{
    [Fact]
    public void NumeroComCasas_GaranteAsCasas()
    {
        Assert.Equal("8,0", NumeroTexto.Numero(8.0, 1));
        Assert.Equal("1,234", NumeroTexto.Numero(1.234, 3));
    }

    [Fact]
    public void NumeroComCasas_Arredonda()
    {
        Assert.Equal("8,0", NumeroTexto.Numero(8.04, 1));
        Assert.Equal("4,45", NumeroTexto.Numero(4.451, 2));
    }

    [Fact]
    public void NumeroComCasas_NaoDependeDaCultura()
    {
        var cultura = CultureInfo.CurrentCulture;

        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("en-US");

            Assert.Equal("1,234", NumeroTexto.Numero(1.234, 3));
        }
        finally
        {
            CultureInfo.CurrentCulture = cultura;
        }
    }

    [Fact]
    public void NumeroComCasas_ComValorNaoFinito_ViraEmDash()
    {
        Assert.Equal("—", NumeroTexto.Numero(double.NaN, 1));
        Assert.Equal("—", NumeroTexto.Numero(double.PositiveInfinity, 3));
    }
}
```

- [ ] **Step 2: Rode o teste e veja falhar**

```
dotnet test "E:\projetos\tranal-sentinel\TranalSentinel.Tests\TranalSentinel.Tests.csproj" -c Release --filter "FullyQualifiedName~SensorTextoTestes"
```
Esperado: erro de compilação — `SensorTexto` não existe.

- [ ] **Step 3: Escreva a implementação mínima**

Crie `SensorTexto.cs` (LF, no raiz do projeto):

```csharp
using System.Globalization;

namespace TranalSentinel;

/// <summary>
/// O texto das unidades dos sensores — o único lugar onde "temperatura",
/// "porcentagem", "gigabytes" etc. viram texto.
///
/// <para>
/// O número sai sempre do <see cref="NumeroTexto"/> (vírgula decimal, ponto de
/// milhar, independente da cultura do Windows). A unidade mora aqui, e não
/// espalhada pelo overlay, pelo painel e pela aba de ventoinhas: o mesmo
/// número tem que sair do mesmo jeito em todo o app.
///
/// <para>
/// Valor ausente vira "—" (nunca zero e nunca um valor velho): número errado
/// é pior que número ausente.
/// </para>
/// </summary>
public static class SensorTexto
{
    /// <summary>O texto de um valor que não deu para ler.</summary>
    public const string Ausente = "—";

    /// <summary>Uma temperatura em °C (uma casa decimal).</summary>
    public static string Temperatura(double? valor) =>
        valor is double v && CabeNaTela(v)
            ? NumeroTexto.Numero(Math.Round(v, 1, MidpointRounding.AwayFromZero)) + " °C"
            : Ausente;

    /// <summary>Uma porcentagem (inteiro, arredondado).</summary>
    public static string Porcentual(double? valor) =>
        valor is double v && CabeNaTela(v)
            ? NumeroTexto.Inteiro((long)Math.Round(v, MidpointRounding.AwayFromZero)) + "%"
            : Ausente;

    /// <summary>Um tamanho em gigabytes (uma casa decimal, sempre presente).</summary>
    public static string Gigabytes(double? valor) =>
        valor is double v && CabeNaTela(v)
            ? NumeroTexto.Numero(Math.Round(v, 1, MidpointRounding.AwayFromZero), 1) + " GB"
            : Ausente;

    /// <summary>Uma frequência em MHz.</summary>
    public static string Mhz(double? valor) =>
        valor is double v && CabeNaTela(v)
            ? NumeroTexto.Inteiro((long)Math.Round(v, MidpointRounding.AwayFromZero)) + " MHz"
            : Ausente;

    /// <summary>Uma frequência em GHz (duas casas decimais).</summary>
    public static string Gigahertz(double? valor) =>
        valor is double v && CabeNaTela(v)
            ? NumeroTexto.Numero(Math.Round(v, 2, MidpointRounding.AwayFromZero), 2) + " GHz"
            : Ausente;

    /// <summary>Uma potência em watts.</summary>
    public static string Watts(double? valor) =>
        valor is double v && CabeNaTela(v)
            ? NumeroTexto.Inteiro((long)Math.Round(v, MidpointRounding.AwayFromZero)) + " W"
            : Ausente;

    /// <summary>Uma tensão em volts (três casas decimais por padrão).</summary>
    ///
    /// <para>
    /// A casa é um parâmetro porque a grandeza manda: a tensão do núcleo da
    /// GPU tem três casas ("1,234 V") e a de um módulo de memória tem duas
    /// ("1,35 V"). O formatador é um só — o que muda é a casa pedida.
    /// </para>
    public static string Volts(double? valor, int casas = 3) =>
        valor is double v && CabeNaTela(v)
            ? NumeroTexto.Numero(
                Math.Round(v, Math.Clamp(casas, 0, 15), MidpointRounding.AwayFromZero),
                Math.Clamp(casas, 0, 15)) + " V"
            : Ausente;

    /// <summary>Um número de rotações por minuto.</summary>
    public static string Rpm(double? valor) =>
        valor is double v && CabeNaTela(v)
            ? NumeroTexto.Inteiro((long)Math.Round(v, MidpointRounding.AwayFromZero)) + " RPM"
            : Ausente;

    /// <summary>
    /// O valor é um número que cabe na tela: um inteiro exato dentro do
    /// limite do <see cref="double"/> (2^53) e, portanto, dentro do
    /// <see cref="long"/>.
    ///
    /// <para>
    /// Um sensor que devolvesse 1e30 faria o cast saturar em
    /// <see cref="long.MaxValue"/> e a tela mostraria
    /// "9.223.372.036.854.775.807 RPM" — 19 dígitos. Número errado é pior
    /// que número ausente: fora do limite, o texto é "—".
    /// </para>
    /// </summary>
    private static bool CabeNaTela(double valor) =>
        double.IsFinite(valor) && Math.Abs(valor) <= 9.007199254740992E15;
}
```

E, no `NumeroTexto.cs` (arquivo existente), a sobrecarga que recebe o
número de casas fixas — as casas decimais moram no formatador de números da casa,
e não dentro do `SensorTexto`:

```csharp
    /// <summary>
    /// Número no padrão brasileiro (vírgula decimal) com um número fixo de
    /// casas decimais ("8,0", "1,234").
    ///
    /// <para>
    /// O <see cref="Numero(double)"/> corta os zeros à direita (formato
    /// "0.##"), e isso não serve para as unidades que pedem casas fixas
    /// (gigabytes, volts, gigahertz). Esta sobrecarga garante as casas, no
    /// mesmo padrão do app: vírgula decimal e independente da cultura do
    /// Windows.
    /// </para>
    /// </summary>
    public static string Numero(double valor, int casas)
    {
        if (!double.IsFinite(valor)) return "—";
        int fixas = Math.Clamp(casas, 0, 15);
        // O zero negativo (-0) aparece quando o arredondamento devolve um
        // zero com sinal: na tela sairia "-0 °C". Numero errado e pior que
        // numero ausente: o zero negativo e o zero.
        double limpo = valor == 0 ? 0 : valor;
        return limpo.ToString("F" + fixas, CultureInfo.InvariantCulture).Replace('.', ',');
    }
```
```

- [ ] **Step 3b: Tire o `NumeroTexto.Percentual` (arbitragem 4)**

O `NumeroTexto.Percentual` escrevia "%" em dois formatos diferentes do app
("41,18%" no alerta da ventoinha, "47%" no novo `SensorTexto`). Ele sai, sem
compatibilidade artificial — e o "%" fica em um lugar só, o `SensorTexto`:

```csharp
// No NumeroTexto.cs, apague o metodo Percentual.
//
// No VentoinhaRegras.cs (a linha do alerta de degradacao, em 562):
                $"{SensorTexto.Porcentual(perda * 100)} abaixo do recorde, com a temperatura no mesmo patamar ou mais alta.",
```

E, no `TranalSentinel.Tests/VentoinhaRegrasTestes.cs`, as duas asserções que
travam "41,18%" passam a travar "41%":

```csharp
            Assert.Equal("41%", SensorTexto.Porcentual(41.176));
```

**O zero negativo também sai.** O `Math.Round` pode devolver um zero com sinal
(-0), e o .NET 10 imprime o sinal: "-0 °C" na tela é número errado. A
normalização fica no `NumeroTexto` (é o lugar de formatação de número do app),
nos dois métodos de número decimal — o de uma casa e o de casas fixas:

```csharp
    public static string Numero(double valor)
    {
        if (!double.IsFinite(valor)) return "—";
        // O zero negativo (-0) sai de um arredondamento; na tela viraria
        // "-0". Numero errado e pior que numero ausente.
        double limpo = valor == 0 ? 0 : valor;
        return limpo.ToString("0.##", CultureInfo.InvariantCulture).Replace('.', ',');
    }
```

- [ ] **Step 4: Rode o teste e veja passar**

```
dotnet test "E:\projetos\tranal-sentinel\TranalSentinel.Tests\TranalSentinel.Tests.csproj" -c Release --filter "FullyQualifiedName~SensorTextoTestes"
```
Esperado: todos PASS.

- [ ] **Step 5: Commit**

Antes do commit, grave a mensagem num arquivo UTF-8 **sem BOM** em <workspace>/msg-v031-t1.txt:

```
O texto das unidades dos sensores mora em um lugar so

O overlay formatava os numeros com interpolacao crua, e isso dependia da
cultura do Windows: o mesmo numero saia de dois jeitos em PCs diferentes.
Agora existe um nucleo puro (SensorTexto) que e o unico lugar onde
temperatura, porcentagem, gigabytes, megahertz, gigahertz, watts, volts e rpm
viram texto. O numero continua saindo do NumeroTexto, e valor ausente, nao
finito ou fora do limite de um inteiro vira traco.

As casas decimais fixas (gigabytes, volts, gigahertz) moram no NumeroTexto,
que ganhou a sobrecarga Numero(valor, casas): continua havendo um lugar so
de formacao de numero no app.

Testes: o novo SensorTextoTestes, o novo NumeroTextoTestes, mais a suite do
app inteira.
```

```bash
git add SensorTexto.cs NumeroTexto.cs TranalSentinel.Tests/SensorTextoTestes.cs TranalSentinel.Tests/NumeroTextoTestes.cs
git commit -F "<workspace>/msg-v031-t1.txt"
```
(A mensagem vai num arquivo UTF-8 **sem BOM**; não pode começar com "-" nem conter palavra em inglês.)

---

### Task 2: `PainelRegras` — o núcleo puro que monta as quatro linhas do painel

**Files:**
- Create: `PainelRegras.cs`
- Test: `TranalSentinel.Tests/PainelRegrasTestes.cs`

**Interfaces:**
- Consumes: `SensorTexto` (Task 1).
- Produces:
  - `public sealed record PainelVentoinha(string Nome, double? Rpm)`
  - `public sealed record PainelRetrato(double? TemperaturaCpu, double? CargaCpu, double? TemperaturaGpu, double? CargaGpu, double? RamPct, IReadOnlyList<PainelVentoinha> Ventoinhas)`
  - `public sealed record PainelLinha(string Rotulo, string Valor)`
  - `public static class PainelRegras` com
    - `IReadOnlyList<PainelLinha> Linhas(PainelRetrato retrato)`
    - `PainelVentoinha? EscolherVentoinha(IReadOnlyList<PainelVentoinha> ventoinhas)`

- [ ] **Step 1: Escreva os testes que falham**

Crie `TranalSentinel.Tests/PainelRegrasTestes.cs`:

```csharp
using Xunit;

namespace TranalSentinel.Tests;

/// <summary>
/// Testes das linhas do painel flutuante.
///
/// <para>
/// Quatro blocos, sempre, na mesma ordem: CPU, GPU, RAM e ventoinha. O que
/// não deu para ler vira "—" — nunca zero, nunca o valor de antes.
/// </para>
/// </summary>
public class PainelRegrasTestes
{
    private static PainelRetrato Retrato(
        double? tempCpu = 50.35, double? cargaCpu = 47,
        double? tempGpu = 44, double? cargaGpu = 12,
        double? ram = 68,
        IReadOnlyList<PainelVentoinha>? ventoinhas = null) =>
        new(tempCpu, cargaCpu, tempGpu, cargaGpu, ram,
            ventoinhas ?? new[] { new PainelVentoinha("Fan #1", 3409) });

    [Fact]
    public void Linhas_SempreComOsQuatroBlocosNaOrdem()
    {
        var linhas = PainelRegras.Linhas(Retrato());

        Assert.Equal(4, linhas.Count);
        Assert.Equal("CPU", linhas[0].Rotulo);
        Assert.Equal("GPU", linhas[1].Rotulo);
        Assert.Equal("RAM", linhas[2].Rotulo);
        Assert.Equal("VENT", linhas[3].Rotulo);
    }

    [Fact]
    public void LinhaDaCpu_ComTemperaturaECarga_MostraOsDois()
    {
        var cpu = PainelRegras.Linhas(Retrato())[0];

        Assert.Equal("50,4 °C · 47%", cpu.Valor);
    }

    [Fact]
    public void LinhaDaCpu_ComSoTemperatura_MostraSoEla()
    {
        var cpu = PainelRegras.Linhas(Retrato(cargaCpu: null))[0];

        Assert.Equal("50,4 °C", cpu.Valor);
    }

    [Fact]
    public void LinhaDaCpu_SemValor_NaoMostraZero()
    {
        var cpu = PainelRegras.Linhas(Retrato(tempCpu: null, cargaCpu: null))[0];

        Assert.Equal("—", cpu.Valor);
    }

    [Fact]
    public void LinhaDaRam_ComValor_MostraOPorcentual()
    {
        var ram = PainelRegras.Linhas(Retrato())[2];

        Assert.Equal("68%", ram.Valor);
    }

    [Fact]
    public void LinhaDaRam_SemValor_ViraEmDash()
    {
        var ram = PainelRegras.Linhas(Retrato(ram: null))[2];

        Assert.Equal("—", ram.Valor);
    }

    [Fact]
    public void EscolherVentoinha_ComVariasAMaiorRpmVence()
    {
        var escolhida = PainelRegras.EscolherVentoinha(new[]
        {
            new PainelVentoinha("Fan #1", 3409),
            new PainelVentoinha("Fan #2", 0),
            new PainelVentoinha("GPU Fan", 1407),
        });

        Assert.Equal("Fan #1", escolhida!.Nome);
    }

    [Fact]
    public void EscolherVentoinha_ComEmpateEscolheOMesmoNomeSempre()
    {
        var escolhidas = PainelRegras.EscolherVentoinha(new[]
        {
            new PainelVentoinha("Fan #3", 2000),
            new PainelVentoinha("Fan #1", 2000),
        });

        Assert.Equal("Fan #1", escolhidas!.Nome);
    }

    [Fact]
    public void EscolherVentoinha_SemNenhumaLendo_EscolheUmaComRpmAusente()
    {
        var escolhida = PainelRegras.EscolherVentoinha(new[]
        {
            new PainelVentoinha("Fan #2", null),
            new PainelVentoinha("Fan #3", null),
        });

        Assert.Equal("Fan #2", escolhida!.Nome);
        Assert.Null(escolhida.Rpm);
    }

    [Fact]
    public void EscolherVentoinha_SemNenhumaVentoinha_DevolveNulo()
    {
        Assert.Null(PainelRegras.EscolherVentoinha(Array.Empty<PainelVentoinha>()));
    }

    [Fact]
    public void LinhaDaVentoinha_SemLeitura_NaoMostraORpmVelho()
    {
        var vent = PainelRegras.Linhas(Retrato(ventoinhas: new[]
        {
            new PainelVentoinha("Fan #1", null),
        }))[3];

        // A ventoinha que parou de dar leitura mostra o nome e um travessão:
        // o RPM velho não pode aparecer como se fosse o de agora.
        Assert.Equal("Fan #1 · —", vent.Valor);
    }

    [Fact]
    public void LinhaDaVentoinha_ComLeitura_MostraONomeEORpm()
    {
        var vent = PainelRegras.Linhas(Retrato())[3];

        Assert.Equal("Fan #1 · 3.409 RPM", vent.Valor);
    }

    [Fact]
    public void LinhaDaVentoinha_SemVentoinhaNoApp_ViraEmDash()
    {
        var vent = PainelRegras.Linhas(Retrato(ventoinhas: Array.Empty<PainelVentoinha>()))[3];

        Assert.Equal("—", vent.Valor);
    }
}
```

- [ ] **Step 2: Rode os testes e veja falhar**

```
dotnet test "E:\projetos\tranal-sentinel\TranalSentinel.Tests\TranalSentinel.Tests.csproj" -c Release --filter "FullyQualifiedName~PainelRegrasTestes"
```
Esperado: erro de compilação — `PainelRegras` não existe.

- [ ] **Step 3: Escreva a implementação mínima**

Crie `PainelRegras.cs` (LF):

```csharp
namespace TranalSentinel;

/// <summary>
/// Uma ventoinha como o painel a mostra: o nome legível e o RPM de agora
/// (null quando ela não está dando leitura).
/// </summary>
/// <param name="Nome">O nome do sensor ("Fan #1"), nunca o id interno.</param>
/// <param name="Rpm">As rotações por minuto (null quando não deu para ler).</param>
public sealed record PainelVentoinha(string Nome, double? Rpm);

/// <summary>
/// O retrato do PC que o painel mostra agora.
/// </summary>
/// <param name="TemperaturaCpu">A temperatura da CPU em °C (null se não deu para ler).</param>
/// <param name="CargaCpu">A carga total da CPU em % (null se não deu para ler).</param>
/// <param name="TemperaturaGpu">A temperatura da GPU em °C (null se não deu para ler).</param>
/// <param name="CargaGpu">A carga da GPU em % (null se não deu para ler).</param>
/// <param name="RamPct">A memória RAM em uso em % (null se não deu para ler).</param>
/// <param name="Ventoinhas">As ventoinhas que o app conhece agora.</param>
public sealed record PainelRetrato(
    double? TemperaturaCpu,
    double? CargaCpu,
    double? TemperaturaGpu,
    double? CargaGpu,
    double? RamPct,
    IReadOnlyList<PainelVentoinha> Ventoinhas);

/// <summary>Uma linha do painel: o rótulo e o valor (prontos para a tela).</summary>
public sealed record PainelLinha(string Rotulo, string Valor);

/// <summary>
/// As regras do painel flutuante — toda ela mora aqui.
///
/// <para>
/// O painel mostra quatro blocos, sempre na mesma ordem: CPU, GPU, RAM e
/// ventoinha. O que não deu para ler vira "—" (nunca zero e nunca um valor
/// velho) e os números saem do <see cref="SensorTexto"/>.
///
/// <para>
/// A ventoinha mostrada é a de <b>maior RPM entre as que estão dando
/// leitura</b> (a que está trabalhando). Sem isso, o painel mostraria a
/// ventoinha parada e o dono pensaria que o PC está com ela morta.
/// </para>
/// </summary>
public static class PainelRegras
{
    /// <summary>As quatro linhas do painel.</summary>
    public static IReadOnlyList<PainelLinha> Linhas(PainelRetrato retrato)
    {
        return new[]
        {
            new PainelLinha("CPU", Juntar(SensorTexto.Temperatura(retrato.TemperaturaCpu), SensorTexto.Porcentual(retrato.CargaCpu))),
            new PainelLinha("GPU", Juntar(SensorTexto.Temperatura(retrato.TemperaturaGpu), SensorTexto.Porcentual(retrato.CargaGpu))),
            new PainelLinha("RAM", SensorTexto.Porcentual(retrato.RamPct)),
            new PainelLinha("VENT", ValorDaVentoinha(EscolherVentoinha(retrato.Ventoinhas))),
        };
    }

    /// <summary>
    /// A ventoinha que o painel mostra: a de maior RPM entre as que estão
    /// dando leitura. Se nenhuma estiver dando leitura, a primeira que o app
    /// conhece (sem RPM — o número que não existe não é inventado).
    /// </summary>
    public static PainelVentoinha? EscolherVentoinha(IReadOnlyList<PainelVentoinha> ventoinhas)
    {
        if (ventoinhas.Count == 0) return null;

        var comLeitura = ventoinhas.Where(v => v.Rpm is not null).ToList();

        if (comLeitura.Count > 0)
        {
            // O desempate é pelo nome: duas ventoinhas com o mesmo RPM têm
            // que dar sempre o mesmo painel.
            return comLeitura
                .OrderByDescending(v => v.Rpm!.Value)
                .ThenBy(v => v.Nome, StringComparer.Ordinal)
                .First();
        }

        return ventoinhas.OrderBy(v => v.Nome, StringComparer.Ordinal).First();
    }

    /// <summary>O valor da linha da ventoinha ("Fan #1 · 3.409 RPM").</summary>
    private static string ValorDaVentoinha(PainelVentoinha? ventoinha)
    {
        if (ventoinha is null) return SensorTexto.Ausente;

        string rpm = SensorTexto.Rpm(ventoinha.Rpm);
        if (string.IsNullOrWhiteSpace(ventoinha.Nome)) return rpm;

        return ventoinha.Nome + " · " + rpm;
    }

    /// <summary>
    /// Junta as partes de uma linha: o que não deu para ler some (e não
    /// vira "— · —").
    /// </summary>
    private static string Juntar(params string[] partes)
    {
        var uteis = partes.Where(p => p != SensorTexto.Ausente).ToList();
        if (uteis.Count == 0) return SensorTexto.Ausente;
        return string.Join(" · ", uteis);
    }
}
```

- [ ] **Step 4: Rode os testes e veja passar**

```
dotnet test "E:\projetos\tranal-sentinel\TranalSentinel.Tests\TranalSentinel.Tests.csproj" -c Release --filter "FullyQualifiedName~PainelRegrasTestes"
```
Esperado: todos PASS.

- [ ] **Step 5: Commit**

Antes do commit, grave a mensagem num arquivo UTF-8 **sem BOM** em <workspace>/msg-v031-t2.txt:

```
O painel flutuante: o nucleo puro das quatro linhas

O painel mostra CPU, GPU, RAM e ventoinha. O nucleo puro (PainelRegras)
monta as quatro linhas a partir de um retrato, sem conhecer WPF e sem
conhecer os sensores. O que nao deu para ler vira traco, e a ventoinha
mostrada e a de maior RPM entre as que estao dando leitura (a que esta
trabalhando) - sem isso o painel mostraria a ventoinha parada e o dono
pensaria que ela morreu.

Testes: o novo PainelRegrasTestes.
```

```bash
git add PainelRegras.cs TranalSentinel.Tests/PainelRegrasTestes.cs
git commit -F "<workspace>/msg-v031-t2.txt"
```

---

### Task 3: Os consumidores passam a usar o `SensorTexto` (refactor)

O `BuildOverlayLines` (`MainWindow.Overlay.cs:84`) e o `VentoinhaRegras` formattam os números por conta própria. Depois desta task, **todo** número de sensor do app sai do `NumeroTexto`, com a unidade do `SensorTexto`.

**Files:**
- Modify: `MainWindow.Overlay.cs` (as linhas `T(...)`, `P(...)` e as interpolações de clock, power, volt, VRAM e fan)
- Modify: `VentoinhaRegras.cs` (`TextoDaTemperatura` e `TextoDoRpm` delegam ao `SensorTexto`)
- Modify: `MemoriaTexto.cs` (`Bytes` delega ao `SensorTexto.Gigabytes` — arbitragem 3)
- Test: `TranalSentinel.Tests/VentoinhaRegrasTestes.cs` (o texto da ventoinha não muda — os testes existentes têm que continuar verdes)

**Interfaces:**
- Consumes: `SensorTexto` (Task 1).
- Produces: nada novo na API. **O texto do overlay muda** (arbitragem 2): o °C
  passa a ter uma casa ("51,4 °C"), o MHz e o RPM ganham ponto de milhar
  ("2.790 MHz", "1.407 RPM") e o clock da CPU sai em GHz com duas casas
  ("4,45 GHz") — igual ao cartão de ventoinha. O texto do **cartão de
  ventoinha** não muda.

- [ ] **Step 1: Escreva o teste que prende o texto atual**

No fim de `TranalSentinel.Tests/VentoinhaRegrasTestes.cs`, acrescente:

```csharp
    [Fact]
    public void OTextoDaTemperaturaEDoRpmSaoOsDoFormatadorDaCasa()
    {
        // O texto visível não muda com o refactor: ele só passa a sair do
        // mesmo lugar que o resto do app.
        Assert.Equal("50,4 °C", VentoinhaRegras.TextoDaTemperatura(50.35));
        Assert.Equal("3.409 RPM", VentoinhaRegras.TextoDoRpm(3409.4));
    }
```

- [ ] **Step 2: Rode a suíte e veja que ela passa antes do refactor**

```
dotnet test "E:\projetos\tranal-sentinel\TranalSentinel.Tests\TranalSentinel.Tests.csproj" -c Release --filter "FullyQualifiedName~VentoinhaRegrasTestes"
```
Esperado: PASS (o texto já é o mesmo — o refactor é de origem, não de resultado).

- [ ] **Step 3: Faça o refactor em `VentoinhaRegras.cs`**

Troque o corpo de `TextoDaTemperatura` e `TextoDoRpm` por delegações:

```csharp
    /// <summary>Uma temperatura no padrão da tela ("85,4 °C").</summary>
    public static string TextoDaTemperatura(double temperatura) =>
        SensorTexto.Temperatura(temperatura);

    /// <summary>Um RPM no padrão da tela ("3.392 RPM").</summary>
    public static string TextoDoRpm(double rpm) => SensorTexto.Rpm(rpm);
```

(Mantenha os comentários XML existentes acima de cada método; troque só o corpo e a assinatura de `TextoDoRpm` — que hoje tem uma guarda de `double.IsFinite`. A guarda passa a morar no `SensorTexto`: um valor não finito tem que virar "—", não um número gigante. **Verifique**: o `SensorTexto` já tem a guarda `double.IsFinite` em todos os formatadores (Step 3 da Task 1), e o teste `ValorNaoFinito_ViraEmDash` já prende isso — o `TextoDoRpm` que delegue para ele fica com a mesma proteção.)

- [ ] **Step 4: Faça o refactor em `MainWindow.Overlay.cs`**

No `BuildOverlayLines`, troque os formatadores locais e as interpolações:

```csharp
        string T(float? v) => SensorTexto.Temperatura(v);
        string P(double? v) => SensorTexto.Porcentual(v);
```

E as linhas que usam clock, power, volt, VRAM e fan:

```csharp
        if (show.Contains("gpu_clock")) gpu.Add(MainGpuClock(Nvml.ClockGraphics, "GPU Core") is uint c ? SensorTexto.Mhz(c) : SensorTexto.Ausente);
        if (show.Contains("gpu_memclock")) gpu.Add(MainGpuClock(Nvml.ClockMem, "GPU Memory") is uint mc ? SensorTexto.Mhz(mc) : "mem —");
        if (show.Contains("gpu_power")) gpu.Add(MainGpuPower() is float w ? SensorTexto.Watts(w) : SensorTexto.Ausente);
        if (show.Contains("gpu_volt")) gpu.Add(GpuRow(SensorType.Voltage, "GPU Core")?.RawValue is float v ? SensorTexto.Volts(v) : SensorTexto.Ausente);
        if (show.Contains("gpu_fan"))
            gpu.Add(GpuRow(SensorType.Fan, "GPU Fan", true)?.RawValue is float rpm && rpm > 0 ? SensorTexto.Rpm(rpm)
                  : GpuRow(SensorType.Control, "GPU Fan", true)?.RawValue is float pct ? $"fan {SensorTexto.Porcentual(pct)}" : "fan —");
```

E a VRAM:

```csharp
        if (show.Contains("vram_used")) vram.Add(vr is { } a ? SensorTexto.Gigabytes(a.Used) : SensorTexto.Ausente);
```

E o clock da CPU (que hoje sai com `:0.00` e depende da cultura do Windows) e a
RAM usada (que hoje sai com `:0.0`):

```csharp
        if (show.Contains("cpu_clock")) cpu.Add(avg is float ca ? SensorTexto.Gigahertz(ca / 1000) : SensorTexto.Ausente);
        if (show.Contains("cpu_clockmax")) cpu.Add(max is float cm ? $"pico {SensorTexto.Gigahertz(cm / 1000)}" : "pico —");
        if (show.Contains("ram_used")) ram.Add(total > 0 ? SensorTexto.Gigabytes((total - availB) / 1073741824.0) : SensorTexto.Ausente);
```

> **Atenção:** o `SensorTexto.Mhz` devolve "2.790 MHz" (com ponto de milhar, padrão da casa) e não "2790 MHz". Isso é a regra do app: o mesmo número sai do mesmo jeito em todo lugar.

- [ ] **Step 5: Faça o `MemoriaTexto` delegar ao `SensorTexto`**

O `MemoriaTexto.Bytes` (`MemoriaTexto.cs:26`) e o `MemoriaTexto.Voltagem`
(`MemoriaTexto.cs:23`) montam o texto por conta própria. Agora os dois
delegam (arbitragem 3 e arbitragem 5 — um jeito só de escrever GB e de
escrever volt no app):

```csharp
    /// <summary>Uma tensão em volts.</summary>
    public static string Voltagem(double volts) => SensorTexto.Volts(volts, 2);

    /// <summary>Uma quantidade de bytes em gigabytes.</summary>
    public static string Bytes(long bytes) =>
        SensorTexto.Gigabytes(Math.Round(bytes / 1024.0 / 1024.0 / 1024.0, 1));
```

> **Atenção:** o texto muda de "8 GB" para "8,0 GB" na tela de memória. É a
> consequência da arbitragem 3, e é o mesmo número que o painel e o overlay
> mostram. A tensão do módulo continua com duas casas ("1,35 V") — o
> `SensorTexto.Volts` é o mesmo formatador, com a casa que a grandeza pede.

- [ ] **Step 6: Rode a suíte inteira**

```
dotnet test "E:\projetos\tranal-sentinel\TranalSentinel.Tests\TranalSentinel.Tests.csproj" -c Release
```
Esperado: todos PASS (o número de testes sobe em 1 — o teste novo do `SensorTexto`).

- [ ] **Step 7: Build e contagem de avisos**

```
dotnet build "E:\projetos\tranal-sentinel\TranalSentinel.csproj" -c Release -t:Rebuild
```
Esperado: **16 avisos, 0 erros**. Qualquer aviso novo = regressão: pare e investigue.

- [ ] **Step 8: Commit**

Antes do commit, grave a mensagem num arquivo UTF-8 **sem BOM** em <workspace>/msg-v031-t3.txt:

```
Os sensores do app passam a sair do mesmo formatador

O overlay e a aba de ventoinhas formattavam os numeros por conta propria.
Agora eles consomem o SensorTexto, e o numero continua saindo do NumeroTexto:
o mesmo numero nao pode sair de dois jeitos no app.

O texto do cartao de ventoinha nao muda. O do overlay muda, e muda para o
padrao da casa: a temperatura ganha a casa decimal (51,4 °C), o clock e o
fan da GPU ganham o ponto de milhar (2.790 MHz, 1.407 RPM) e o clock da CPU
sai em gigahertz (4,45 GHz), que antes dependia da cultura do Windows.

A memoria passa a escrever gigabyte do mesmo jeito que o resto do app
(8,0 GB, e nao 8 GB).
```

```bash
git add MainWindow.Overlay.cs VentoinhaRegras.cs MemoriaTexto.cs TranalSentinel.Tests/VentoinhaRegrasTestes.cs
git commit -F "<workspace>/msg-v031-t3.txt"
```

---

### Task 4: `PainelWindow` — a janela flutuante

**Files:**
- Create: `PainelWindow.xaml`
- Create: `PainelWindow.xaml.cs`

**Interfaces:**
- Consumes: `PainelLinha` (Task 2).
- Produces: `public partial class PainelWindow : Window` com
  - `void Update(IReadOnlyList<PainelLinha> linhas)`
  - `event Action? AoAbrirJanela` (duplo clique)
  - `event Action? AoFechar` (botão "×")
  - `void Topmost` herdado de `Window`
  - `static bool DentroDaTela(double left, double top, double largura, double altura)` — decide se o painel está em algum monitor (a posição é salva em DIPs; a tela virtual também é lida em DIPs, via `SystemParameters`)

- [ ] **Step 1: Escreva o XAML**

Crie `PainelWindow.xaml` (LF):

```xml
<Window x:Class="TranalSentinel.PainelWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="TRANAL SENTINEL painel" WindowStyle="None" AllowsTransparency="True" Background="Transparent"
        Topmost="True" ShowInTaskbar="False" ResizeMode="NoResize" SizeToContent="WidthAndHeight"
        WindowStartupLocation="Manual" UseLayoutRounding="True" TextOptions.TextFormattingMode="Display">
    <Border x:Name="Box" Background="#D90A0A0A" BorderBrush="#33FFFFFF" BorderThickness="1" CornerRadius="6" Padding="12,9">
        <StackPanel TextElement.FontFamily="Cascadia Mono, Consolas" TextElement.FontSize="12" TextElement.Foreground="#F5F5F5">
            <Grid>
                <TextBlock Text="TRANAL SENTINEL" Foreground="#5A5A5A" FontSize="10" VerticalAlignment="Center"/>
                <Button x:Name="BtnFechar" Content="&#xE711;" FontFamily="{StaticResource Icons}" FontSize="10"
                        Foreground="#8C8C8C" Background="Transparent" BorderThickness="0" Padding="4,0,0,0"
                        HorizontalAlignment="Right" VerticalAlignment="Top" Cursor="Hand" Click="BtnFechar_Click"/>
            </Grid>
            <!-- As quatro linhas: o texto vem pronto do nucleo puro. -->
            <ItemsControl x:Name="PLinhas" Margin="0,6,0,0">
                <ItemsControl.ItemTemplate>
                    <DataTemplate>
                        <Grid>
                            <Grid.ColumnDefinitions>
                                <ColumnDefinition Width="42"/>
                                <ColumnDefinition Width="*"/>
                            </Grid.ColumnDefinitions>
                            <TextBlock Text="{Binding Rotulo}" Foreground="#8C8C8C"/>
                            <TextBlock Grid.Column="1" Text="{Binding Valor}"/>
                        </Grid>
                    </DataTemplate>
                </ItemsControl.ItemTemplate>
            </ItemsControl>
        </StackPanel>
    </Border>
</Window>
```

- [ ] **Step 2: Escreva o code-behind**

Crie `PainelWindow.xaml.cs` (LF):

```csharp
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace TranalSentinel;

/// <summary>
/// O painel flutuante: uma janela pequena, arrastável, com os números do PC
/// sempre à vista.
///
/// <para>
/// Ela é a segunda janela do app (como o <see cref="OverlayWindow"/>), e não
/// um app à parte: nasce com o TRANAL SENTINEL e morre com ele.
///
/// <para>
/// O botão "×" esconde o painel — o app continua rodando na bandeja, e só o
/// "Sair" da bandeja encerra tudo.
/// </para>
/// </summary>
public partial class PainelWindow : Window
{
    private bool _arrastando;
    private Point _origemEmPixels;

    public PainelWindow()
    {
        InitializeComponent();

        // O corpo do painel é a alça de arrastar.
        MouseLeftButtonDown += (_, e) =>
        {
            if (e.ButtonState != MouseButtonState.Pressed) return;
            _arrastando = true;
            _origemEmPixels = PointToScreen(e.GetPosition(this));
            try { CaptureMouse(); }
            catch
            {
                // A captura do mouse pode falhar se a janela perdeu o foco
                // entre o clique e a captura: o arrasto simplesmente não
                // começa, e o painel continua onde está.
            }
        };

        MouseMove += (_, e) => Arrastando(e);
        MouseLeftButtonUp += (_, _) => FimDoArrasto();
        MouseDoubleClick += (_, _) => AoAbrirJanela?.Invoke();
    }

    /// <summary>Disparado quando o dono pede para abrir a janela principal (duplo clique).</summary>
    public event Action? AoAbrirJanela;

    /// <summary>Disparado quando o dono fecha o painel (ele se esconde; o app continua).</summary>
    public event Action? AoFechar;

    /// <summary>Mostra as linhas (o texto já vem pronto do núcleo puro).</summary>
    public void Update(IReadOnlyList<PainelLinha> linhas)
    {
        PLinhas.ItemsSource = linhas;
    }

    /// <summary>
    /// A posição do painel está em algum monitor?
    ///
    /// <para>
    /// A posição é guardada em DIPs (a unidade do WPF) e a tela virtual é lida
    /// na mesma unidade. Se o dono trocou de monitor, o painel volta para o
    /// canto em vez de ficar fora da tela.
    /// </para>
    /// </summary>
    public static bool DentroDaTela(double left, double top, double largura, double altura)
    {
        double x = SystemParameters.VirtualScreenLeft;
        double y = SystemParameters.VirtualScreenTop;
        double w = SystemParameters.VirtualScreenWidth;
        double h = SystemParameters.VirtualScreenHeight;

        return left + largura > x && left < x + w && top + altura > y && top < y + h;
    }

    private void Arrastando(MouseEventArgs e)
    {
        if (!_arrastando || e.LeftButton != MouseButtonState.Pressed) return;

        var tela = PointToScreen(e.GetPosition(this));
        var dpi = VisualTreeHelper.GetDpi(this);

        // PointToScreen devolve pixels do dispositivo; Left/Top são DIPs.
        // Sem a conversão, o painel andaria torto em tela com escala.
        Left = (tela.X - _origemEmPixels.X) / dpi.DpiScaleX;
        Top = (tela.Y - _origemEmPixels.Y) / dpi.DpiScaleY;
    }

    private void FimDoArrasto()
    {
        _arrastando = false;
        try { ReleaseMouseCapture(); }
        catch
        {
            // Se o mouse não estava capturado, não há o que liberar.
        }
    }

    private void BtnFechar_Click(object sender, RoutedEventArgs e) => AoFechar?.Invoke();
}
```

- [ ] **Step 3: Compile**

```
dotnet build "E:\projetos\tranal-sentinel\TranalSentinel.csproj" -c Release
```
Esperado: 0 erros. (O `{StaticResource Icons}` vem do `App.xaml` — se o build reclamar, use `FontFamily="Segoe MDL2 Assets"` no botão, que é o mesmo nome da fonte.)

- [ ] **Step 4: Commit**

Antes do commit, grave a mensagem num arquivo UTF-8 **sem BOM** em <workspace>/msg-v031-t4.txt:

```
A janela do painel flutuante

A segunda janela do app (como o overlay): pequena, transparente, arrastavel,
com as quatro linhas e um botao que so esconde o painel. A posicao e
guardada em DIPs, e a conversao de pixels para DIPs e feita com a DPI da
janela - sem isso o painel andaria torto em tela com escala.
```

```bash
git add PainelWindow.xaml PainelWindow.xaml.cs
git commit -F "<workspace>/msg-v031-t4.txt"
```

---

### Task 5: Ligar o painel no app (settings, integração e página)

**Files:**
- Modify: `AppSettings.cs` (quatro propriedades novas)
- Create: `MainWindow.Painel.cs`
- Modify: `MainWindow.xaml` (o item do menu e a página "Painel")
- Modify: `MainWindow.xaml.cs` (os campos `_painel`/`_painelTimer`, o `ShowPage` e o `Closed`)

**Interfaces:**
- Consumes: `PainelWindow` (Task 4), `PainelRegras` (Task 2), `BottleneckAnalyzer.Sample` (já existe), `VentoinhaRegras.NomeLegivel` (já existe).
- Produces: `AppSettings.PainelVisivel`, `AppSettings.PainelSempreNoTopo`, `AppSettings.PainelX`, `AppSettings.PainelY`.

- [ ] **Step 1: As settings**

Em `AppSettings.cs`, depois do bloco do Overlay, acrescente:

```csharp
    // Painel flutuante: os números do PC sempre à vista.
    public bool PainelVisivel { get; set; }
    public bool PainelSempreNoTopo { get; set; } = true;
    /// <summary>A posição do painel em DIPs (null = ainda nunca foi movido).</summary>
    public double? PainelX { get; set; }
    /// <summary>A posição do painel em DIPs (null = ainda nunca foi movido).</summary>
    public double? PainelY { get; set; }
```

- [ ] **Step 2: A integração**

Crie `MainWindow.Painel.cs` (LF):

```csharp
using System.Windows;
using System.Windows.Threading;

namespace TranalSentinel;

public partial class MainWindow
{
    /// <summary>O painel flutuante (null enquanto ele não foi ligado).</summary>
    private PainelWindow? _painel;

    /// <summary>O relógio que atualiza o painel.</summary>
    private DispatcherTimer? _painelTimer;

    /// <summary>
    /// Liga ou desliga o painel flutuante (o interruptor da página "Painel").
    /// </summary>
    private void ChkPainel_Changed(object sender, RoutedEventArgs e)
    {
        if (ChkPainel.IsChecked == true)
        {
            _painel ??= new PainelWindow();
            _painel.AoFechar += () => ChkPainel.IsChecked = false;
            _painel.AoAbrirJanela += () => Dispatcher.Invoke(MostrarJanela);
            _painel.Topmost = ChkPainelTopo.IsChecked == true;

            RestaurarPosicaoDoPainel();
            _painel.Show();

            _painelTimer ??= new DispatcherTimer(
                TimeSpan.FromMilliseconds(500), DispatcherPriority.Normal, (_, _) => UpdatePainel(), Dispatcher);
            _painelTimer.Start();

            UpdatePainel();
        }
        else
        {
            _painelTimer?.Stop();
            SalvarPosicaoDoPainel();
            _painel?.Hide();
        }

        _settings.PainelVisivel = ChkPainel.IsChecked == true;
        _settings.Save();
    }

    /// <summary>Liga ou desliga o "sempre no topo" do painel.</summary>
    private void ChkPainelOpcao_Changed(object sender, RoutedEventArgs e)
    {
        if (_painel is not null) _painel.Topmost = ChkPainelTopo.IsChecked == true;
        _settings.PainelSempreNoTopo = ChkPainelTopo.IsChecked == true;
        _settings.Save();
    }

    /// <summary>Devolve o painel para o canto superior direito do monitor principal.</summary>
    private void PainelCanto_Click(object sender, RoutedEventArgs e)
    {
        if (_painel is null) return;

        var area = SystemParameters.WorkArea;
        _painel.Left = area.Right - _painel.Width - 12;
        _painel.Top = area.Top + 12;

        SalvarPosicaoDoPainel();
    }

    /// <summary>Atualiza as linhas do painel (a cada meio segundo).</summary>
    private void UpdatePainel()
    {
        if (_painel is null || !_painel.IsVisible) return;

        try
        {
            // A mesma leitura que o overlay usa: um número só no app.
            var s = BottleneckAnalyzer.Sample(_monitor, _gpus.FirstOrDefault(), null);

            var ventoinhas = new List<PainelVentoinha>();
            try
            {
                ventoinhas = _ventoinhas.Cartoes()
                    .Select(c => new PainelVentoinha(VentoinhaRegras.NomeLegivel(c.Nome), c.RpmAtual))
                    .ToList();
            }
            catch
            {
                // A leitura das ventoinhas é um extra: sem ela, o painel
                // mostra os outros blocos.
            }

            var retrato = new PainelRetrato(
                s.CpuTemp, s.CpuTotal, s.GpuTemp, s.GpuLoad, s.RamPct, ventoinhas);

            _painel.Update(PainelRegras.Linhas(retrato));
        }
        catch (Exception ex)
        {
            // Um painel que falha não pode derrubar o app: ele simplesmente some.
            _painelTimer?.Stop();
            _painel?.Hide();
            RegistroDiario.Registrar("Falha ao atualizar o painel flutuante: " + ex.Message, "PAINEL");
        }
    }

    /// <summary>Guarda a posição do painel (em DIPs).</summary>
    private void SalvarPosicaoDoPainel()
    {
        if (_painel is null || !_painel.IsVisible) return;

        _settings.PainelX = _painel.Left;
        _settings.PainelY = _painel.Top;
    }

    /// <summary>
    /// Devolve o painel para a posição guardada — ou para o canto, se a
    /// posição guardada não estiver em nenhum monitor (o dono trocou de
    /// monitor).
    /// </summary>
    private void RestaurarPosicaoDoPainel()
    {
        if (_painel is null) return;

        if (_settings.PainelX is double x && _settings.PainelY is double y
            && PainelWindow.DentroDaTela(x, y, 260, 120))
        {
            _painel.Left = x;
            _painel.Top = y;
            return;
        }

        var area = SystemParameters.WorkArea;
        _painel.Left = area.Right - 260 - 12;
        _painel.Top = area.Top + 12;
    }
}
```

- [ ] **Step 3: O item do menu**

Em `MainWindow.xaml`, no grupo TRANAL ASSISTENTE (logo depois do `NavOverlay`), acrescente:

```xml
                            <RadioButton x:Name="NavPainel" Style="{StaticResource NavItem}" Tag="PagePainel" Checked="Nav_Checked">
                                <StackPanel Orientation="Horizontal">
                                    <TextBlock Text="&#xE80F;" FontFamily="{StaticResource Icons}" FontSize="14" Width="28" VerticalAlignment="Center"/>
                                    <TextBlock Text="Painel" VerticalAlignment="Center"/>
                                </StackPanel>
                            </RadioButton>
```

- [ ] **Step 4: A página**

Em `MainWindow.xaml`, depois do `PageOverlay` (que termina na linha 2388), acrescente:

```xml
                <!-- ===== PAINEL ===== -->
                <ScrollViewer x:Name="PagePainel" Visibility="Collapsed" VerticalScrollBarVisibility="Auto">
                    <StackPanel Margin="36,30,36,30">
                        <StackPanel Margin="0,0,0,20">
                            <TextBlock Text="PAINEL" Style="{StaticResource H1}"/>
                            <TextBlock Text="Os números do PC sempre à vista, numa janela que você arrasta" Style="{StaticResource Sub}"/>
                        </StackPanel>

                        <Border Style="{StaticResource CardBorder}" Margin="0,0,0,14">
                            <StackPanel>
                                <CheckBox x:Name="ChkPainel" Content="Mostrar o painel flutuante" FontSize="15" Checked="ChkPainel_Changed" Unchecked="ChkPainel_Changed"/>
                                <TextBlock Foreground="{DynamicResource Muted}" Margin="46,6,0,0" TextWrapping="Wrap">
                                    Uma janela pequena com CPU, GPU, RAM e ventoinha. Você arrasta para onde quiser; o botão × só esconde o painel, e o TRANAL SENTINEL continua rodando na bandeja.
                                </TextBlock>
                            </StackPanel>
                        </Border>

                        <Border Style="{StaticResource CardBorder}" Margin="0,0,0,14">
                            <StackPanel>
                                <CheckBox x:Name="ChkPainelTopo" Content="Sempre no topo" FontSize="15" Checked="ChkPainelOpcao_Changed" Unchecked="ChkPainelOpcao_Changed"/>
                                <TextBlock Foreground="{DynamicResource Muted}" Margin="46,6,0,0" TextWrapping="Wrap">
                                    Desligue quando o painel atrapalhar o jogo. Deixado ligado, o painel fica acima das outras janelas.
                                </TextBlock>
                            </StackPanel>
                        </Border>

                        <Border Style="{StaticResource CardBorder}">
                            <StackPanel>
                                <TextBlock Text="POSIÇÃO" Style="{StaticResource Label}"/>
                                <Button Style="{StaticResource OutlineButton}" Content="VOLTAR PARA O CANTO" Padding="10,4" Click="PainelCanto_Click"/>
                            </StackPanel>
                        </Border>
                    </StackPanel>
                </ScrollViewer>
```

- [ ] **Step 5: Registrar a página no `ShowPage` e os campos**

Em `MainWindow.xaml.cs`:
- na lista do `ShowPage` (linha 394), acrescente `PagePainel` à lista de `FrameworkElement`;
- no switch do `Nav_Checked` (linha 366), acrescente `case "PagePainel": InitPainel(); break;`.

> Os campos `_painel` e `_painelTimer` já são declarados no `MainWindow.Painel.cs` (Step 2 desta task). **Não** os redeclare no `MainWindow.xaml.cs` — a classe é a mesma e o compilador acusa o duplicado.

E em `MainWindow.Painel.cs`, acrescente o método:

```csharp
    /// <summary>Prepara a página (chamada quando o menu "Painel" é aberto).</summary>
    private void InitPainel()
    {
        // O interruptor reflete o que está ligado agora.
        ChkPainel.IsChecked = _painel is not null && _painel.IsVisible;
        ChkPainelTopo.IsChecked = _settings.PainelSempreNoTopo;
    }
```

- [ ] **Step 6: Abrir o painel no arranque (se o dono o deixou ligado)**

Em `MainWindow.xaml.cs`, no construtor, depois de `IniciarVigia();` (linha 196), acrescente:

```csharp
        // O painel flutuante: o dono liga e desliga pela página "Painel".
        // Se ele o deixou ligado da outra vez, ele volta.
        if (_settings.PainelVisivel) ChkPainel.IsChecked = true;
```

> **Cuidado:** `ChkPainel.IsChecked = true` dispara `ChkPainel_Changed`, que cria a janela e grava `_settings.PainelVisivel = true`. Está certo — mas só faça isso **depois** que a página existir (o `InitializeComponent` já rodou no construtor). O lugar acima é depois do `InitializeComponent`, então está ok.

- [ ] **Step 7: Grave a posição no fechamento**

Em `MainWindow.xaml.cs`, no bloco `Closed` (linhas 155-190), antes de `_bandeja?.Dispose();`, acrescente:

```csharp
            // A posição do painel fica salva: ela só se perde se o dono
            // arrastar o painel para outro lugar.
            SalvarPosicaoDoPainel();
```

- [ ] **Step 8: Compile e rode a suíte**

```
dotnet build "E:\projetos\tranal-sentinel\TranalSentinel.csproj" -c Release -t:Rebuild
dotnet test "E:\projetos\tranal-sentinel\TranalSentinel.Tests\TranalSentinel.Tests.csproj" -c Release
```
Esperado: 16 avisos, 0 erros; suíte verde.

- [ ] **Step 9: Prova na tela**

1. Feche o app (se estiver rodando): `workspace\sair.ps1` (elevado).
2. Publique: `dotnet publish ... -o "E:\projetos\tranal-sentinel\release-prova-v031"`.
3. Suba o app (elevado) e abra a página "Painel" (grupo TRANAL ASSISTENTE).
4. Ligue "Mostrar o painel flutuante" e capture o painel.

   O painel **não é uma aba**: é uma janela separada, com o título
   "TRANAL SENTINEL painel". A captura precisa achá-la pelo título (o
   `prova-ventoinhas.ps1` serve de modelo, mas ele captura a janela
   principal). Crie `workspace\prova-painel.ps1` com este conteúdo:

   ```powershell
   param([string]$Arquivo = 'painel.png')
   $ErrorActionPreference = 'Continue'
   Add-Type -AssemblyName System.Drawing
   Add-Type -AssemblyName UIAutomationClient

   $w = 'C:\Users\Matheus\.minimax\sessions\mvs_033f5363b29e40f8a95383634bf92f4b\workspace'
   $png = Join-Path $w $Arquivo

   Add-Type @"
   using System;
   using System.Runtime.InteropServices;
   public class JanelaPainel {
     [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hwnd, IntPtr hdcBlt, uint nFlags);
     [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out RECT lpRect);
     [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
   }
   "@

   # A janela do painel e a que tem o titulo proprio.
   $proc = Get-Process TranalSentinel -ErrorAction SilentlyContinue |
           Where-Object { $_.MainWindowTitle -like '*painel*' } | Select-Object -First 1
   if (-not $proc) { Write-Output 'ERRO: a janela do painel nao foi encontrada'; exit 1 }

   $h = $proc.MainWindowHandle
   $r = New-Object JanelaPainel+RECT
   [JanelaPainel]::GetWindowRect($h, [ref]$r) | Out-Null
   $larg = $r.Right - $r.Left
   $alt = $r.Bottom - $r.Top
   if ($larg -le 0 -or $alt -le 0) { Write-Output 'ERRO: a janela do painel nao tem tamanho'; exit 1 }

   $bmp = New-Object System.Drawing.Bitmap($larg, $alt)
   $g = [System.Drawing.Graphics]::FromImage($bmp)
   $hdc = $g.GetHdc()
   [JanelaPainel]::PrintWindow($h, $hdc, 2) | Out-Null
   $g.ReleaseHdc($hdc)
   $g.Dispose()
   $bmp.Save($png, [System.Drawing.Imaging.ImageFormat]::Png)
   $bmp.Dispose()
   Write-Output ('captura: ' + $png)
   ```

   Rode elevado (a janela do app é elevada):
   `Start-Process cmd.exe -Verb RunAs -Wait -ArgumentList '/c',"powershell -ExecutionPolicy Bypass -File `"<workspace>\prova-painel.ps1`" -Arquivo `"painel.png`""`
5. Arraste o painel, feche o app, abra de novo: o painel tem que voltar no mesmo lugar.

- [ ] **Step 10: Commit**

Antes do commit, grave a mensagem num arquivo UTF-8 **sem BOM** em <workspace>/msg-v031-t5.txt:

```
O painel entra no app: settings, integracao e pagina

A pagina "Painel" (no grupo TRANAL ASSISTENTE) liga e desliga o painel,
escolhe o "sempre no topo" e devolve o painel para o canto. A posicao
guardada e conferida contra a tela virtual: se o dono trocou de monitor, o
painel volta para o canto em vez de ficar fora da tela.

O painel le a mesma leitura que o overlay usa (BottleneckAnalyzer.Sample),
entao nao ha dois numeros para a mesma coisa no app. Ele nao grava nada no
banco.
```

```bash
git add AppSettings.cs MainWindow.Painel.cs MainWindow.xaml MainWindow.xaml.cs
git commit -F "<workspace>/msg-v031-t5.txt"
```

---

### Task 6: Documentação e versão

**Files:**
- Modify: `Directory.Build.props` (a versão → 0.31.0)
- Modify: `AGENTS.md` (a seção da v0.31)
- Modify: `NOVIDADES.md` (a entrada da v0.31)
- Modify: `MERCADO.md` (a linha dos widgets, com o que a pesquisa mediu)
- Modify: `README.md` (a aba "Painel" na lista)
- Modify: `LEIA-ME.txt` (a aba "Painel" na lista)

**Interfaces:**
- Consumes: tudo das tasks 1-5.

- [ ] **Step 1: A versão**

Em `Directory.Build.props`, troque a versão para `0.31.0`.

- [ ] **Step 2: `AGENTS.md`**

Acrescente uma seção depois da da v0.30, com:
- o que o painel é e onde ele mora (`MainWindow.Painel.cs`, `PainelWindow`, `PainelRegras`);
- a regra do núcleo puro (`PainelRegras` e `SensorTexto` não conhecem WPF);
- o contrato do "—" (valor ausente nunca é zero nem valor velho) e o da ventoinha (a de maior RPM entre as que leem);
- o `SensorTexto` como o **único** lugar das unidades dos sensores, e o `NumeroTexto` como o único lugar do número;
- a linha em "onde as coisas estão" com os três arquivos novos.

- [ ] **Step 3: `NOVIDADES.md`**

Acrescente a entrada da v0.31 no topo (o arquivo está em ordem decrescente de versão), com o que o dono vê: o painel flutuante, o que ele mostra, que ele é arrastável, que o "×" esconde, e as correções da refatoração do overlay.

- [ ] **Step 4: `MERCADO.md`**

Na linha do backlog de widgets, escreva o que foi **medido** na pesquisa de 09/11/2026:
- o Kinetik Stats ganhou o widget de desktop na v1.4.0 (barras e gráficos por métrica, ligado/desligado, no desktop ou sempre no topo);
- o RigSight tem 7 widgets no desktop + as leituras ao lado do relógio na barra de tarefas;
- e o que ficou **fora** do nosso escopo: leitura dentro da barra de tarefas (injeção no `explorer.exe`) e o widget oficial do Windows 11 (Adaptive Cards/MSIX) — ambos anotados como v0.32+.

- [ ] **Step 5: `README.md` e `LEIA-ME.txt`**

Acrescente "Painel" à lista de abas.

- [ ] **Step 6: Suíte e build**

```
dotnet build "E:\projetos\tranal-sentinel\TranalSentinel.csproj" -c Release -t:Rebuild
dotnet test "E:\projetos\tranal-sentinel\TranalSentinel.Tests\TranalSentinel.Tests.csproj" -c Release
dotnet test "E:\projetos\tranal-sentinel\Tranal.Setup.Tests\Tranal.Setup.Tests.csproj" -c Release
```
Esperado: 16 avisos / 0 erros; suíte do app e a do instalador verdes.

- [ ] **Step 7: Commit**

Antes do commit, grave a mensagem num arquivo UTF-8 **sem BOM** em <workspace>/msg-v031-t6.txt:

```
O GUIA, AS NOVIDADES E O MERCADO: o painel flutuante entra na v0.31

A documentacao da v0.31: o que o painel e, onde as regras moram (nucleo
puro), o contrato do traco (valor ausente nunca e zero nem valor velho) e o
SensorTexto como o unico lugar das unidades dos sensores. O MERCADO.md
registra o que foi medido na pesquisa dos concorrentes (o widget de desktop
do Kinetik e os 7 widgets do RigSight) e o que ficou para a v0.32 (leitura
dentro da barra do Windows e o widget oficial do Windows 11).
```

```bash
git add Directory.Build.props AGENTS.md NOVIDADES.md MERCADO.md README.md LEIA-ME.txt
git commit -F "<workspace>/msg-v031-t6.txt"
```

---

### Task 7: Prova real, revisões adversariais e publicação

**Files:**
- Create: `workspace\prova-painel.ps1` (o script de captura, fora do repo)

**Interfaces:**
- Consumes: tudo.

- [ ] **Step 1: A prova real**

1. Feche o app (elevado): `workspace\sair.ps1`.
2. Publique em `release-prova-v031` e suba o app.
3. Ligue o painel, arraste-o para um canto, e capture:
   - o painel flutuante (a janela "TRANAL SENTINEL painel"), com o script
     `workspace\prova-painel.ps1` criado na Task 5, Step 9;
   - a página "Painel" da janela principal (com o `prova-ventoinhas.ps1`,
     no grupo TRANAL ASSISTENTE).
4. Feche o app (o "Sair" da bandeja) e confirme que o painel some junto.
5. Confira o banco: `--banco` continua igual (o painel **não** grava nada — ele só lê).

- [ ] **Step 2: As revisões adversariais**

Três rodadas, com o revisor lendo o HEAD **atual** (a casa já sabe que as rodadas acham defeitos em camadas diferentes — não publique antes das três):

- **Rodada 1** — o núcleo puro (`SensorTexto`, `PainelRegras`) e os testes: o texto está certo em qualquer cultura? o "—" está em todo lugar? a escolha da ventoinha é determinística?
- **Rodada 2** — a janela e a integração: o arrasto funciona com escala de DPI? a posição sobrevive ao fechamento? o painel some quando o app sai? o "×" esconde sem encerrar?
- **Rodada 3** — a integração no app, o ciclo de vida, a documentação × o código e a qualidade dos testes.

Cada rodada: um revisor (`verifier`) com o relatório em `revisao-v031-N.md`, lido, corrigido e commitado antes da próxima.

- [ ] **Step 3: A publicação**

```
git push origin main
git tag -a v0.31.0 -F "<workspace>/msg-tag-v031.txt"
git push origin v0.31.0
```

O CI (`.github/workflows/publicar.yml`) sobe a release "latest" no push da `main` e a `v0.31.0` na tag, com os 3 assets (`TranalSentinel.exe`, `TranalSentinel.zip`, `TranalSetup.exe`). Confira com `gh release list --repo Matheus-araujo-dev/tranal-sentinel`.

- [ ] **Step 4: O registro no diário do dono**

No fim, responda ao owner com: o que a v0.31 entrega, a prova na tela (a imagem do painel), o número de testes e o link da release.
