## Novidades da v0.27 — o que ficou em frente na tela e por que cada alerta disparou

O app já sabia o que estava acontecendo na máquina (a temperatura, a carga, o
disco). Agora ele também guarda **quem estava em frente na tela**, e usa isso
para responder a duas perguntas que sempre sobraram: "quanto tempo eu passei
neste programa?" e "por que esse aviso disparou?".

**Novo · TRANAL USO (menu TRANAL ASSISTENTE)**
- TEMPO DE TELA: quanto tempo o app observou a tela no dia (em horas e
  minutos), o programa que está em frente agora (com o título da janela) e o
  ranking de tudo que ficou em frente, com o tempo de cada um e o percentual
  do total.
- SESSÕES MAIS LONGAS: as 5 sessões mais longas do dia, com programa, título
  da janela, hora de início e duração. A sessão é o período em que um mesmo
  programa ficou em frente, sem que outro programa tomasse a frente.
- QUEM FEZ O CALOR: o programa que estava em frente quando a CPU ficou mais
  quente, a temperatura máxima e o que fazer com isso. Sem histórico de
  sensores, o app diz que não sabe — número inventado é pior que número
  ausente.
- O seletor DIA troca o dia (hoje vem primeiro, e os dias com histórico
  embaixo) e o botão ATUALIZAR recarrega a página.
- O histórico fica em %AppData%\TranalSentinel\uso\, um arquivo por dia, e é
  apagado depois de 30 dias. O app anota só o nome do programa e o título da
  janela: nunca a imagem da tela, o conteúdo dos arquivos ou o que foi
  digitado. Nada sai do computador.
- O tempo só é contado de onde o app começou a observar até onde parou: as
  horas em que ele estava fechado (PC desligado, app fechado) não entram na
  conta. E o próprio TRANAL SENTINEL aparece na lista quando ele está em
  frente — o tempo em que você olha o monitor é dele.

**Novo · o "por que disparou" no TRANAL ALERTAS**
- Cada aviso ativo mostra agora por que disparou: o que os sensores faziam
  nos dois minutos antes de a condição começar (CPU a 81,5 °C, carga da CPU
  92 %) e qual programa estava em frente na hora.
- Quando o app não estava gravando sensores naquele momento, o aviso diz
  exatamente isso. O que o aviso mostra hoje não muda: a explicação vem
  embaixo, como uma linha a mais.

**O que continua igual**
- A regra do aviso é uma só (`RegraAlerta` + `SensorCatalogo` +
  `MonitorAlertas`): a tela, a vigia e o TRANAL SAÚDE usam a mesma. A
  explicação só consome o que o histórico já tem.
- A aba é somente leitura. O app não muda nada no sistema por causa dela.
- A regra que interpreta o histórico é função pura e tem teste próprio; a
  coleta é a parte que olha a tela e grava o arquivo. A tela só mostra.

---
## Novidades da v0.26 — comparar os benchmarks de um PC com o de outro

O TRANAL BENCH mostra o quanto a sua máquina é rápida. Agora dá para **comparar
com a de outra pessoa**: um arquivo leve leva os resultados (e a identificação
do PC) para o outro computador, e lá a tabela mostra os dois lado a lado.

**Novo · TRANAL COMPARAR (menu TRANAL DESEMPENHO)**
- EXPORTAR grava um arquivo `.tranalbench.json` com os resultados de
  benchmark da máquina e a identificação dela (CPU, placa de vídeo, memória,
  sistema). É um arquivo seu: nada sai do computador.
- IMPORTAR lê o arquivo de outro PC e compara com os resultados desta
  máquina, teste a teste.
- A tabela mostra a nota de cada lado, a diferença em porcentagem e o
  veredito ("mais rápido", "mais lento", "empate"). Compara o melhor
  resultado de cada teste, como o Cinebench faz.
- Quando as máquinas são diferentes (CPU, placa de vídeo, memória, sistema),
  o app avisa: a diferença de nota não é só de hardware.
- Os testes que existem em um dos PCs só (o outro não rodou aquele
  benchmark) são mostrados à parte, em vez de sumirem.
- Benchmark que falhou (nota zero) não vira "empate (0%)": o app avisa que
  aquele teste não tem nota com que comparar. O mesmo vale para arquivo
  danificado ou de uma versão mais nova do formato — o app recusa com
  explicação, em vez de mostrar número errado.

**Novo · linha de comando**
- `TranalSentinel.exe --comparar exportar <arquivo>` — exporta os
  benchmarks da máquina.
- `TranalSentinel.exe --comparar importar <arquivo>` — compara o arquivo de
  outro PC com os benchmarks desta máquina.
- `TranalSentinel.exe --comparar comparar <arquivoA> <arquivoB>` — compara
  dois arquivos, sem precisar de benchmark rodado aqui.

**Ajuste interno**
- O nome de cada benchmark passou a ter um lugar só (o TRANAL BENCH, o
  relatório exportável e a comparação usam a mesma tabela), para os três
  não divergirem na primeira edição em um deles.
- O render de 10 minutos (sustentado, limitado termicamente) passou a ser um
  teste à parte do render curto: os dois não são comparados um contra o
  outro.
- A placa de vídeo do arquivo sai da mesma regra que a tela do app usa
  (a dedicada primeiro, pela VRAM), e não da primeira linha do WMI.

---

## Novidades da v0.25 — o app agora lê os tempos que a BIOS programou de verdade

Até aqui o TRANAL MEMÓRIA mostrava o que o módulo de memória **oferece**: o
padrão JEDEC e o perfil XMP/EXPO gravados no SPD. Agora ele mostra também o
que está **rodando**: os tempos ativos, lidos dos registradores do
controlador de memória do processador.

A diferença entre os dois é o que explica travamento. Tempo mais apertado
que o perfil é ajuste manual na BIOS — e é a causa mais comum de
instabilidade por memória.

**Novo · tempos ativos de memória**
- O app lê os registradores do UMC do AMD pelo módulo PawnIO do SMN: clock
  real, taxa de comando, gear down, tCL, tRCD, tRP, tRAS, tRC, tFAW, tWR,
  tWTR, tCKE, tREFI e mais.
- Cada tempo aparece lado a lado com o do perfil do módulo (ou com o padrão JEDEC,
  quando é o que está rodando), e a coluna "situação" diz se está igual, mais
  apertado ou mais solto.
- No TRANAL SAÚDE, tempo primário mais apertado que o perfil vira achado
  próprio, na área Memória, com a ação sugerida.
- Funciona em processador AMD das famílias Zen 2, Zen 3 e Zen 4 com memória
  DDR4. Em processador Intel ou com DDR5 o app diz honestamente que não
  conseguiu ler, em vez de mostrar número inventado.

**Prova real (PC do owner, Ryzen 7 5700X)**
- Lido de verdade: 3466 MT/s (1733 MHz), 1T, gear down, 18-22-22-42.
- O perfil XMP 2.0 do módulo é 18-22-22-42: os tempos ativos são exatamente
  os do perfil. A BIOS aplicou o XMP e só subiu o clock de 3200 para
  3466 MT/s — não há ajuste manual nos tempos.

**Como usar**
- TRANAL MEMÓRIA -> aba "Módulos" -> LER MÓDULOS. O cartão "TEMPOS ATIVOS"
  aparece logo abaixo do resumo.
- Pelo terminal: `TranalSentinel.exe --memoria`.
- Continua sendo somente leitura: nenhum registro do processador é escrito.

---

## Novidades da v0.23 — o app agora conta o que a máquina estava fazendo antes de travar

O TRANAL SAÚDE já contava os travamentos (o evento Kernel-Power 41, que o
Windows grava quando o PC é desligado sem limpeza). Agora ele também conta o
que os **sensores estavam fazendo** nos minutos que antecederam o
congelamento — que é a pergunta que interessa: a CPU estava a 95 °C? A GPU
tava a 100 %? O clock tinha caído?

**Novo · log contínuo de sensores**
- O app passa a gravar o estado dos sensores a cada 5 segundos: temperatura e
  clock da CPU, carga da CPU, temperatura, carga, clock e VRAM da GPU, uso da
  RAM e o tráfego do disco.
- O log fica em `%LOCALAPPDATA%\TranalSentinel\sensores\historico.csv` — um CSV
  simples, que dá para abrir em qualquer editor. Ele gira sozinho (fica no
  máximo 8 MB, guardando o que é mais recente).
- A gravação acontece fora da thread da tela: escrever arquivo na interface já
  tinha chegado a engasgar o benchmark.

**Novo · retrospectiva do travamento**
- Cada travamento da lista do TRANAL SAÚDE passa a mostrar a linha do que os
  sensores faziam nos 2 minutos antes do congelamento:
  `CPU a 95 °C · GPU a 72 °C · carga da CPU 87 % · RAM 91 % · clock da CPU 3,2 GHz`.
- Quando o app não estava rodando na hora (ou o log ainda não tem nada), a
  linha diz isso em vez de fingir que sabe.
- O mesmo texto aparece no relatório de texto (`TranalSentinel.exe --saude
  relatar`).

**Como usar**
- O app precisa estar aberto (na bandeja) para gravar o log. O quanto antes
  ele ficar rodando, mais antigo é o histórico que dá para correlacionar.
- Nada disso muda o computador: o log é só leitura de sensores.

---

## Novidades da v0.22 — o app passou a ler a memória de verdade

O TRANAL SENTINEL agora lê o **SPD** dos módulos de memória: o "cartão de visita"
de 512 bytes que cada módulo carrega, lido direto pelo barramento SMBus do
processador. É a mesma informação que o CPU-Z e o HWiNFO mostram — aqui sem
instalar nada além do que o app já usava.

**Nova página · TRANAL MEMÓRIA › Módulos**
- Cada módulo aparece com fabricante, part number, número de série, capacidade,
  ranks, latência, os tempos do padrão **JEDEC** e o perfil **XMP/EXPO** gravado
  dentro do módulo.
- O perfil XMP 2.0 é lido campo a campo: clock (3200 MT/s), latência (CL 18),
  tempos principais (18-22-22-42) e a tensão do perfil (1,35 V).
- Os identificadores que o módulo guarda em binário (fabricante, número de série)
  aparecem em hexadecimal, do jeito que vem impresso na etiqueta.
- Precisa do driver **PawnIO** — o mesmo que o app já usa para a temperatura da
  CPU. Se ele faltar, o botão CORRIGIR do menu lateral instala com um clique.
- É **somente leitura**: nada do que está gravado no módulo de memória é
  alterado. A única escrita é a troca de página do hub do SPD, que é estado
  volátil do próprio hub.
- Linha de comando: `TranalSentinel.exe --memoria`

**O TRANAL SAÚDE passou a olhar a memória**
- O app compara o clock que a BIOS configurou com o que o módulo realmente
  oferece e avisa quando a memória está no perfil, no padrão JEDEC ou ajustada à
  mão (acima do perfil do módulo).
- O aviso é informativo quando a memória está no padrão e só vira problema quando
  ela foi mexida à mão.

**Transparência com a licença**
- Os módulos PawnIO usados na leitura são LGPL-2.1. Eles vêm embutidos no
  executável e são extraídos para `%LOCALAPPDATA%\TranalSentinel\PawnIO\`, com a
  licença e o aviso de origem ao lado. Trocar por uma versão mais nova é só
  substituir o arquivo `.bin` nessa pasta.

**O que ainda não entra**
- Os tempos **ativos** (os registradores do controlador de memória do AMD): só o
  que está gravado no módulo é lido.
- **EXPO**: o decodificador existe, mas falta um módulo EXPO para servir de prova.

---

## Novidades da v0.21.1 — o desfazer passou a devolver o serviço de verdade

Correções vindas da terceira revisão adversarial da v0.21 (8 defeitos, com
teste). Nenhuma delas muda o jeito de usar o programa; todas mudam o que
acontece quando você desfaz uma alteração.

- **Serviço que já era manual volta a rodar.** O registro do modo original
  agora guarda também se o serviço **estava rodando**. Antes, desfazer
  devolvia o modo e deixava o serviço parado — o app dizia que tinha
  restaurado, e o serviço continuava parado.
- **O Windows recusou a mudança de modo?** O serviço tinha parado mesmo
  assim. Agora o desfazer percebe que o modo é o mesmo de sempre e volta a
  **ligar** o serviço.
- **A checagem de segurança usa o caminho que o Windows registra**, não o
  texto do arquivo de configuração (que é editável à mão). Um registro
  adulterado apontando para "SysMain" é descartado — e serviço sem caminho
  conhecido também.
- **Restaurar o ASPM não troca mais o seu plano de energia.** O valor volta
  para o plano em que a mudança foi feita, e esse plano só é reativado se já
  era o plano em uso — ativar outro trocaria a escolha de energia sem você
  pedir.
- **Um registro por plano de energia.** Desligar o ASPM em dois planos não
  sobrescreve o registro do primeiro, e desfazer desfaz os dois.
- **"O ASPM já estava desligado"** não vira mais uma alteração registrada:
  o app não inventa uma mudança que não fez (senão o desfazer sobrescreveria
  uma escolha sua).
- **CSV: uma regra só para decimais.** Uma casa vira vírgula nos dois
  caminhos (o número puro e o número dentro de um texto); duas ou mais ficam
  como estão, porque são versão de driver, não medida.
- **Encerrar programa travado confere o processo antes.** O Windows
  reaproveita o número do processo: se o PID já for de outro programa, o app
  não encerra nada.

---

## Novidades da v0.21 — o app passou a agir

O TRANAL SAÚDE até aqui era só leitura: ele dizia o que estava errado e
como resolver, e você resolvia. Agora dá para resolver **sem sair do app** —
e o app passou a vigiar o PC enquanto fica na bandeja.

**TRANAL SAÚDE — ações de um clique**
- Cada achado que tem solução prática ganhou botões: **desligar o ASPM** (o
  gerenciamento de energia do link PCIe, que é a suspeita padrão quando o
  NVMe numa placa adaptadora trava), **abrir a Inicialização**, **abrir os
  Drivers** e **abrir as Otimizações**.
- Toda ação que mexe no sistema **pede confirmação antes** e explica o custo
  ("gasta um pouco mais de energia"). O que foi alterado fica guardado, e
  aparece o botão de **restaurar**.
- A regra do diagnóstico continua sendo pura: ela só descreve a ação; quem
  executa é o executor, que é a única parte que toca no Windows.

**Suspender serviços (estilo "Sleep Mode")**
- O app lista os **serviços de terceiros** que sobem com o Windows, explica o
  que cada um é (atualizador, telemetria, sincronização) e **você escolhe**
  quais suspender. Os que o app reconhece já vêm marcados; os desconhecidos
  aparecem, mas desmarcados.
- Serviço do Windows e serviço de segurança **nunca entram na lista** — nem
  se o app for chamado direto.
- Suspender é mudar o modo para Manual e parar o serviço. **Restaurar tudo**
  devolve cada serviço ao modo que ele tinha.

**Nota de saúde do PC**
- Uma nota de **0 a 100** (conceito de A a E) a partir dos achados, com a
  **tendência** comparando com os dias anteriores. O histórico fica em
  `%AppData%\TranalSentinel\log\tranal-saude-nota.log`.

**Programa travado**
- O app vigia as janelas abertas e avisa quando um programa **para de
  responder** por mais de 30 s (é a mesma pergunta que o Windows faz quando
  mostra o "Não responder"). O aviso aparece na tela e o menu do ícone ganha
  a opção de **encerrar** — a decisão é sua.

**Alertas em segundo plano**
- Temperatura da CPU acima de 85 °C, disco acima de 60 °C e bateria abaixo de
  20 % fora da tomada. O aviso não repete enquanto a condição continua
  valendo, e volta a aparecer se o problema voltar.

**Relatório em CSV**
- O relatório exportado agora sai também em **CSV**, no padrão do Excel em
  português (separador `;`, decimal com vírgula, acentuação correta) — para
  abrir numa planilha e filtrar. Células que o Excel leria como fórmula são
  neutralizadas.

---

## Novidades da v0.20 — o aviso dos sensores passou a culpar a coisa certa

Correções vindas de uma revisão adversarial da v0.19. Nenhuma delas muda o
jeito de usar o programa; todas mudam o que o programa faz quando algo dá errado.

**O motivo da falha não se perde mais**
- Quando um hardware falhava (o driver recusou, o acesso foi negado), o motivo
  ficava só na volta em que aconteceu. Na volta seguinte o hardware já estava
  desligado — e o app voltava a dizer "o driver não respondeu", sem explicar
  nada. Agora o motivo fica guardado e continua aparecendo.
- A falha da CPU foi separada da falha geral da coleta: é ela que interessa
  quando o app pergunta pela temperatura da CPU. O aviso mostra **"Falha na
  coleta: <o motivo real>"** em vez de acusar o driver.

**O erro engolido saiu**
- A coleta engolia a exceção num campo que ninguém lia. Agora o motivo vai
  para o snapshot: o app mostra o que deu errado em vez de continuar
  exibindo as últimas leituras como se fossem de agora.
- O processo de coleta foi isolado do LibreHardwareMonitor por um adaptador
  (`HardwareColeta`): a regra da volta ficou testável sem depender do driver.

**Gravar as configurações não derruba mais o app**
- `Save()` era chamado sem proteção em vários lugares: disco cheio ou arquivo
  bloqueado pelo antivírus derrubavam o programa. Agora a gravação engole o
  erro (perder uma preferência é um incômodo; derrubar o app, não).
- Um número não finito (uma nota de benchmark que deu `NaN`) não estraga mais o
  arquivo de configurações: ele vai para o disco como literal nomeado e volta
  na leitura.

---

## Novidades da v0.19 — os sensores voltaram (e o app parou de culpar o PawnIO)

**O defeito**
- O app mostrava **"O PawnIO está instalado, mas a leitura falhou"** e nenhum sensor
  aparecia, mesmo com o PawnIO instalado e funcionando.
- A causa não era o driver: o LibreHardwareMonitor devolve `infinito` para sensores que
  não sabem o valor (o clock de um núcleo ocioso, por exemplo). O JSON não aceita
  infinito, então a gravação da leitura estourava — e o processo de coleta, que engolia
  o erro, **nunca gravava o arquivo**. Sem arquivo, zero sensores, e o app concluía que o
  driver tinha falhado.

**A correção**
- O valor não finito vira **nulo na origem** (regra única, `SensorValor`), e a
  serialização aceita literal nomeado nos dois lados — assim um arquivo antigo também
  é lido sem derrubar o app.
- Medido nesta máquina: de **0 para 340 sensores lidos** (CPU, GPU, discos, rede).
- O processo de coleta não engole mais o erro: ele grava o motivo no arquivo.

**O aviso passa a dizer a verdade**
- O motivo da falha que derrubou a coleta é gravado separado das falhas de um
  hardware só, e aparece no aviso: em vez de "O driver não respondeu", o menu mostra
  **"Falha na coleta: <o erro de verdade>"** (e a janela CORRIGIR mostra o detalhe).
- Sem elevação ou sem driver instalado, o motivo conhecido continua valendo mais que
  qualquer erro registrado.

**Identidade**
- O cabeçalho do programa mostra **TRANAL SENTINEL** (ainda estava "RESET EXPLODE").
- O relatório exportado e a cópia de segurança da biblioteca de jogos usam o nome novo.

---

## Novidades da v0.18 — o instalador deixa você escolher onde instalar

**Pasta de instalação à escolha**
- A janela do instalador mostra a pasta de destino num **campo editável**: dá para
  digitar o caminho completo e ele é respeitado exatamente como foi escrito.
- O botão `...` abre o **seletor de pasta do Windows**. Ele cria a subpasta
  `Tranal Sentinel` dentro da pasta escolhida (e não cria a subpasta duas vezes
  se você já estiver dentro dela).
- O padrão continua sendo `%ProgramFiles%\Tranal Sentinel`.
- O caminho é conferido antes de instalar: caminho incompleto, raiz da unidade ou
  caractere proibido são recusados com aviso, e o botão INSTALAR fica desabilitado.
- Se já existe uma instalação na pasta escolhida, a tela avisa que ela vai ser
  substituída (o executável atual vira `.bak`).

**Trocar de pasta é uma mudança, não uma cópia**
- Se o TRANAL SENTINEL já estava instalado em **outra** pasta, a tela avisa, e a
  instalação remove os arquivos do lugar antigo **depois** que a nova instalação
  está pronta. Sem isso, o app antigo ficaria órfão, fora do alcance do botão
  Desinstalar.
- Se algo falhar no meio, a instalação antiga continua intacta: a remoção vem
  por último. A tarefa agendada também volta a apontar para a instalação
  antiga, e não para o executável novo que o rollback apagou.
- Se o executável antigo estiver aberto (é o TRANAL SENTINEL rodando), a remoção
  espera o app ser fechado (até 2 minutos). O laço só apaga o arquivo que foi
  agendado: se o lugar for preenchido de novo (reinstalação), o arquivo novo
  fica intacto.
- A tarefa agendada e o atalho da área de trabalho não ficam órfãos: com a
  opção desligada, a instalação nova não os recria, então eles são removidos
  em vez de continuarem apontando para o executável antigo.

**Caminhos recusados**
- Pasta de rede (`\\servidor\pasta`, e também `//servidor/pasta`): o app, a
  tarefa agendada e o desinstalador dependeriam da rede estar montada. A
  checagem é feita depois de normalizar o caminho, então não escapa com barra
  normal.
- Caminho do Windows (`\\?\C:\...`, `\\.\C:\...`).
- Raiz da unidade (`C:\`), caminho incompleto, caractere que o Windows não
  aceita e caminho cujo executável passaria de 259 caracteres (o limite do
  MAX_PATH).

**A desinstalação acha o app onde ele está**
- O local instalado fica gravado na entrada de desinstalação (`InstallLocation`) e
  a desinstalação lê esse valor — ou seja, ela funciona mesmo se o app estiver em
  `D:\...` e não na pasta padrão.
- Se essa informação não existir, ela usa a pasta de onde o próprio instalador
  está rodando.

**Linha de comando**
- `TranalSetup.exe --instalar <payload> --destino <pasta>` instala em modo
  silencioso na pasta escolhida.
- `TranalSetup.exe --simular --instalar <payload> --destino <pasta>` mostra o
  plano completo com o destino novo, sem alterar nada.

**Correção**
- As mensagens de erro do instalador saíam com os acentos quebrados quando a saída
  era capturada em arquivo ou pipe (o `stderr` não recebia a codificação do
  `stdout`).

## Novidades da v0.17 — o app vira assistente diário

**Nova página · TRANAL ASSISTENTE › Linha do tempo**
- O app tira um **retrato** do PC de tempos em tempos e compara com o anterior.
  O retrato cobre: versão do Windows, BIOS/UEFI, driver de vídeo, driver de rede,
  plano de energia, memória (módulos, total e velocidade), configurações que
  afetam desempenho (Modo de Jogo, Game DVR, HAGS, Integridade de memória, VBS,
  Inicialização Rápida), programas instalados e itens de inicialização.
- A linha do tempo mostra **o que mudou**, com data e hora: driver atualizado,
  programa instalado ou desinstalado, versão trocada, plano de energia mudado.
- Botão **TIRAR RETRATO AGORA** para fotografar o PC na hora.
- Snapshot anterior guardado em `%AppData%\TranalSentinel\historico\instantaneos.json`.

**Diário diário**
- Um arquivo de log por dia em `%AppData%\TranalSentinel\log\tranal-AAAA-MM-DD.log`.
- Registra abertura do app, perfil escolhido, mudanças detectadas, atualização
  disponível e demais eventos. Mantém 30 dias.

**Ícone na bandeja**
- O app passa a ter ícone ao lado do relógio e vive ali. **Fechar a janela não
  fecha o app** — ele continua acompanhando o PC, e um aviso explica isso.
- Menu do ícone: *Abrir o TRANAL SENTINEL* e *Sair*.
- Implementado com P/Invoke direto (`Shell_NotifyIcon`), sem Windows Forms, para
  não misturar os dois frameworks no mesmo projeto.

**Instalador próprio (`TranalSetup.exe`)**
- Pede administrador e instala em `%ProgramFiles%\Tranal Sentinel`.
- Cria atalho no menu Iniciar e (opcionalmente) na área de trabalho.
- Registra a desinstalação no Painel de Controle; desinstalar remove os arquivos,
  os atalhos e o registro, e **não** apaga as suas configurações.
- **Inicia junto com o Windows** por tarefa agendada (sem prompt de UAC a cada boot).
- Modo `--simular` mostra o que seria feito sem mexer em nada.

**Atualização automática**
- O app confere a API do GitHub do próprio projeto e avisa quando há versão nova.
- O banner fica na lateral, com o tamanho do download e barra de progresso.
- O arquivo é conferido pelo tamanho exato da release: download cortado não
  vira executável.
- A troca do executável acontece quando o app fecha: o Windows não deixa
  sobrescrever um .exe em uso, então o app chama a si mesmo com `--atualizar` e
  esse processo espera o app atual sair (com backup e rollback).

**Perfil de recursos**
- **Completo**: sensores a cada 1 s, retrato a cada 3 h, overlay e análise de
  saúde rodando sozinhos.
- **Equilibrado** (padrão): sensores a cada 2 s, retrato a cada 6 h.
- **Economia**: sensores a cada 5 s, retrato a cada 24 h, overlay e análise
  automática desligados.
- O perfil muda o intervalo da leitura no **coletor** (não é só texto na tela):
  o que pesa é interrogar a placa.

**Diagnóstico pela linha de comando**
- `TranalSentinel.exe --saude coletar` grava um snapshot JSON + TXT.
- `TranalSentinel.exe --saude comparar a.json b.json` mostra o que sumiu, o que
  apareceu e o que continua.
- `TranalSentinel.exe --saude relatar` imprime o relatório.
- Usa exatamente as mesmas regras da aba TRANAL SAÚDE (nenhuma duplicação).

**Custo real medido** (60 s, app + coletor, 16 núcleos): 0,215 % da CPU da
máquina e ~364 MB de RAM. A leitura do hardware é o custo; o runtime não é.
Por isso a resposta a "reescrever em Rust?" foi medir, não palpite — e mexer no
intervalo da leitura, não na linguagem.

---

## Novidades da v0.16

**Nova página · TRANAL FERRAMENTAS › Inicialização**
- Lista os programas que abrem junto com o Windows: chaves Run do registro (usuário, todos os usuários, 32 e 64 bits)
  e pastas Inicializar, com descrição, fabricante e aviso quando o arquivo do programa não existe mais.
- Interruptor para ativar/desativar, igual ao Gerenciador de Tarefas: a entrada continua lá e pode ser religada.
- Botão **REMOVER** com confirmação; o programa continua instalado e tudo pode ser desfeito em **Restaurar removidos**.

**Nova página · TRANAL FERRAMENTAS › Instalar apps**
- Os essenciais depois de formatar o PC: Steam, Google Chrome, Discord, Lightshot, 7-Zip, WinRAR e VLC.
- Instala pelo winget (instalador de pacotes da Microsoft que já vem no Windows): versão oficial mais recente,
  sem janelas, um app de cada vez, com o resultado de cada um na lista.
- Mostra o que já está instalado; se o winget ainda não existir (PC recém-formatado), um botão abre a Microsoft Store
  para atualizar o "Instalador de Aplicativo".

**Nova categoria · TRANAL MAKER** (Arduino, ESP32 e conversores USB-serial)
- **Portas COM**: a limpeza de dispositivos fantasmas e de números COM presos saiu de Otimização e ganhou página própria.
- **Drivers seriais**: diagnóstico das placas com CH340/CH341, CP2102/CP210x, FTDI, Prolific PL2303 e Arduino
  (sem driver, código 10, código 43, FTDI falsificado travado em PID 0000), com a orientação de cada caso.
- **REINSTALAR** remove o dispositivo e faz o Windows detectar de novo com o melhor driver.
- Lista as versões de driver guardadas no Windows para cada chip, com **REMOVER** (backup antes): volta para uma versão
  anterior quando a nova não funciona com clones, ou desinstala de vez. **Restaurar removidos** desfaz.
- **Drivers incluídos no app**: CH340 4.0, CP210x 11.6, FTDI 2.12.36.20 e Arduino, oficiais e assinados, instalam sem internet
  (ideal logo depois de formatar), com a placa conectada ou não. Também abre a página oficial de cada fabricante.
- **Arduino IDE**: instalar/atualizar pelo winget, fechar programas que prendem a porta COM e limpar o cache de índices
  quando a IDE trava em "Downloading index".

**Novo benchmark · Ray tracing (GPU)**
- Teste em Direct3D 12 com DXR 1.1 que usa os núcleos de ray tracing (RTX 20+, Radeon RX 6000+, Intel Arc): 4 mil objetos,
  reflexos de reflexos, sombras suaves, oclusão de ambiente e iluminação indireta, com a cena refeita a cada frame.
- Feito para comparar com confiança: aquecimento sem medir, 3 passadas iguais, nota pela mediana e variação entre passadas
  (selo de "resultado confiável" abaixo de 3%); tempo de frame medido pelo relógio da própria GPU.
- Presets Normal, Alta e Extrema em 1080p, 1440p e 4K; histórico comparando só resultados do mesmo preset e resolução.
- Referência: RX 9070, Alta 1440p = 878 pts (87,8 FPS), variação 0,5%.

**Overlay personalizável**
- Na aba Overlay, escolha o que aparece: FPS, frametime, 1% low, gráfico; GPU (uso, temperatura, hotspot, clock do núcleo,
  clock da memória, consumo, tensão, ventoinha, limitador NVIDIA); VRAM (usada, %, temperatura); CPU (uso, núcleo mais
  ocupado, temperatura, clock médio, clock máximo, consumo); RAM (GB, %); nome do jogo e hora. Atalhos SÓ FPS, PADRÃO e TUDO.
- Cada componente vira uma linha só com os itens escolhidos; a escolha fica salva e vale na hora.

**TRANAL JOGOS**
- Novo atalho em Extras: **-allow_third_party_software** (CS2), que libera captura de jogo do OBS, ReShade e overlays
  bloqueados pelo modo confiável; o aviso sobre o Fator de Confiança aparece ao passar o mouse.

**Abertura mais rápida**
- A janela aparece em ~0,5 s (antes ~4,6 s). Informações do sistema, sensores e placa de vídeo carregam em paralelo,
  em segundo plano, e cada página é preenchida quando a sua parte fica pronta.
- O aviso "sem sensor da CPU" não aparece mais por engano enquanto o hardware ainda está sendo detectado.

**Correção · Overlay de jogos**
- O overlay agora fica por cima de jogos em janela sem borda que também se marcam como "sempre no topo"
  (ex.: GTA V e Cyberpunk 2077), inclusive depois de Alt+Tab.
- Aparece no monitor onde o jogo está, não só no monitor principal.
