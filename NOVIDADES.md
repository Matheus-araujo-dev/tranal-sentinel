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
