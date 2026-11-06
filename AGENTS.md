# TRANAL SENTINEL — guia para quem mexer neste repositório

Assistente de performance do PC (diagnóstico de travamentos e lentidão, sensores em
tempo real, benchmarks, linha do tempo do que mudou no PC). WPF + .NET 10, em português.

Fork não oficial do [RESET EXPLODE](https://github.com/GustavoReset/ResetExplode) (MIT).
A atribuição e o aviso de fork ficam no README e no LICENSE — **não removê-los**.

## Identidade (uma só interpretação)

| Item | Valor |
|---|---|
| Nome exibido | TRANAL SENTINEL |
| Executável / assembly | `TranalSentinel.exe` / `TranalSentinel` |
| Namespace CLR | `TranalSentinel` |
| Projeto do app | `TranalSentinel.csproj` |
| Projetos de teste | `TranalSentinel.Tests` (namespace `TranalSentinel.Tests`), `Tranal.Setup.Tests` |
| Instalador | `Tranal.Setup\Tranal.Setup.csproj` → `TranalSetup.exe` |
| Configurações e histórico | `%AppData%\TranalSentinel` |
| Log do instalador | `%ProgramData%\Tranal Sentinel\instalador.log` |
| Diário do app | `%AppData%\TranalSentinel\log\tranal-AAAA-MM-DD.log` |
| Mutex (instância única) | `Local\TranalSentinel.SingleInstance` |
| Repositório | `https://github.com/Matheus-araujo-dev/tranal-sentinel` |

O título da janela é `TRANAL SENTINEL` e o `FindWindow` de instância única depende
disso: se mudar um, mude o outro.

## Comandos

```powershell
# build e testes
dotnet build "E:\projetos\tranal-sentinel\TranalSentinel.csproj" -c Release -t:Rebuild
dotnet test  "E:\projetos\tranal-sentinel\TranalSentinel.Tests\TranalSentinel.Tests.csproj" -c Release
dotnet test  "E:\projetos\tranal-sentinel\Tranal.Setup.Tests\Tranal.Setup.Tests.csproj" -c Release

# publicação (o .NET vai embutido; não precisa instalar runtime no PC do usuário)
dotnet publish "E:\projetos\tranal-sentinel\TranalSentinel.csproj" -c Release -r win-x64 `
  --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:DebugType=None -o dist
```

Baseline de avisos: **16 no app, 0 no instalador**. Acima disso, é regressão.

## Regras do projeto

- **Português (pt-BR)** em comentários, XML docs, textos de interface e mensagens.
- **Nenhum PackageReference novo** no app. O que é do framework, é do framework.
  (`System.Management` e `LibreHardwareMonitorLib` são as exceções já existentes.)
- **`using System.IO` explícito** no WPF — não é implícito.
- **XML doc `/// <summary>`** em toda classe e método público, explicando o *porquê*.
- **Uma só interpretação**: regra fica em um lugar só e o resto consome. Se a lógica
  precisa aparecer em dois lugares, extraia para um núcleo compartilhado
  (ex.: `Wmi.cs` serve à aba de saúde e à linha do tempo).
- **Só leitura por padrão**: a aba TRANAL SAÚDE não instala, não altera e não
  reinicia nada. Ações que mudam o sistema são exceções explícitas — e cada
  uma delas pede confirmação, fica registrada no diário e tem como desfazer.
- **A regra do diagnóstico só descreve; quem executa é o executor.**
  `SaudeRegras` (pura) devolve `AcaoAplicavel`; `AcaoExecutor` (impuro)
  executa, perguntando ao `IAcaoAlvo`. A regra nunca toca no Windows.
- **Serviço de segurança nunca é tocado** — nem pela lista, nem se o app for
  chamado direto. A lista de proibidos está em `ServicosSuspendiveis` (puro),
  e o executor (`ServicosSuspender`) só obedece; a regra do diagnóstico
  depende do puro, e não do executor.
- **O que o app mexe fica guardado para desfazer** (`AppSettings.TweakBackup`):
  o modo original do serviço, o valor original do ASPM. Sem isso, "restaurar"
  não tem de onde voltar.
- **O app nunca age por conta própria**: suspender serviço é sempre escolha do
  usuário; o app só marca os que ele reconhece.
- **Nada de `catch` vazio** sem comentário dizendo por que é seguro.

## Arquitetura que não se deve quebrar

- **A coleta de sensores roda em processo separado** (`SensorWorker` / `SensorMonitor`).
  O LibreHardwareMonitor chama código nativo do driver da GPU e já matou o app
  duas vezes (access violation dentro de `AmdGpu.Update()`). Um access violation
  não pode ser pego em C# — por isso o isolamento em processo é obrigatório,
  não opcional. `SensorGuard` desliga a GPU depois de 1 crash nativo e tudo
  depois de 3.
- **A troca do executável** (`Atualizador` + `AutoUpdate`) só acontece com o app
  fechado: o Windows não deixa sobrescrever um `.exe` em uso.
- **A inicialização automática é tarefa agendada**, não chave `Run`: o app exige
  administrador, e `requireAdministrator` na `Run` pediria UAC a cada login.
- **A pasta de instalação é escolha do usuário.** A regra que decide onde o app
  fica mora só em `DestinoInstalacao.cs` (construtor privado: todo destino já
  passou pela validação). A desinstalação descobre o local pelo `InstallLocation`
  da entrada de desinstalação no registro — nunca assumindo a pasta padrão.
  Trocar de pasta é uma **movimentação**: os arquivos do lugar antigo saem
  depois que a nova instalação está pronta, e a tarefa agendada e o atalho da
  área de trabalho não ficam órfãos apontando para o executável velho.
- **O valor de um sensor só existe se for finito.** A regra mora em `SensorValor.Finito`
  (não-finite vira `null`) e é aplicada na origem, na coleta — o JSON padrão não aceita
  infinito, e uma leitura com infinito estourava a gravação, o processo de coleta
  engolia o erro, o arquivo nunca era escrito e o app acabava acusando o PawnIO sem
  motivo. `SensorValor.OpcoesJson` é o par de leitura e escrita; use-o nos dois lados.
- **Falha da coleta inteira é diferente de hardware que falhou.** `SensorSnapshot.ErroGeral`
  é o motivo que derrubou a coleta (vai para o aviso do menu); `SensorSnapshot.Falhas` é
  um hardware que não abriu (as outras leituras continuam válidas); `SensorSnapshot.FalhaCpu`
  é o motivo da CPU em si (é o que interessa quando a temperatura não vem). O texto do
  aviso mora em `SensorMonitor.ResumoCpuSensor` / `TextoAvisoCpuSensor` / `TextoExplicacaoCpuSensor`
  — o menu, o painel e a janela de correção consomem de lá, não cada um com o seu `switch`.
- **O motivo de um hardware que falhou é guardado, não jogado fora.** O hardware
  desligado não volta; sem o motivo guardado, o app perderia a explicação na volta
  seguinte e voltaria a culpar o driver à toa. A regra da volta mora em
  `SensorColeta.MontarVolta`, e ela só conversa com `HardwareColeta` — o
  LibreHardwareMonitor entra pelo adaptador `HardwareLibre`.
- **`AppSettings.Save()` nunca lança.** Gravar configuração é uma conveniência;
  derrubar o app, não. E o `settings.json` aceita literal nomeado (`NaN`, `Infinity`):
  uma nota de benchmark estranha não pode estragar o arquivo inteiro. A gravação
  é **atômica** (arquivo temporário + troca) e serializada por uma trava: a tela e
  a vigia gravam de threads diferentes, e duas gravações ao mesmo tempo deixavam o
  arquivo pela metade — o que faria a leitura engolir o erro e devolver o padrão,
  apagando as preferências do usuário.
- **A regra de alerta é uma só** (`RegraAlerta` + `SensorCatalogo` + `MonitorAlertas`).
  A tela, a vigia e o TRANAL SAÚDE consomem os mesmos; não existe constante de
  limite no meio do código. O motor trata **mudança de configuração como episódio
  novo**: quando o usuário muda o sensor, a operação ou o limite, o aviso antigo
  sai da tela e a contagem recomeça (o aviso com o limite velho mentia).
  `AppSettings.Alertas` é anulável: `null` = nunca configurado (semeia os padrões),
  lista vazia = o usuário apagou tudo (é respeitado).
- **O S.M.A.R.T. é lido, nunca escrito.** A tabela de atributos vem do WMI
  (`root\wmi`) para o SATA e do IOCTL de protocolo do NVMe; o caminho NVMe
  (`IOCTL_STORAGE_QUERY_PROPERTY`, `StorageDeviceProtocolSpecificProperty`) usa o
  layout do **winioctl.h** (10 `DWORD`, 40 bytes — o de user-mode; o de kernel, de
  20 bytes, é diferente e não funciona). A struct do protocolo é um buffer de
  memória: offset errado quebra. Em máquina onde o driver recusa, o disco aparece
  como "sem SMART" com o motivo — **nunca** com número inventado.
- **Número que a spec não garante, o app não mostra.** O log NVMe é decodificado
  só nos campos estáveis (aviso crítico, temperatura composta, reserva,
  percentual de uso e as 8 temperaturas, quando são plausíveis). Os contadores
  grandes ficam em blocos de 128 bits cujo tamanho muda entre revisões da spec:
  número errado é pior que número ausente. O "percentual de uso" é quanto o disco
  foi usado — a vida que resta é o complemento.
- **A comparação de snapshots** (`Mudancas.Comparar`) é função pura e não tem
  I/O — é o que os testes cobrem.
- **Os tempos ativos de memória são lidos, nunca escritos.** O módulo PawnIO do
  AMD (`AMDFamily17.bin`) expõe funções de escrita (`ioctl_write_msr` e
  afins): **nenhuma delas é usada**. A única chamada é `ioctl_read_smn`, que lê
  os registradores do UMC — é de onde saem os tempos que a BIOS programou, que
  não são os mesmos que o SPD oferece. Cada handle do driver PawnIO guarda o
  seu próprio módulo, então o leitor de SMN e o de SMBus convivem.
- **O decodificador dos tempos ativos é função pura** (`TimingsAtivosDecoder`),
  e o que ele recusa é regra, não acidente: tipo de memória que não seja DDR4
  (o mapa de DDR5 ainda não foi conferido), clock que não bate com o que o
  Windows informa como configurado, e tempos incoerentes entre si. Número
  ausente é melhor que número errado — a mesma regra do S.M.A.R.T.
- **A comparação com o perfil é em ciclos, não em nanossegundos.** Quando a
  BIOS sobe o clock, ela mantém a contagem de ciclos do perfil: é mexer
  nessa contagem que caracteriza ajuste manual. Nos tempos primários
  (tCL, tRCD, tRP, tRAS, tRC) qualquer diferença conta; nos secundários a
  diferença de um ciclo é ruído de revisão de BIOS. A regra mora em
  `MemoriaRegras.CompararTimings` — a tela, o `--memoria` e o TRANAL SAÚDE
  consomem o mesmo relatório.
- **O formato de intercâmbio de benchmarks é versionado e tem um dono.**
  `BenchIntercambio` é o único lugar que sabe ler e gravar o
  `.tranalbench.json`; a regra da comparação mora em `BenchComparacao` (função
  pura). A tela e a CLI (`--comparar`) consomem as duas. Um arquivo de uma
  versão mais nova do formato é recusado com a explicação, nunca lido pela
  metade.
- **O nome de cada benchmark tem um lugar só** (`BenchCatalogo`): a tela do
  TRANAL BENCH, o relatório exportável e a comparação entre PCs usam a
  mesma tabela. E o nome inclui o detalhe da chave (multi-core, single-core,
  preset e resolução do ray tracing) para duas linhas nunca saírem com o
  mesmo rótulo.

## Publicar

```powershell
git push origin main                      # CI monta a release "latest"
git tag -a v0.17 -m "..." && git push origin v0.17   # CI monta a release da versão
```

O `.github/workflows/publicar.yml` roda os testes, publica o app e o instalador e
sobe as releases. O asset **precisa** se chamar `TranalSentinel.exe`: é o que o
auto-update procura.

## Onde as coisas estão

| Arquivo | O que faz |
|---|---|
| `MainWindow.*.cs` | Uma partial class por aba. `MainWindow.Assistente.cs` é o modo assistente (bandeja, perfil de recursos, linha do tempo). |
| `Saude*.cs` | O diagnóstico de travamentos e lentidão: modelo, coleta, regras (funções puras), orquestrador. |
| `SaudeCli.cs` | O mesmo diagnóstico pela linha de comando (`--saude`). Reaproveita as mesmas regras. |
| `SmartLeitor.cs` / `SmartDisco.cs` / `SmartNomes.cs` | A leitura do S.M.A.R.T. (WMI para SATA, IOCTL de protocolo para NVMe), o modelo e os nomes padrão dos atributos. |
| `SmartCli.cs` | A tabela do S.M.A.R.T. pela linha de comando (`--smart`). |
| `RegraAlerta.cs` / `SensorCatalogo.cs` | A regra de alerta do usuário e o catálogo dos 8 sensores que podem ser vigiados. |
| `TimingsAtivos.cs` / `TimingsAtivosDecoder.cs` | O modelo dos tempos ativos de memória e o decodificador puro dos registradores do UMC do AMD. |
| `SmnPawnIo.cs` / `MemoriaAtivos.cs` | A leitura do SMN pelo módulo PawnIO do AMD (só leitura) e a coleta dos registradores. |
| `MemoriaRegras.cs` | A regra da memória: modo (JEDEC, perfil, acima do perfil) e a comparação dos tempos ativos com o perfil. |
| `BenchCatalogo.cs` | O nome e a descrição de cada benchmark, em um lugar só (tela, relatório e comparação consomem). |
| `BenchIntercambio.cs` | O formato `.tranalbench.json`: modelo, leitura, gravação e as specs da máquina. |
| `BenchComparacao.cs` | A regra da comparação entre dois PCs (função pura: recebe os resultados e devolve o relatório). |
| `MainWindow.Alertas.cs` / `MainWindow.Smart.cs` | As páginas de configuração de alertas e de S.M.A.R.T. |
| `Mudancas.cs` | A linha do tempo do PC: retrato, comparação pura e coleta do retrato. |
| `PoliticaRecursos.cs` | Perfil Completo / Equilibrado / Economia. Funções puras. |
| `RegistroDiario.cs` | O log diário (um arquivo por dia). |
| `Bandeja.cs` | Ícone na bandeja por P/Invoke direto (sem Windows Forms, que criaria ambiguidade de nomes). |
| `SensorValor.cs` | Regra única do valor de sensor: finito ou nada (`Finito`) e as opções de JSON (`OpcoesJson`). |
| `SensorHistorico.cs` | O log contínuo de sensores (CSV de 5 em 5 segundos): o que o PC estava fazendo antes de travar. |
| `RetrospectivaTravamento.cs` | Correlaciona o log de sensores com os travamentos (função pura): vira a frase da tela. |
| `HardwareColeta.cs` / `HardwareLibre.cs` | O que a coleta precisa de um hardware (interface) e o adaptador do LibreHardwareMonitor. |
| `AutoUpdate.cs` / `Atualizador.cs` | Checagem de versão nova, download e troca do executável. |
| `Tranal.Setup\` | O instalador (admin, atalhos, tarefa agendada, desinstalação). |
| `Tranal.Setup\Dominio\DestinoInstalacao.cs` | Onde o app é instalado. Regra única do destino (pasta escolhida pelo usuário). |
| `MERCADO.md` | Pesquisa de concorrentes e o que foi adotado aqui. |
