# Pesquisa de mercado — o que existe em volta do TRANAL SENTINEL

Este documento registra o que foi procurado no mercado (agosto/2026), o que os
concorrentes fazem melhor que o TRANAL SENTINEL e o que foi **adotado** aqui.
O objetivo não é copiar ninguém: é saber onde o mercado deixou uma brecha.

## O cenário

A categoria "otimizador de PC" no Windows é dividida em duas famílias que
quase não se cruzam:

**A) Otimizadores que mexem no sistema.** Wise Game Booster, Razer Cortex,
AVG TuneUp, CCleaner, IObit Uninstaller, Ashampoo WinOptimizer. Fazem
"limpeza", desativam serviços, editam registro, desligam o Windows Update,
removem programas. O posicionamento é de ferramenta de ação: você clica e
acontece alguma coisa.

**B) Observadores que explicam.** HWiNFO, CrystalDiskInfo, Speccy, AIDA64,
Sysinternals. Medem, grafam, avisam quando algo foge do normal. Não mexem em
nada. O posicionamento é de instrumento de medição.

O **TRANAL SENTINEL** é, historicamente, um híbrido: tem as ferramentas de ação
do grupo A (TRANAL OTIMIZA, TRANAL MAKER, TRANAL FERRAMENTAS) e os sensores do
grupo B (TRANAL BENCH, aba de sensores). A aba TRANAL SAÚDE já era uma terceira
coisa: um diagnóstico **somente leitura** que explica *por que* o PC trava.

## Os concorrentes mais próximos

| Software | O que faz | Referência |
|---|---|---|
| **RigSight** (open source) | Temperaturas, uso de apps, crashes e disco explicados em português claro. Grava um relatório por dia. Ícone na bandeja com "ponto de saúde". Timeline do que mudou. | github.com/b0llu/RigSight |
| **Kinetik Stats** | Monitor na barra de tarefas com tela por app, tempo de tela por dia, **timeline do que mudou** (drivers, updates, programas, BIOS, plano de energia, Game Mode, HAGS) detectada comparando snapshots a cada 3 h, e travamentos dos últimos 60 dias explicados. | github.com/aranys/kinetik |
| **Pulse Diagnostics** | Só leitura do log de eventos → linguagem simples. Timeline viva, incidentes correlacionados, relatório de saúde, exportação JSON/CSV/HTML/PDF, e um assistente de IA **local** (Ollama/LM Studio, só localhost). Roda como serviço com um prompt de UAC. | github.com/Pulse-Diagnostics/Pulse |
| **Sysoptimizer** (Protagonist Labs) | Debloat do Windows 11 com desfazer, 17 ajustes mostrando quais já estão ativos, histórico segundo a segundo, tarefas agendadas (diária/semanal), **relatório semanal toda segunda**, alertas pela bandeja, exportação CSV. | github.com/protagonistlabs/sysoptimizer |
| **Cpu-Pets** | Fica na bandeja: análise de produtividade em SQLite por app e por dia, lembretes, **detecção de app travado com fechar à mão**, histórico de saúde do app, nota de saúde com crashes e reinícios vindos do log, e **saúde da bateria** lendo o XML do powercfg. | github.com/cosmotet/cpu-pets |
| **SysPerf** (Microsoft Store) | Aprende o que é normal **para o seu PC** (linha de base própria), nota de desempenho contra metas, análise de causa raiz, tendência de até um ano, e **fecha de verdade** os apps de bandeja depois que a janela é fechada (pedindo antes). | apps.microsoft.com |
| **Microsoft PC Manager** | PC Boost, Health Check, Manage Storage, Pop-Up Management e uma barra compacta que mostra o status; inicia com o Windows e fica na bandeja. | apps.microsoft.com |
| **Today on PC** | "Today Card" diário, privacidade primeiro, tudo local, exporta PNG/Markdown. | github.com/mdrisse/Today-on-PC |
| **AVG TuneUp (Sleep Mode)** | Pausa os serviços em segundo plano de programas que não estão em uso até o programa ser aberto. | avg.com |
| **Razer Cortex / Wise Game Booster** | Suspendem processos e serviços ao abrir o jogo e restauram ao sair. | razer.com |

## A brecha que existe

Três coisas que **nenhum** dos concorrentes faz ao mesmo tempo:

1. **Em português.** Todos os acima são em inglês. A maior parte do mercado
   brasileiro de otimização é ou falsa (scanner de registry) ou ferramenta genérica em
   inglês com tutorial no YouTube.
2. **Diagnóstico somente leitura, com a explicação do *porquê*.** Os
   otimizadores do grupo A alteram o sistema; a maioria dos observadores do
   grupo B só mostra o número. O TRANAL SAÚDE já tinha a peça que faltava:
   correlacionar evento do Windows com causa provável.
3. **Não morrer.** O LibreHardwareMonitor é famoso por derrubar o processo que
   o hospeda (foi exatamente o que matou o RESET EXPLODE em 04/11/2026, num
   access violation dentro do `AmdGpu.Update()`). Nenhum concorrente isola a
   coleta de sensores em processo próprio — o TRANAL SENTINEL já faz.

## O que foi adotado (e está neste repositório)

| Ideia | De onde | Como ficou aqui |
|---|---|---|
| **Timeline do que mudou** | Kinetik Stats, RigSight | Aba TRANAL ASSISTENTE. O app tira um "retrato" do PC (Windows, BIOS, drivers, plano de energia, memória, programas instalados, inicialização) e compara com o anterior. Driver novo, programa instalado, plano trocado: tudo anotado com data e hora. |
| **Relatório diário** | RigSight, Sysoptimizer | Registro diário em `%AppData%\TranalSentinel\log\tranal-AAAA-MM-DD.log`, uma linha por evento, 30 dias. Aparece na aba do assistente. |
| **Ícone na bandeja com o app vivo** | RigSight, Cpu-Pets, CrystalDiskInfo | Ícone de bandeja em P/Invoke puro (`Bandeja.cs`). Fechar a janela esconde o app; ele continua acompanhando. |
| **Ajuste com desfazer e "o que já está ativo"** | Sysoptimizer | TRANAL OTIMIZA já guarda o valor original de cada ajuste. A tela do assistente mostra o estado atual do PC. |
| **Baseline do próprio PC** | SysPerf | TRANAL BENCH já mede a máquina. A base para "o que é normal para este PC" já existe (falta correlacionar). |
| **Modo economia** | (ideia própria) | Perfil de recursos: Completo / Equilibrado / Economia. Economia lê os sensores a cada 5 s em vez de 1 s, reduz o histórico de 3 h para 24 h e desliga overlay e análise automática. |
| **Comparação entre execuções** | Sysoptimizer | CLI `--saude comparar`: duas execuções do diagnóstico viram um diff do que sumiu, apareceu e continua. |
| **Isolamento da coleta em processo** | (ideia própria, já existia) | O coletor roda em processo filho e o app não morre se ele morrer. |
| **Suspender serviços de segundo plano** | AVG TuneUp Sleep Mode | Janela "SUSPENDER SERVIÇOS": lista os serviços de terceiros, explica o que cada um é e deixa o usuário escolher. Os que o app reconhece vêm marcados. Serviço de segurança nunca entra. |
| **Detecção de programa travado** | Cpu-Pets | A vigia do app pergunta às janelas abertas se elas respondem (é a mesma pergunta do "Não responder" do Windows) e oferece encerrar pelo menu do ícone da bandeja. |
| **Nota de saúde** | SysPerf | Nota de 0 a 100 (conceito A–E) calculada a partir dos achados, com tendência comparando com os dias anteriores. Histórico em `%AppData%\TranalSentinel\log\tranal-saude-nota.log`. |
| **Ações de um clique** | (continuação do TRANAL SAÚDE) | A regra do diagnóstico descreve a ação; o executor aplica. Cada ação que mexe no sistema pede confirmação e tem como desfazer. |
| **Alertas em segundo plano** | (ideia própria) | Temperatura da CPU, do disco e carga da bateria, com aviso que não repete enquanto a condição continua valendo. |
| **Relatório em CSV** | (ideia própria) | O relatório exportado sai também em CSV no padrão do Excel em português, para abrir em planilha e filtrar. |
| **SPD e timings de memória** | CPU-Z, HWiNFO | Aba TRANAL MEMÓRIA: lê o SPD de cada módulo pelo SMBus (driver PawnIO) e mostra fabricante, part number, serial, capacidade, ranks, os tempos do padrão JEDEC e o perfil XMP/EXPO gravado no módulo. O TRANAL SAÚDE compara o clock configurado na BIOS com o que o módulo oferece: perfil ativo, padrão JEDEC ou overclock manual. |

## O que ficou na lista (não cabe nesta versão)

- **Assistente de IA local** (Pulse Diagnostics): rodar um modelo local
  (Ollama/LM Studio) sobre o relatório, com a mesma promessa de privacidade —
  nada sai da máquina. É o próximo passo natural, porque o TRANAL SAÚDE já
  produz o texto que ele responderia. **Ficou de fora por decisão do dono.**
- **Comparar benchmarks entre PCs**: precisa de um formato de intercâmbio e de
  uma tela de importação — é trabalho de produto, não de uma versão.
- **Leitura de SPD / Super I/O**: **entrou na v0.22** (ver abaixo). O que
  ficou de fora: os *timings ativos* (registradores do controlador de
  memória do AMD) e o instalador do driver PawnIO embutido no app.
- **Assinatura digital do instalador**: exige certificado pago.

## Por que continuar em C#

A pergunta "precisa reescrever em Rust ou Go?" foi respondida com medição, não
com palpite. App e coletor rodando, 60 segundos de janela:

| | CPU (de 1 núcleo) | CPU (da máquina, 16 núcleos) | RAM (working set) | RAM privada |
|---|---|---|---|---|
| App principal | 2,06 % | 0,129 % | 254 MB | 163 MB |
| Coletor de sensores | 1,38 % | 0,086 % | 109 MB | 50 MB |
| **Total** | **3,4 %** | **0,215 %** | **364 MB** | **213 MB** |

A leitura de hardware é o que pesa, e ela vem do LibreHardwareMonitor: o custo
está na interrogação da placa, não no runtime. Reescrever em Rust trocaria
pouco de CPU e cortaria a memória (o WPF sozinho já segura ~150 MB). Rust ou Go
também exigiriam reescrever a interface inteira, o que é caro e arriscado sem
ganho proporcional.

O que dá para ganhar de verdade é o que já foi feito: **ler menos vezes**
(perfil de recursos) e **não estar no caminho da UI** (coletor em processo
separado, leitura em thread de fundo). Com 16 núcleos, 0,215 % da máquina é
irrelevante.
