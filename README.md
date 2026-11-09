<!-- ================= INICIO DO AVISO DO FORK ================= -->

# 🔎 TRANAL SENTINEL — fork local com a aba **TRANAL SAÚDE**

> ⚠️ **Fork não oficial.** Esta é uma cópia modificada de
> [GustavoReset/ResetExplode](https://github.com/GustavoReset/ResetExplode),
> Copyright (c) 2026 Gustavo (GustavoReset), sob licença [MIT](LICENSE).
> Todo o código original, o nome "RESET EXPLODE" e a identidade do projeto pertencem ao autor original;
> este fork não é afiliado, não é endossado e não substitui o app oficial.
> Download oficial do app original: [github.com/GustavoReset/ResetExplode/releases](https://github.com/GustavoReset/ResetExplode/releases).
>
> O aviso de copyright e a licença MIT foram mantidos, como a licença exige.
> O binário deste repositório é o **TRANAL SENTINEL**, com identidade e código próprios.

## O que este fork acrescenta

**TRANAL SAÚDE** — nova aba no grupo "TRANAL SAÚDE", ao lado de "Visão geral".
Ela responde a pergunta que o TRANAL SENTINEL original não responde: **"por que meu PC trava e o que está deixando ele lento?"**

O app original mede o hardware em tempo real (sensores, SMART, benchmarks). O TRANAL SAÚDE olha
para o **histórico** do Windows e aponta as causas prováveis, cada uma com a ação sugerida:

- **Travamentos**: eventos Kernel-Power 41 (boot depois de desligamento não limpo) nos últimos 90 dias,
  com data, bugcheck e se o botão de energia foi apertado. Sem bugcheck e sem minidump = congelamento
  total, que aponta hardware/firmware em vez de programa.
- **Erros de hardware**: eventos WHEA, telas azuis (minidumps) e resets de driver de vídeo.
- **PCIe / ASPM**: gerenciamento de energia do link PCIe (a causa clássica de NVMe que não volta
  depois de reiniciar) e se o NVMe está atrás de um adaptador M.2→PCIe.
- **Discos**: NVMe fora do ar e dispositivos de armazenamento órfãos (sobras de troca de SSD).
- **Segurança**: estado real do antivírus (o Defender "parado" pode ser normal quando outro produto
  assumiu), filtro de kernel de terceiros, idade das definições e consumo de RAM.
- **Lentidão**: compressão de memória, RAM acima do spec, inicialização automática, serviços de
  terceiros e Hyper-V/VBS.

**A leitura é sempre somente leitura. As ações são a exceção explícita:** cada
achado que tem solução prática ganhou um botão (desligar o ASPM, abrir a
Inicialização, abrir os Drivers, abrir as Otimizações). Toda ação que mexe no
sistema **pede confirmação antes**, explica o custo, fica registrada no diário
e **tem como desfazer** — o app guarda o valor original antes de mudar qualquer
coisa. Serviço de antivírus, Defender e serviço do Windows nunca são tocados.

**Nota de saúde do PC** — uma nota de 0 a 100 (conceito de A a E) calculada a
partir dos achados, com a **tendência** comparando com os dias anteriores.
Histórico em `%AppData%\TranalSentinel\log\tranal-saude-nota.log`.

**Suspender serviços** (janela própria) — o app lista os **serviços de
terceiros** que sobem com o Windows, diz o que cada um é (atualizador,
telemetria, sincronização) e **você escolhe** quais suspender. Os que o app
reconhece vêm marcados; os desconhecidos aparecem, mas desmarcados. Suspender
é mudar o modo para Manual e parar o serviço; "restaurar tudo" devolve cada
um ao modo que tinha.

**Vigia em segundo plano** — o app acompanha o PC mesmo com a janela fechada:
avisa quando um programa **para de responder** por mais de 30 s (a mesma
pergunta que o Windows faz quando mostra o "Não responder") e o menu do ícone
ganha a opção de **encerrar**; avisa também de **temperatura alta** da CPU
(85 °C), do disco (60 °C) e de **bateria acabando** (20 % fora da tomada).

**TRANAL USO** — o app passa a lembrar **qual programa ficou em frente na tela**
e a responder as duas perguntas que sempre sobraram: *"quanto tempo eu passei
neste programa?"* e *"por que esse aviso disparou?"*.

- O histórico mora num **banco SQLite** em
  `%LOCALAPPDATA%\TranalSentinel\historico\historico.db`, e o que é mais velho
  que 30 dias é apagado. O app anota **só o nome do programa e o título da
  janela** — nunca a imagem da tela, o conteúdo dos arquivos ou o que foi
  digitado — e **nada sai do computador**.
- A aba mostra o tempo observado no dia, o ranking de tudo que ficou em frente
  (tempo e percentual), as 5 sessões mais longas e **quem estava em frente
  quando a CPU ficou mais quente**.
- Cada aviso da vigia passa a dizer **por que disparou**: o que os sensores
  faziam na janela antes do disparo e qual programa estava em frente na hora.
  Sem histórico, o app diz que não sabe — número inventado é pior que número
  ausente.

**Relatório em CSV** — o relatório exportado sai também em CSV no padrão do
Excel em português (separador `;`, decimal com vírgula, acentuação correta),
para abrir numa planilha e filtrar.

O mesmo diagnóstico
roda pela linha de comando, sem abrir a janela:

```
TranalSentinel.exe --saude coletar      # grava um snapshot JSON + TXT
TranalSentinel.exe --saude comparar a.json b.json   # o que sumiu, apareceu e continua
TranalSentinel.exe --saude relatar      # só o relatório em texto
```

Os instantâneos ficam em `%AppData%\TranalSentinel\saude\`.

O banco de dados do histórico também se confere pela linha de comando:

```
TranalSentinel.exe --banco                          # estado, tamanho e o que tem dentro
TranalSentinel.exe --banco --reparar --confirmar    # reconstrói o banco estragado (guarda o velho como cópia)
TranalSentinel.exe --banco --pasta <dir>            # olha outra pasta (uma cópia do banco)
```

### Como o código está organizado

| Arquivo | O que faz |
|---|---|
| `SaudeModelos.cs` | Modelo de dados: `Achado`, `TravamentoInfo`, `RelatorioSaude`, `ColetaSaude`. |
| `SaudeRegras.cs` | As regras do diagnóstico. Funções puras: recebem a foto do sistema e devolvem achados. É o único lugar com a lógica de decisão — e é o que os testes cobrem. |
| `SaudeColeta.cs` | Coleta os dados (log de eventos, WMI, registro, processos). Cada bloco é isolado: uma falha aqui não derruba a análise inteira. |
| `SaudeEventos.cs` | Leitura do log de eventos do Windows. |
| `SaudePnp.cs` | Detecção de dispositivos órfãos (SetupAPI + cfgmgr32 + registro). |
| `SaudeDiagnostico.cs` | Orquestrador: coleta → regras → relatório. |
| `MainWindow.Saude.cs` | A aba na interface (partial class, mesmo padrão das outras). |
| `SaudeAcao.cs` | O catálogo de ações: o que dá para fazer, o que cada uma custa e se tem como desfazer. |
| `AcaoExecutor.cs` | Quem executa a ação (a regra só descreve). Guarda o valor original antes de mudar e nunca lança. |
| `ServicosSuspendiveis.cs` | A lista de serviços que dá para suspender: filtra e classifica. **Puro** — não executa nada. |
| `ServicosSuspender.cs` | Suspender/restaurar serviços de terceiros (fala com o Windows pelo `sc.exe`), guardando o modo original para desfazer. |
| `JanelaServicos.xaml` | A janela "SUSPENDER SERVIÇOS": a lista com as caixas de marcação e o botão de restaurar tudo. |
| `SaudeNota.cs` | A nota de saúde (0–100, conceito A–E) e o histórico por dia. |
| `MonitorTravados.cs` | A vigia de programa travado: quanto tempo cada janela está sem responder. |
| `AlertasSegundoPlano.cs` | As regras de alerta de temperatura, disco e bateria (com deduplicação por episódio). |
| `MainWindow.Vigia.cs` | Liga a vigia na interface: os timers, o aviso na tela e o menu da bandeja. |

### Como o TRANAL ASSISTENTE está organizado

| Arquivo | O que faz |
|---|---|
| `Mudancas.cs` | A linha do tempo: o retrato do PC (`InstantaneoPC`), a comparação pura entre dois retratos (`Mudancas.Comparar`) e a coleta do retrato (`ColetaInstantaneo`). |
| `PoliticaRecursos.cs` | As regras do perfil Completo / Equilibrado / Economia: de quanto em quanto tempo ler os sensores, tirar o retrato e o que roda sozinho. Funções puras, sem I/O. |
| `RegistroDiario.cs` | O log diário: um arquivo por dia em `%AppData%\TranalSentinel\log\`, 30 dias. |
| `Bandeja.cs` | O ícone na bandeja (P/Invoke direto do `Shell_NotifyIcon`, sem Windows Forms). |
| `AutoUpdate.cs` | A checagem de versão nova na API do GitHub e o download do executável. Sem interface. |
| `Atualizador.cs` | A troca do executável quando o app fecha (backup, rollback, reabrir). |
| `SaudeCli.cs` | O diagnóstico por linha de comando (`--saude`), reaproveitando as mesmas regras da aba. |
| `Wmi.cs` | As consultas WMI compartilhadas entre a aba de saúde e a linha do tempo. |
| `MainWindow.Assistente.cs` | A aba do assistente: bandeja, perfil de recursos, retrato periódico e diário. |
| `UsoHistorico.cs` | O histórico de uso: a troca de programa gravada no banco, 30 dias. |
| `Banco.cs` | O banco de dados do histórico: esquema, migração do CSV antigo, consulta por período, poda e o reparo (que guarda cópia do banco estragado). |
| `Sqlite.cs` | O SQLite do próprio Windows, chamado por P/Invoke (`winsqlite3.dll`) — sem dependência nova no app. |
| `UsoRegras.cs` | A regra do uso da tela (função pura): quanto tempo cada programa ficou em frente, as sessões mais longas e quem estava em frente quando a CPU ficou mais quente. |
| `UsoTexto.cs` | Os textos da aba "Uso" (duração, percentual, temperatura): função pura, padrão brasileiro. |
| `MainWindow.Uso.cs` | A aba "Uso": tempo de tela, ranking, sessões mais longas e o "quem fez o calor". |

### Sensores: por que a coleta roda num processo separado

O TRANAL SENTINEL mede temperatura, consumo e clocks de tudo. Para isso ele usa o
LibreHardwareMonitor, que chama **código nativo do driver** — no caso da GPU
AMD, a ADL (AMD Display Library).

**Problema comprovado nesta máquina:** em 04/11 o app morreu duas vezes com
`0xc0000005` (access violation) dentro de
`LibreHardwareMonitor.Hardware.Gpu.AmdGpu.Update()` — a biblioteca leu um
ponteiro inválido devolvido pela ADL. Access violation **não dá para pegar em
C#**: o processo inteiro morre. Nenhum `try/catch` resolve isso.

**O que o fork faz, em três camadas:**

1. **Isolamento em processo.** A coleta roda no mesmo executável, chamado com
   `--sensores-worker`, num processo separado. Se a ADL devolver lixo, morre
   **só esse processo** — o app percebe a morte e reinicia o coletor na hora.
   (O TRANAL SENTINEL nunca morre por causa do driver da GPU.)
2. **Disjuntro.** Na primeira morte, o coletor é reiniciado **sem a GPU** — é o
   caminho nativo suspeito. CPU, memória, discos e rede continuam funcionando, e
   a lateral mostra um aviso com botão para religar a GPU à mão.
3. **Guarda por evidência.** No arranque, o app lê o log de eventos do Windows
   (`Application Error`, evento 1000) e procura crashes nativos do próprio app
   (`0xc0000005` e `0x80131623`). Se houver, a GPU fica de fora da coleta
   desde o começo, antes de qualquer crash novo.

Além disso, dentro do processo de coleta cada hardware é atualizado dentro de
um `try/catch`: um placa com problema é desligada e as outras continuam.

**Comandos para conferir (sem abrir a janela):**

```powershell
TranalSentinel.exe --sensores
```

Grava em `%AppData%\TranalSentinel\sensores.txt`: quantos crashes o Windows
registrou, qual nível foi escolhido e se a coleta abre. O mesmo relatório
tenta matar o processo de coleta de propósito e confere que o app o reinicia
sozinho — é a prova de que o isolamento funciona.

> Observação honesta: o driver da GPU instalado em 28/10/2026 é o suspeito
> mais provável, e desligar o HAGS foi uma hipótese testada, não uma causa
> comprovada. Se o problema continuar depois do BIOS atualizado, o próximo
> degrau é trocar a GPU de slot (para o M.2_1, ligado à CPU).

### Testes

O núcleo de diagnóstico tem testes automatizados (xUnit) em `TranalSentinel.Tests/`:

```powershell
dotnet test TranalSentinel.Tests\TranalSentinel.Tests.csproj -c Release
```

Os testes cobrem só as regras (que são puras) — nenhum Windows é tocado por eles.

### Visor Kalkan (SmartMonitor)

Na aba **TRANAL INTEGRAÇÃO** (junto com iCUE, OpenRGB e Chroma) há o cartão
**VISOR KALKAN**, que mostra no visor do cooler Kalkan (linha Alpha Rev.1,
firmware 1.2) a temperatura do CPU, a rotação da ventoinha e a carga da
processor — sem o software da Kalkan instalado e sem a DLL dele.

Como funciona, em três passos:

1. **Handshake** — o app escreve um pedido de 64 bytes (`01 81 E3 00…`) e lê a
   resposta do visor. O byte depois do marcador `0xE3` é a *semente* do embaralhador.
2. **Quadro** — a cada segundo o app monta `01 86 E0 idx temp fanHi fanLo carga 00…`
   e embaralha os bytes a partir do 3º, bit a bit, com a semente do handshake.
3. **Confirmação** — o visor devolve o quadro decodificado (o app usa isso para
   conferir que o índice chegou certo).

O transporte é **HID nativo do Windows** (SetupAPI + `CreateFile` + I/O sobreposto):
sem `hidapi.dll`, sem DLL de terceiros e sem dependência do app original. O
aplicativo acha o visor pelo VID/PID `0416:1100` e escolhe a coleção do fabricante
(página de uso `0xFF`, relatório de 64 bytes) — a coleção de toque é ignorada.

| Arquivo | O que faz |
|---|---|
| `KalkanProtocol.cs` | Protocolo puro: pedido de semente, quadro, embaralhador. É o único lugar com a lógica do firmware — e é o que os testes cobrem byte a byte. |
| `KalkanNative.cs` | P/Invoke de SetupAPI, hid.pd e kernel32. |
| `KalkanHid.cs` | Transporte: enumeração do device, abertura, escrita e leitura com timeout. |
| `KalkanDisplay.cs` | Sessão: handshake, envio de quadros e leitura de conferência. |
| `MainWindow.Kalkan.cs` | A interface (cartão "VISOR KALKAN" na aba TRANAL INTEGRAÇÃO). |

**Uso:** abra o menu lateral → **TRANAL FERRAMENTAS → Integração** → cartão
**VISOR KALKAN** → `CONECTAR`. Marque "atualizar o visor todo segundo" para ele
acompanhar a temperatura em tempo real (ou `ENVIAR AGORA` para um envio só).

**O que aparece no visor:** o visor do Kalkan tem dois campos — "DEV TEMP" (°C) e
"FAN SPEED" (RPM). No app oficial você escolhe *de qual sensor* vem cada um; aqui
também, com duas listas no cartão:

- **TEMPERATURA DO VISOR** — qualquer sensor de temperatura que o PC expõe
  (pacote e núcleos da CPU, GPU, placa-mãe, discos, memória).
- **VENTOINHA DO VISOR** — qualquer ventoinha (CPU, placa-mãe, GPU).

As escolhas ficam gravadas em `%AppData%\TranalSentinel\settings.json` e voltam
prontas na próxima vez que abrir.

Para conferir o visor sem abrir a janela:

```powershell
TranalSentinel.exe --kalkan-teste
```

O teste conecta no visor, manda um quadro e grava o resultado em
`%AppData%\TranalSentinel\kalkan-teste.txt` (inclui a resposta bruta do aparelho,
que vem decodificada: `01 e0 <índice>`). Rodando como administrador, ele manda a
temperatura real do CPU; sem permissão, manda um quadro de teste de 99 °C para
dar para ver no visor.

**Atenção:**

- Só funciona na **Rev.1 (firmware 1.2)**. A Rev.2 (comprada a partir de janeiro de
  2026) usa **porta serial**, não USB HID — não é suportada por esta implementação.
  O número de série na caixa (`KLKxxxxx - AAMMDDXXX`) diz qual é a sua.
- **Feche o software oficial da Kalkan** antes de usar. Dois programas escrevendo no
  mesmo visor ao mesmo tempo embaralham os quadros um do outro (cada um tem sua
  semente). O software oficial também é a causa dos travamentos registrados na
  máquina (22 acessos inválidos dentro do `hidapi.dll` em 60 dias) — dá para
  tirá-lo da inicialização automática depois que este recurso estiver funcionando.
- A temperatura vem do LibreHardwareMonitor (o mesmo motor do app). Sem PawnIO e sem
  execução como administrador, o sensor da CPU não aparece e o visor mostra 0 °C.

**Créditos e licença:** o protocolo foi conferido contra a análise estática do
binário oficial publicada no projeto aberto
[medeiroz/kalkan-monitor-rs](https://github.com/medeiroz/kalkan-monitor-rs) (MIT),
que documenta o handshake, o quadro, o embaralhador e os vetores de teste usados
aqui. Nenhum código do aplicativo oficial da Kalkan foi copiado, redistribuído ou
modificado — esta implementação fala com o seu próprio hardware por um protocolo
documentado.

<!-- ================= FIM DO AVISO DO FORK ================= -->

---

# TRANAL SENTINEL — v0.17

> **Download:** apenas nas [releases deste repositório](https://github.com/Matheus-araujo-dev/tranal-sentinel/releases).
> O app roda como administrador; não use versões baixadas de outros lugares.
>
> **Contribuições:** este projeto não aceita pull requests. Fique à vontade para fazer um *fork* e modificar a sua própria cópia
> (licença [MIT](LICENSE): mantenha o aviso de copyright).

## Novidades da v0.17 — o app vira assistente

- **TRANAL ASSISTENTE** (nova aba): a **linha do tempo do PC**. O app tira um retrato
  do sistema de tempos em tempos (Windows, BIOS, drivers, plano de energia, memória,
  programas instalados, itens de inicialização) e anota **o que mudou** desde o
  retrato anterior, com data e hora. Driver novo, programa instalado, plano de
  energia trocado: fica registrado.
- **Diário diário**: um arquivo de log por dia em `%AppData%\TranalSentinel\log\`,
  com o que o app fez. Aparece na aba do assistente.
- **Ícone na bandeja**: o app passa a morar na bandeja (ao lado do relógio) e
  acompanha o PC em segundo plano. Fechar a janela **não** fecha o app — ele
  continua olhando; o menu do ícone tem *Abrir* e *Sair*.
- **Instalador próprio**: `TranalSetup.exe` pede administrador, instala em
  `%ProgramFiles%\Tranal Sentinel` **ou na pasta que você escolher**, cria atalho
  no menu Iniciar (e na área de trabalho), registra a desinstalação no Painel de
  Controle e **inicia junto com o Windows** sem pedir UAC a cada boot.
- **Atualização automática**: o app confere a API do GitHub do próprio projeto e
  avisa quando há versão nova. O download é feito pelo app, com barra de
  progresso e conferência de tamanho; a troca do executável acontece quando o
  app fecha (o Windows não deixa sobrescrever um .exe em uso).
- **Perfil de recursos**: Completo / Equilibrado / Economia. No modo economia os
  sensores são lidos a cada 5 s em vez de 1 s, o histórico passa de 3 h para 24 h
  e o overlay e a análise de saúde não rodam sozinhos.
- **Diagnóstico pela linha de comando**: `--saude coletar|comparar|relatar`,
  reaproveitando exatamente as mesmas regras da aba TRANAL SAÚDE.

Medição real do custo (60 s, app e coletor rodando): **0,215 % da CPU da máquina**
(16 núcleos) e ~364 MB de RAM. A leitura do hardware é o que pesa — por isso o
perfil de recursos muda o intervalo da leitura, e não o runtime.
Ver [MERCADO.md](MERCADO.md) para a pesquisa de concorrentes e o que foi
adotado aqui.

## Novidades da v0.16
- **Inicialização** (TRANAL FERRAMENTAS): veja, desative ou remova os programas que abrem junto com o Windows, com opção de desfazer.
- **Instalar apps** (TRANAL FERRAMENTAS): Steam, Chrome, Discord, Lightshot, 7-Zip, WinRAR e VLC de uma vez depois de formatar, pelo winget.
- **TRANAL MAKER** (nova categoria): Portas COM (fantasmas e números COM presos, que saiu de Otimização) e Drivers seriais
  (CH340, CP2102, FTDI, Prolific e Arduino: diagnóstico, reinstalar, trocar ou remover versão do driver, Arduino IDE).
- **Abertura mais rápida**: a janela aparece em ~0,5 s (antes ~4,6 s); o hardware é lido em paralelo, em segundo plano.
- **Overlay corrigido**: fica por cima de jogos em janela sem borda como GTA V e Cyberpunk 2077 e segue o monitor do jogo.

Detalhes em [NOVIDADES.md](NOVIDADES.md) (o mesmo texto vai nas notas da release).

App WPF (.NET 10) com:
- **Informações do sistema** (categorias + detalhes): SO, placa-mãe/BIOS, CPU, RAM (por módulo), GPU, discos — via WMI.
  Em Armazenamento, cartões por unidade (C:, pendrive, HD externo, rede) com anel de cheio/vazio, tipo e disco físico (`DriveMap.cs`).
- **Avisos de dispositivo** (`ToastWindow.xaml`): ao conectar/remover pendrive, HD externo ou qualquer dispositivo USB (ex.: Arduino),
  aparece um aviso no canto da tela e a lista de unidades se atualiza (via `WM_DEVICECHANGE`, sem ficar consultando).
- **Sensores em tempo real**: temperatura, clock, carga, voltagem, ventoinhas, consumo etc. — via LibreHardwareMonitorLib. Atualiza a cada 1 s, com filtro por tipo.
- **Placa de vídeo** (estilo GPU-Z): chip, arquitetura, processo, die, transistores, shaders/SMs/Tensor/RT, ROPs/TMUs, fillrates, TFLOPS,
  memória (tamanho, tipo, barramento, clock, banda, Resizable BAR), clocks, PCIe (máximo e em uso), limites de energia,
  VBIOS, driver, assinatura WHQL, CUDA, DirectX (feature level), APIs e recursos da arquitetura.
  Sensores ao vivo da GPU + P-state, PerfCap, NVENC/NVDEC. Fontes: NVML (NVIDIA), DXGI/Direct3D 12, WMI/PnP, registro e banco de specs em `GpuSpecDb.cs`.
- **Stress test**: carga de CPU em todos os threads (FPU + inteiros) e/ou RAM (grava e verifica padrões em 40–80% da memória livre).
  Para sozinho quando a temperatura da CPU atinge o limite configurado, quando o tempo acaba ou se aparecer erro de memória.
  Mostra temperatura atual/máxima, carga, consumo, gráfico dos últimos 5 min e um log dos eventos.
- **Stress de GPU** (inspirado no FurMark, `GpuBurn.cs`): torus com pelos renderizado por *shells* em Direct3D 11 (Vortice),
  com overdraw massivo, cadeia de FMAs no pixel shader e alpha blending, sem limite de FPS, numa janela própria.
  Opções: resolução (720p/1080p/1440p/tela cheia), MSAA (off/4x/8x), intensidade (Normal/Alta/Extrema) e limite de temperatura da GPU.
  Mostra FPS, temperatura, consumo e PerfCap; ao final registra frames e FPS médio. ESC ou fechar a janela encerra o teste.
  Medido numa RTX 5070 (1080p, 4x MSAA, Alta): ~99% de uso e 220–232 W de 250 W.
- **Jogos**: FPS esperado em 32 jogos populares para a sua GPU + CPU, por resolução (1080p/1440p/4K), gráficos e upscaling (DLSS/FSR).
  Com chave da [HowManyFPS API](https://howmanyfps.com/developer-api) (pedida em info@howmanyfps.com) mostra FPS médio e 1% low medidos em sessões reais;
  sem chave, usa uma estimativa local aproximada (`GameCatalog.cs`). A chave fica cifrada com DPAPI em `%AppData%\TranalSentinel` e as respostas em cache por 7 dias.
- **Detector de gargalo**: equilíbrio teórico GPU × CPU; leitura ao vivo (carga da GPU, CPU total, núcleo mais ocupado, VRAM, RAM,
  temperaturas, limitador térmico) com veredito; e análise de sessão (1/3/5 min) com % do tempo limitado por GPU, CPU, VRAM, RAM,
  temperatura ou trava de FPS, mais recomendações (`BottleneckAnalyzer.cs`).
- **Overlay de jogos** (estilo RTSS/Afterburner, `OverlayWindow.xaml`): FPS, frametime, 1% low, gráfico de frametime, GPU/CPU/RAM/VRAM.
  Janela transparente, sempre no topo e que ignora cliques; atalho global **Ctrl+Shift+O**. **Itens escolhidos pelo usuário** (`OverlayMetrics.cs`,
  `MainWindow.Overlay.cs`): FPS/frametime/1% low/gráfico, GPU (uso, temperatura, hotspot, clocks do núcleo e da memória, consumo, tensão,
  ventoinha, PerfCap), VRAM, CPU (uso, núcleo mais ocupado, temperatura, clock médio e máximo, consumo), RAM, nome do jogo e hora. Volta para o topo a cada troca de janela ativa
  (jogos em janela sem borda também se marcam como "sempre no topo") e fica no monitor do jogo. Não aparece em tela cheia exclusiva. O FPS de qualquer jogo é medido via ETW
  (eventos Present de DXGI/D3D9, como o PresentMon, `FrameMonitor.cs`), sem injetar nada no jogo.
- **TRANAL BENCH** (`BenchRenderer.cs`): benchmark de CPU no princípio do Cinebench, com renderizador próprio. Path tracer monocromático
  (iluminação global, sombras suaves, metal e vidro com refração), 960×540 a 1024 amostras/pixel, imagem em blocos de 32 px
  distribuídos entre threads do centro para fora. Multi-core (todas as threads) e single-core (1 thread, imagem inteira, ~1min40s num 7700X),
  1 passada ou mínimo de 10 min. Nota = amostras/s ÷ 10.000, determinística (sementes fixas por pixel). Histórico dos últimos 10 resultados.
  Referência: Ryzen 7 7700X ≈ 4.500 multi / 530 single.
- **TRANAL BENCH · benchmarks de jogo**:
  - *Cena de jogo (GPU)* (`GpuGameBench.cs`): vale em Direct3D 11 com terreno fractal, 40 mil árvores por instancing, sombras 4096² com PCF,
    água, neblina e pós HDR; câmera em trajeto fixo por tempo, 1080p/1440p/4K, 30/60 s. Nota = FPS médio × 10. RTX 5070 em 1080p ≈ 680 FPS.
    Os sensores ficam pausados durante o teste (consultas ao driver geravam engasgos no 1% low).
  - *Ray tracing (GPU)* (`GpuRtBench.cs`, `MainWindow.Rt.cs`): Direct3D 12 com **DXR 1.1** (raios inline num compute shader), usa os núcleos
    de RT (RTX 20+, Radeon RX 6000+, Intel Arc). 4 mil objetos (fosco, metal, espelho, luzes), path tracing com sombra suave, oclusão de
    ambiente, reflexos e iluminação indireta; TLAS refeita a cada frame. Presets Normal/Alta/Extrema (2/4/8 amostras, 2/3/4 rebatidas).
    **Confiabilidade**: aquecimento sem medir + 3 passadas; nota pela mediana (FPS × 10), variação entre passadas (confiável abaixo de 3%),
    tempo de frame pelos timestamps da GPU. Referência: RX 9070, Alta 1440p, modo Padrão = 878 pts (87,8 FPS), variação 0,5%.
  - *Simulação de jogo (CPU)* (`GameSimBench.cs`): 30 mil unidades em frente de batalha; thread principal (grade, lógica, preparo do desenho)
    + workers (física, colisão, IA). Nota = FPS da simulação. Ryzen 7 7700X ≈ 290 FPS.
  - *Memória e carregamento* (`MemoryBench.cs`): latência e banda da RAM; carregamento de mapa (pacote de 512 MB + mil arquivos de 64 KB)
    lido sem cache do Windows, com passada prévia para o antivírus e 8 threads. Arquivos temporários apagados no final.
  - *Disco* (`DiskBench.cs`): leitura e escrita sequencial (1 MB) e aleatória 4K (QD1 e QD32), sem cache do Windows
    (`FILE_FLAG_NO_BUFFERING`), em arquivo temporário de 512 MB / 1 GB / 4 GB na unidade escolhida; apagado no final. Histórico dos últimos resultados.
- **TRANAL OC** (`MainWindow.Oc.cs`, `OcCheck.cs`): ajuste de limite de energia e travamento de clock da GPU NVIDIA (via NVML) com botão de restaurar,
  e **verificação de estabilidade** de overclock (CPU, GPU ou ambos; 5/15/30 min) com limites de segurança de temperatura, leitura de erros
  de hardware (WHEA, reset de driver de vídeo), veredito, histórico e guia de BIOS conforme a placa-mãe e a CPU.
- **TRANAL OTIMIZA** (`Optimizer.cs`, `WindowsTweaks.cs`): um botão que limpa a memória em espera (como o ISLC) e enxuga a RAM dos
  processos, e fecha os programas marcados (pré-marca só os seguros: OneDrive, Teams, Widgets, Adobe...; programas com janela são
  fechados sem forçar). Ajustes do Windows com interruptor e "Restaurar originais": plano Alto desempenho, Modo de Jogo, desativar
  Game DVR, HAGS, otimizações para jogos em janela, aceleração do mouse. Nada que reduza a segurança (Defender, VBS, UAC, Update).
- **TRANAL MAKER** (Arduino, ESP32 e conversores USB-serial):
  - *Portas COM* (`UsbCleaner.cs`): lista e remove registros de dispositivos USB/seriais ausentes ("fantasmas" de Arduino,
    ESP32, CH340, FTDI, CP210x...) e libera os números COM presos no ComDB, com backup para desfazer. Resolve a IDE do Arduino
    não achar a placa ou a porta ir para COM20+.
  - *Drivers seriais* (`MakerDrivers.cs`, `MainWindow.Maker.cs`): diagnóstico das placas conectadas com **CH340/CH341, CP2102/CP210x,
    FTDI, Prolific PL2303 e Arduino** pelo código do Gerenciador de Dispositivos (sem driver, código 10, 43, FTDI falsificado com PID 0000),
    com orientação para cada caso; **REINSTALAR** (remove e detecta de novo); lista das versões de driver guardadas no Windows com
    **REMOVER** (backup antes, para voltar a uma versão anterior ou desinstalar de vez) e "Restaurar removidos"; **drivers oficiais incluídos no app** (CH340 4.0, CP210x 11.6,
    FTDI 2.12.36.20 e Arduino, em `Drivers`, veja `Drivers/LEIA-ME.md`) que instalam sem internet; e link para a página oficial de cada fabricante. **Arduino IDE**: instalar/atualizar pelo winget (traz os drivers das placas oficiais),
    fechar programas que prendem a porta (monitor serial, PuTTY, Cura...) e limpar o cache de índices quando a IDE trava em "Downloading index".
- **Inicialização** (`StartupManager.cs`, `MainWindow.Startup.cs`): lista os programas que abrem com o Windows (chaves Run do usuário e
  de todos os usuários, 32 e 64 bits, e pastas Inicializar) com descrição e fabricante. Interruptor para ativar/desativar igual ao
  Gerenciador de Tarefas (chaves StartupApproved) e botão REMOVER, com cópia em `%AppData%\TranalSentinel` para "Restaurar removidos".
- **Instalar apps** (`AppInstaller.cs`, `MainWindow.Apps.cs`): instala os essenciais pós-formatação (Steam, Google Chrome, Discord,
  Lightshot, 7-Zip, WinRAR, VLC) pelo **winget**, em silêncio e um de cada vez, mostrando o resultado de cada um. Detecta o que já está
  instalado (Aplicativos instalados do Windows). Para incluir outro app, acrescente uma linha em `AppInstaller.Catalog()` com o ID do winget.
- **Saúde dos discos (S.M.A.R.T.)** (`DiskHealth.cs`, `MainWindow.Disks.cs`): página própria com um cartão por disco físico —
  veredito (Bom/Atenção/Ruim) com os motivos, temperatura, horas ligado, ciclos, desgaste do SSD, setores realocados, erros de leitura/escrita
  e previsão de falha. Fontes: Windows Storage (MSFT_PhysicalDisk, MSFT_StorageReliabilityCounter) e MSStorageDriver_FailurePredictStatus.
- **Relatório exportável** (`ReportBuilder.cs`): botão **EXPORTAR RELATÓRIO** em Sistema. Gera um arquivo HTML (com a marca RESET) ou JSON
  com as informações do sistema, RAM por módulo, discos e saúde dos discos.
- **RAM detalhada**: resumo (capacidade, canais, tipo DDR4/DDR5/LPDDR5, velocidade) e, por módulo, fabricante, part number, formato,
  voltagem, clock configurado e localização do slot.
- **Energia** (`BatteryInfo.cs`, `MainWindow.Net.cs`): bateria de notebook com desgaste (capacidade atual × de projeto), ciclos de carga, estado,
  autonomia, tensão e potência, com veredito; fonte de energia (tomada/bateria), plano de energia do Windows e consumo dos sensores de CPU e GPU.
  Em PC de mesa mostra só o cartão de energia.
- **Rede** (`NetworkTest.cs`): diagnóstico de lag em ~30 s — latência, jitter e perda de pacotes até o roteador e até a internet (1.1.1.1 e 8.8.8.8),
  tempo de DNS, velocidade de download/upload (servidor público speed.cloudflare.com) e latência sob carga (bufferbloat). O veredito aponta se o
  problema está na rede local (Wi-Fi/cabo/roteador), no provedor ou se a conexão está estável.
- **TRANAL INTEGRAÇÃO** (`LightBackends.cs`, `CorsairSdk.cs`, `CorsairSdkInstaller.cs`, `MainWindow.Integ.cs`): controle de iluminação e efeitos
  de todo o PC numa tela só (em teste). Pontes: **Corsair iCUE / iCUE LINK** (iCUE SDK v4; a DLL é baixada pelo programa, com confirmação e
  verificação de assinatura), **OpenRGB** (servidor SDK local; é o caminho para **MSI Mystic Light, Gigabyte RGB Fusion, ASUS Aura**, RAM, Razer,
  Logitech e outras, já que essas marcas não têm SDK público) e **Razer Chroma** (API REST do Synapse). Efeitos: cor fixa, respiração, arco-íris em onda,
  e cores que seguem a **temperatura da CPU/GPU** ou a **carga da CPU**, com brilho e velocidade. Ao devolver ou fechar, cada fabricante reassume as luzes.
  Para MSI/Gigabyte/ASUS: abra o OpenRGB com o servidor ligado e feche MSI Center, Armoury Crate e RGB Fusion.
- **TRANAL JOGOS** (`GameLibrary.cs`, `GameLauncher.cs`, `Vdf.cs`, `MainWindow.Launcher.cs`): inicializador que **detecta os jogos instalados** (Steam, Epic Games, GOG,
  Ubisoft Connect e `.exe` adicionados à mão) e guarda um **perfil por jogo**: prioridade do processo, plano de energia Alto desempenho só durante o jogo,
  GPU dedicada e sem otimizações de tela cheia (por `.exe`, no registro do usuário), **menu de gráficos** (modo de janela, resolução, taxa de atualização e extras
  convertidos em argumentos de inicialização no estilo Source/CS2, Unity ou Unreal) e **programas auxiliares** (ferramentas de terceiros que abrem antes do jogo
  e fecham junto). Em jogos da Steam, um botão grava os argumentos nas **opções de inicialização** (`localconfig.vdf`, com backup, fechando a Steam antes).
  O botão JOGAR aplica o perfil, inicia e desfaz o que for temporário quando o jogo fecha.

## Interface
- Visual preto e branco minimalista em **blocos** (cartões arredondados, inspirado em painéis de dashboard). Um único menu lateral
  com *Visão geral* no topo e categorias **RESET** recolhíveis (começam fechadas; clique para ver as sub-abas):
  - *Visão geral* (`MainWindow.Dash.cs`): gráfico de área CPU × GPU (temperatura, carga ou consumo; 1/5/15 min),
    desempenho do TRANAL BENCH, tabela de componentes, anéis de RAM/VRAM/disco, eventos recentes e saúde do PC.
  - **TRANAL SENTINEL** · Sistema, Sensores, Ventoinhas, Placa de vídeo, Discos, Energia, Rede.
  - **TRANAL JOGOS** · Biblioteca, FPS estimado, Gargalo, Overlay.
  - **TRANAL DESEMPENHO** · Benchmark, Stress test, Overclock.
  - **TRANAL FERRAMENTAS** · Otimização, Inicialização, Instalar apps, Drivers, Integração.
  - **TRANAL MAKER** · Portas COM, Drivers seriais.
- Botão de **modo escuro/claro** no canto superior direito (`ThemeManager.cs`); a escolha fica salva. O overlay de jogos e os avisos de dispositivo ficam sempre escuros.
- Leitura ao vivo de temperatura e carga da CPU e da GPU sempre visível no rodapé do menu.
- Sensores com busca e filtros por tipo (chips); tema em `Theme.xaml`.

## Base utilizada
- Interface, stress test e gráficos: código próprio.
- Sensores: [LibreHardwareMonitorLib](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor) (MPL 2.0) — mantenha o aviso de licença ao distribuir.
- Informações do sistema: WMI do Windows.
- Medição de FPS: [TraceEvent](https://github.com/microsoft/perfview) (MIT), da Microsoft, para ler eventos ETW.
- Renderização do stress de GPU: [Vortice.Windows](https://github.com/amerkoleci/Vortice.Windows) (MIT), bindings de Direct3D 11 para .NET. Direct3D 12 e DXC (compilador de shaders da Microsoft) também via Vortice, no teste de ray tracing.

## Requisitos
- Windows 10/11
- .NET 10 SDK (https://dotnet.microsoft.com/download)
- Driver **PawnIO** (temperatura/consumo da CPU): `winget install namazso.PawnIO`
  A LibreHardwareMonitorLib 0.9.6+ usa o PawnIO no lugar do antigo WinRing0, que o Windows Defender bloqueia.

## Como rodar
```
cd tranal-sentinel
dotnet run
```
O app pede privilégio de administrador (necessário para sensores de baixo nível).
Para levar a outro PC: dê dois cliques em `publicar.bat`. Ele gera `..\TranalSentinel-dist\TranalSentinel.exe` (arquivo único, com o .NET embutido,
sem caminhos do seu usuário) + `LEIA-ME.txt`. No outro PC só é preciso instalar o PawnIO. Rode de novo após cada alteração no código.

### Baixar a versão mais recente

A cada push na `main`, o GitHub Actions (`.github/workflows/publicar.yml`) gera o `TranalSentinel.exe` e atualiza a release
**latest** (aba *Releases* do repositório), com o `.exe` e um `.zip`. É só baixar de lá, sem compilar.
Ao criar uma tag (`git tag v0.17` e `git push --tags`), também sai uma release fixa dessa versão.

### Como instalar

Há dois jeitos. Nos dois, o app **pede administrador** (ele precisa ler sensores de baixo nível, MSRs e Super I/O).

**1. Portátil (o mais simples)** — baixe `TranalSentinel.exe` da release e dê dois cliques.
Roda direto de onde foi baixado. Não instala nada, não cria atalho, não abre junto com o Windows.

**2. Instalado (com inicialização automática)** — baixe **os dois** arquivos da release para a *mesma* pasta:

```
TranalSentinel.exe     <- o app
TranalSetup.exe        <- o instalador
```

Dê dois cliques em `TranalSetup.exe`. Ele:

- **mostra a pasta de instalação e deixa você mudar**: o campo é editável e o
  botão `...` abre o seletor de pasta do Windows (o botão cria a subpasta
  `Tranal Sentinel` dentro da pasta escolhida; digitando o caminho completo na
  caixa, vale exatamente o que foi digitado). O padrão é
  `%ProgramFiles%\Tranal Sentinel`;
- se o app já estiver instalado em outra pasta, instalar aqui é **uma mudança de
  pasta**: os arquivos do lugar antigo são removidos depois que a nova
  instalação está pronta (nada fica órfão);
- cria atalho no menu Iniciar (e, se você quiser, na área de trabalho);
- registra a desinstalação em *Painel de Controle → Programas*;
- **faz o app abrir junto com o Windows**, já em segundo plano, na bandeja, **sem pedir UAC a cada login**;
- deixa um log em `%ProgramData%\Tranal Sentinel\instalador.log`.

Para desinstalar: *Painel de Controle → Programas → TRANAL SENTINEL → Desinstalar* (ou rode o
`TranalSetup.exe --desinstalar`). A desinstalação descobre sozinha onde o app foi
instalado, mesmo que não seja na pasta padrão. Os seus arquivos de configuração e
histórico ficam em `%AppData%\TranalSentinel` — a desinstalação **não** apaga o que
é seu.

**Atualização automática:** o app confere sozinho a API do GitHub deste projeto e avisa quando há versão
nova (banner na lateral). O download é feito pelo app, com barra de progresso e conferência do tamanho
do arquivo; a troca do executável acontece quando o app fecha, porque o Windows não deixa sobrescrever
um `.exe` em uso.

## Observações
- Se algum sensor não aparecer, depende do suporte da biblioteca à sua placa-mãe.

## Já feito
- ✔ Informações do sistema, sensores, placa de vídeo, jogos, gargalo, overlay
- ✔ Stress test com limite de temperatura (CPU, RAM, GPU)
- ✔ Benchmarks: CPU, GPU, simulação, memória e **disco**
- ✔ Saúde dos discos (S.M.A.R.T.)
- ✔ Relatório exportável (HTML/JSON)
- ✔ TRANAL OC (limite de energia, clock e verificação de estabilidade)
- ✔ Energia (bateria e fonte) e Rede (latência, perda e velocidade)
- ✔ TRANAL INTEGRAÇÃO: iluminação Corsair iCUE, OpenRGB (MSI/Gigabyte/ASUS) e Razer Chroma (primeira versão, em teste)
- ✔ TRANAL DRIVER: inventário de drivers, NVIDIA (verifica a versão, baixa, confere a assinatura e abre o instalador), AMD/ASUS/MSI/Gigabyte pela página oficial, Windows Update e backup (`DriverManager.cs`, primeira versão, em teste)
- ✔ TRANAL JOGOS: inicializador com detecção de jogos, perfil por jogo e opções de inicialização da Steam (primeira versão, em teste)
- ✔ Inicialização do Windows: ativar, desativar e remover programas, com desfazer (v0.16)
- ✔ Abertura rápida: janela na hora, hardware carregado em paralelo (v0.16)
- ✔ Instalar apps essenciais pós-formatação pelo winget (v0.16)
- ✔ TRANAL MAKER: portas COM e drivers seriais CH340/CP210x/FTDI/Prolific/Arduino (v0.16)
- ✔ TRANAL ASSISTENTE: linha do tempo do PC, diário, ícone de bandeja e perfil de recursos (v0.17)
- ✔ Instalador próprio com administrador, atalho, desinstalação e inicialização automática (v0.17)
- ✔ Atualização automática a partir das releases do GitHub (v0.17)
- ✔ Diagnóstico pela linha de comando: `--saude coletar|comparar|relatar` (v0.17)
- ✔ Instalador com pasta de instalação à escolha do usuário (v0.18)
- ✔ TRANAL MEMÓRIA: SPD lido pelo SMBus (PawnIO) e tempos ativos lidos do controlador de memória do AMD (v0.25)
- ✔ TRANAL COMPARAR: os benchmarks de um PC lado a lado com os de outro (v0.26)
- ✔ TRANAL USO: tempo de tela por programa, "quem fez o calor" e o "por que disparou" de cada alerta (v0.27)
- ✔ Banco de dados do histórico em SQLite (sem dependência nova), com verificação de integridade, reparo e restauração da cópia (v0.28)
- ✔ TRANAL REDE: tráfego de internet por programa, medido pelo próprio Windows, com histórico de 30 dias (v0.29)
- ✔ TRANAL VENTOINHAS: o RPM de cada ventoinha comparado com o próprio recorde, para dizer se ela está gasta (v0.30)

## Próximas ideias
- Assistente de IA local (Ollama/LM Studio) respondendo o relatório do TRANAL SAÚDE, sem sair da máquina
- Histórico de relatórios do TRANAL SAÚDE ao longo das semanas
- Leitura direta de Super I/O
- TRANAL INTEGRAÇÃO: perfis salvos, efeitos por jogo, SDKs nativos da MSI/ASUS/Gigabyte se forem liberados
- Assinatura digital do instalador
- Ver [MERCADO.md](MERCADO.md) para a pesquisa de concorrentes e o que foi adotado aqui

## Licença

MIT — veja o arquivo [LICENSE](LICENSE).
