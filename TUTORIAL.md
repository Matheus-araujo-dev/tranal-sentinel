# TRANAL SENTINEL — tutorial do app

Guia de leitura do app: **o que cada aba analisa, o que ela faz e o que você
ganha com ela**. O TRANAL SENTINEL é um assistente de performance de PC
(diagnóstico de travamentos e lentidão, sensores em tempo real, benchmarks e
linha do tempo do que mudou na máquina).

Duas regras valem em toda a tela, e valem mais ainda depois de ler este texto:

- **O TRANAL SAÚDE só lê.** Ele olha o histórico do Windows e diz o que está
  errado. Nada é mudado sem você confirmar.
- **O que o app mexe, ele guarda para desfazer.** Cada ajuste feito pelo app
  fica registrado no `TweakBackup` e tem o botão **Restaurar originais**.

> Dica de leitura: em toda a tela, o **hover** (o mouse parado sobre o item)
> explica o que aquele item faz. Este tutorial é a versão longa da mesma
> explicação.

---

## Como se navega

A janela tem duas partes:

- **À esquerda**, o menu por categorias. As categorias são os botões grandes
  (`TRANAL SENTINEL`, `TRANAL SAÚDE`, `TRANAL MEMÓRIA`, `TRANAL ASSISTENTE`,
  `TRANAL JOGOS`, `TRANAL DESEMPENHO`, `TRANAL FERRAMENTAS`, `TRANAL MAKER`).
  Clicar no grupo abre a lista de páginas dentro dele.
- **À direita**, a página aberta. A primeira é sempre a **Visão geral**.

A janela pode ser fechada sem parar o app: ele continua na bandeja do sistema
(perto do relógio) e continua olhando o PC.

---

## Visão geral

A primeira página é o painel do dia: temperatura e CPU em gráfico, GPU ao lado,
os números do PC e os atalhos para o resto. Serve para responder "como está a
máquina agora?" sem entrar em nenhuma página.

O gráfico aceita 1 minuto, 5 minutos, 15 minutos ou 1 hora de janela, e mostra
o que mudou no intervalo.

---

## TRANAL SENTINEL — o estado da máquina

### Sistema

Hardware e software detectados: Windows, placa-mãe, BIOS, CPU, memória por
módulo, GPU e discos. É a referência do que a máquina **tem** — compare com o
que você acha que ela tem.

### Sensores

Temperatura, uso, clocks, consumo e rotação de tudo, em tempo real. Os
sensores são lidos pelo LibreHardwareMonitor, que chama código nativo do driver
da GPU — por isso a coleta roda **num processo separado**: se o driver devolver
lixo, morre só o coletor, não o app. Se isso acontecer, a lateral mostra um
aviso e os outros sensores continuam funcionando.

### Ventoinhas

O RPM de cada ventoinha comparado com o **recorde dela própria**, não com o
número de referência do fabricante. É o que diz se a ventoinha está gasta: uma
ventoinha nova do mesmo modelo costuma girar mais.

### Placa de vídeo

Especificações completas e sensores da GPU em tempo real (temperatura, uso,
memória de vídeo, clocks, consumo).

### Discos

Saúde, desgaste e temperatura de cada disco. Cruza as três fontes que o
Windows dá: atributos do S.M.A.R.T., SMART/Health e a temperatura real.

### Energia

Bateria, fonte de energia e consumo. Mostra o plano de energia em uso e o que
cada dispositivo está dormindo.

### Rede

Diagnóstico de lag: **descubra se o lag vem do seu PC, do Wi-Fi ou do
provedor**. O teste leva cerca de 30 segundos e mede perda e latência por etapa
(até o DNS, até o roteador, até a internet). Também mostra o **tráfego de
internet por programa**: quem usou a internet, quanto e em que dia. O
histórico fica neste computador e nada sai daqui.

---

## TRANAL SAÚDE — por que o PC trava

### Diagnóstico

A página que responde *"por que meu PC trava e o que está deixando ele
lento?"*. Ela varre o **histórico** do Windows (não o estado atual) e aponta as
causas prováveis, cada uma com a ação sugerida:

- **Travamentos**: eventos Kernel-Power 41 nos últimos 90 dias, com data,
  bugcheck e se o botão de energia foi apertado. Sem bugcheck e sem minidump =
  congelamento total, que aponta hardware/firmware em vez de programa.
- **Erros de hardware**: eventos WHEA, telas azuis (minidumps) e resets de
  driver de vídeo.
- **PCIe / ASPM**: gerenciamento de energia do link PCIe (a causa clássica de
  NVMe que não volta depois de reiniciar) e se o NVMe está atrás de um
  adaptador M.2→PCIe.
- **Discos**: NVMe fora do ar e dispositivos de armazenamento órfãos.
- **Segurança**: estado real do antivírus, filtro de kernel de terceiros, idade
  das definições e consumo de RAM.
- **Lentidão**: compressão de memória, RAM acima do spec, inicialização
  automática, serviços de terceiros e Hyper-V/VBS.

No fim há uma **nota de saúde de 0 a 100** (conceito de A a E), com a tendência
comparando com os dias anteriores.

Cada achado que tem solução prática ganhou um botão (desligar o ASPM, abrir a
Inicialização, abrir os Drivers, abrir as Otimizações). Toda ação que mexe no
sistema **pede confirmação**, explica o custo, fica registrada no diário e
**tem como desfazer**. Serviço de antivírus, Defender e serviço do Windows
nunca são tocados.

Nesta mesma página mora a **vigia de tecla presa**: quando o Windows continua
achando que Shift, Ctrl, Alt ou Win está pressionado depois que você solta, o
app solta a tecla, avisa e registra no diário. Só age com as três condições ao
mesmo tempo (tecla caída há 30 s, teclado quieto há 30 s e repetição
automática do Windows ligada) — e a correção automática pode ser desligada aqui.

### Alertas

As regras que o app vigia em segundo plano: você escolhe **qual sensor**, **o
limite** e **quanto tempo** a condição precisa valer antes do aviso. Sem regra,
o app não avisa de nada.

### S.M.A.R.T.

A tabela de atributos de cada disco: id, nome, flags, valor, pior, limiar e
valor cru. É o que mostra quanto da vida útil do disco já foi gasto. O botão
**LER AGORA** lê: a leitura só informa, **nada é enviado ao disco**.

---

## TRANAL MEMÓRIA — o que tem dentro dos módulos

### Módulos

O que cada módulo de memória tem dentro: capacidades, tempos e perfis de
overclock, lidos pelo SMBus. **Nada do que está gravado na memória é
alterado.**

A coluna de tempos ativos usa o clock do controlador e a do perfil usa o clock
do perfil: a comparação é em **ciclos**, porque é assim que a BIOS faz — quando
ela sobe o clock, mantém a contagem de ciclos do perfil.

---

## TRANAL ASSISTENTE — o que mudou no seu PC

### Linha do tempo

O que mudou no PC desde a última vez que você olhou. O app tira um retrato de
tempos em tempos (Windows, BIOS, drivers, plano de energia, memória, programas
instalados, itens de inicialização) e anota **a diferença** entre um retrato e
o outro, com data e hora. Driver novo, programa instalado, plano de energia
trocado: fica registrado.

### Uso

Que programa ficou em frente na tela, quanto tempo, e **quem estava na frente
quando a máquina ficou mais quente**. O app anota **só o nome do programa e o
título da janela** — nunca a imagem da tela, o conteúdo dos arquivos ou o que
foi digitado — e **nada sai do computador**. O histórico fica 30 dias.

### Painel

Uma janela pequena com CPU, GPU, RAM e ventoinha que você arrasta para onde
querer. O botão × só esconde o painel: o TRANAL SENTINEL continua rodando na
bandeja.

---

## TRANAL JOGOS — jogar melhor

### Biblioteca

O inicializador de jogos: ele procura seus jogos, e para cada um você escolhe
prioridade do processo, argumentos de inicialização (inclusive os de **gráficos**),
taxa de atualização e **programas auxiliares** (as ferramentas de terceiros que
abrem junto com o jogo).

### FPS estimado

O FPS esperado no seu PC, com opção de considerar **upscaling** (DLSS / FSR).

### Gargalo

Descobre o que está limitando o seu FPS: **GPU, CPU, memória ou temperatura**.
A estimativa vem do desempenho relativo típico; o **detector ao vivo** mostra a
realidade de cada jogo. Clique em iniciar, abra o jogo e jogue normalmente — ao
final você recebe o diagnóstico.

### Overlay

FPS, frametime e sensores **por cima do jogo**, em janela sem borda.

---

## TRANAL DESEMPENHO — medir

### Benchmark

Sete testes, cada um medindo uma peça: CPU por renderização 3D (path tracing,
no mesmo princípio do Cinebench), cena de jogo na GPU, ray tracing por
hardware (DXR 1.1), simulação de jogo (CPU), memória e carregamento, e disco.

### Stress test

Carga máxima com **corte automático por temperatura** — ele desliga sozinho
quando o hardware não aguenta.

### Overclock

Ajustes da GPU, verificador de estabilidade e guia de overclock na BIOS.

### Comparar

Leva os resultados de benchmark de um PC para o outro. No PC de origem, clique
em **EXPORTAR** e guarde o arquivo; no outro PC, clique em **IMPORTAR**. A
tabela mostra os testes que existem nos dois PCs e avisa quando as máquinas
são diferentes demais para a comparação valer.

---

## TRANAL FERRAMENTAS — mexer no Windows

### Otimização

Libera memória, fecha o que atrapalha e ajusta o Windows para jogos. Cada
ajuste é individual e vem com **Restaurar originais**.

Desde a v0.34 entram aqui dois ajustes novos:

- **Rolagem do navegador** — o Windows guarda quantas linhas a roda do mouse
  rola por entalhe. Se esse valor estiver em 0, o navegador (Chrome, Edge,
  Opera) multiplica o movimento da roda por zero e a página **não rola**. O
  app mede esse valor, avisa se estiver quebrado e o conserta (3 linhas e 3
  caracteres por entalhe), guardando o valor anterior para desfazer.
- **Alt+Tab do Windows 11** — nas versões 25H2 e mais novas, a Microsoft removeu
  a tela clássica do Alt+Tab. Com a opção antiga ligada, o Windows troca de
  janela **sem desenhar nada**. O app confere a opção, avisa e liga o
  switcher moderno. Esse ajuste precisa que o Explorer reinicie para valer —
  o app avisa isso em vez de reiniciar o Explorer sozinho (reiniciar fecha as
  pastas que você tem abertas).

### Inicialização

Os programas que abrem junto com o Windows: você vê, desativa ou remove o que
não precisa — com opção de desfazer.

### Instalar apps

Os essenciais depois de formatar o PC, pelo winget, em silêncio e na versão
oficial mais recente: Steam, Chrome, Discord, Lightshot, 7-Zip, WinRAR e VLC.

### Drivers

Verifique, baixe e instale drivers. NVIDIA e AMD automáticos; os de
fabricantes vão pela página oficial. O botão de **exportar** salva todos os
drivers de terceiros em `Documentos\TranalSentinel-Drivers` — útil antes de
formatar ou de trocar um driver.

### Integração

Iluminação, efeitos e o visor do cooler:

- **Corsair iCUE**
- **OpenRGB** (MSI Mystic Light, Gigabyte RGB Fusion, ASUS Aura, RAM, Razer,
  Logitech e outras)
- **Razer Chroma** (em teste)
- **VISOR KALKAN** — mostra no visor do cooler Kalkan a temperatura do CPU, a
  rotação da ventoinha e a carga do processador, sem o software da Kalkan
  instalado e sem a DLL dele. A comunicação é USB HID nativo: o handshake, o
  quadro e o embaralhador são a implementação própria do protocolo.

---

## TRANAL MAKER — placas e conversores seriais

### Portas COM

Arduino, ESP32 e conversores seriais: remove dispositivos fantasmas e libera
números COM presos.

### Drivers seriais

CH340, CP2102/CP210x, FTDI, Prolific e Arduino — diagnóstico, reinstalação e
troca de versão do driver. Os drivers oficiais de cada fabricante já vêm
dentro do TRANAL SENTINEL: instalam sem internet, ideal logo depois de
formatar.

---

## A vigia em segundo plano

Com a janela fechada, o app continua na bandeja e avisa quando:

- um programa **para de responder** por mais de 30 s (o menu do ícone ganha a
  opção de **encerrar**);
- a **temperatura da CPU** passa de 85 °C, a do **disco** de 60 °C;
- a **bateria** está acabando (20 % fora da tomada);
- uma **tecla fica presa** (Shift, Ctrl, Alt ou Win).

Cada aviso passa a dizer **por que disparou**: o que os sensores faziam na
janela antes do disparo e qual programa estava em frente na hora. Sem
histórico, o app diz que não sabe — número inventado é pior que número
ausente.

---

## Onde o app guarda as coisas

| O quê | Onde |
|---|---|
| Configurações | `%AppData%\TranalSentinel\settings.json` |
| Diário (um arquivo por dia) | `%AppData%\TranalSentinel\log\tranal-AAAA-MM-DD.log` |
| Nota de saúde por dia | `%AppData%\TranalSentinel\log\tranal-saude-nota.log` |
| Histórico de uso (SQLite) | `%LOCALAPPDATA%\TranalSentinel\historico\historico.db` |
| Instantâneos do diagnóstico | `%AppData%\TranalSentinel\saude\` |
| Log do instalador | `%ProgramData%\Tranal Sentinel\instalador.log` |

---

## Pela linha de comando

O mesmo diagnóstico, sem abrir a janela:

```
TranalSentinel.exe --saude coletar      # grava um snapshot JSON + TXT
TranalSentinel.exe --saude comparar a.json b.json   # o que sumiu, apareceu e continua
TranalSentinel.exe --saude relatar      # só o relatório em texto
TranalSentinel.exe --banco              # estado, tamanho e o que tem dentro do banco
TranalSentinel.exe --banco --reparar --confirmar
TranalSentinel.exe --sensores           # relatório do processo de coleta
TranalSentinel.exe --kalkan-teste       # teste do visor Kalkan
```

---

## O que o app **não** faz

- Não inventa número: quando não dá para saber, ele **diz que não deu para
  saber**.
- Não mexe em serviço de segurança — nem pela lista, nem se for chamado
  direto.
- Não altera nada por conta própria: suspender serviço é sempre escolha sua. A
  **vigia da tecla presa** é a exceção explícita, e pode ser desligada na
  página do TRANAL SAÚDE.
- Não envia nada para a internet: o histórico de uso e o diagnóstico ficam
  neste computador.
