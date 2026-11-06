# Auditoria dos concorrentes — o que o mercado faz e o que o TRANAL SENTINEL não faz

Documento de projeto. Data da auditoria: **06/11/2026**. Esta é a segunda
auditoria: a primeira (agosto/2026) está resumida em `MERCADO.md`.

## O método (e o que ele não cobre)

A fonte foi o **artefato publicado** dos concorrentes, não o README:

| Concorrente | Artefato analisado | O que foi feito |
|---|---|---|
| **RigSight** (open source, MIT, C#) | `Rigsight-Setup-1.6.1.exe` (57,8 MB, publicado em 06/11/2026) e `Rigsight-Language-es.json` (472 KB, o arquivo de idioma da mesma versão) | REA no instalador (imports, resources) e leitura completa das **5.517 strings** do artefato publicado — que é a superfície inteira do app, no idioma do usuário |
| **Revo Uninstaller 2.7.0** | `RevoUnin.exe` instalado nesta máquina (21,1 MB, x86-64, nativo) | REA nos imports e nos resources (impressão digital de capacidades) |
| **HWiNFO64 / CPU-Z / CrystalDiskInfo** | — | **não analisados**: o download direto do HWiNFO exige formulário (HTTP 403) e o do CPU-Z devolveu página HTML em vez do zip. Ficam como pendência |

**Limites honestos desta auditoria:**

- O instalador do RigSight é **Inno Setup com loader revisão 2**, que o
  `innoextract` 1.9 (a última versão) não lê ("Setup loader checksum
  mismatch"). As assemblagens .NET do concorrente **não foram
  descompiladas**: o inventário de recursos vem do arquivo de idioma e do
  README publicados, não do código do app.
- Dos **11 concorrentes** listados no MERCADO.md, só o RigSight existe de
  verdade. Os  outros (Kinetik Stats, Pulse Diagnostics, Sysoptimizer, Cpu-Pets, Today on
  PC) devolveram 404 na API do GitHub — são nomes da pesquisa de mercado de
  agosto/2026, sem artefato para auditar. Por isso a auditoria de hoje se
  apoia num concorrente real e verificável.
- O REA do instalador do RigSight mediu o **loader do Inno Setup** (150
  imports: `kernel32`, `user32`, `advapi32`, `oleaut32`, `comctl32`,
  privilégios de token e registro), não o app que ele instala. Serve como
  prova de que o artefato é um instalador Inno e de como ele obtém
  elevação — não como prova de nenhuma capacidade do app.

## O que o concorrente faz que nós não fazemos

Do inventário de strings do artefato publicado (contagem por família de
funcionalidade):

| Família | No RigSight | No TRANAL SENTINEL | Decisão |
|---|---|---|---|
| Tempo de tela e sessões | 33 strings ("SESSION SUMMARY", "SESSION PEAK", "Which apps", `Sessions.csv`) | **não existe** | **adotado na v0.27** |
| "Quem fez o calor" (programa × temperatura) | sim (família de sensores, 287 strings) | **não existe** | **adotado na v0.27** |
| "Por que meu alerta disparou?" | sim (notificações, 32 strings) | o alerta avisa, mas não explica | **adotado na v0.27** |
| Tráfego de internet por app | 228 strings ("INTERNET DROP", downloads, banda) | não existe | fica na fila (ETW é caro e pesado) |
| Widgets, barra de tarefas, dashboards de blocos | 92 + 86 strings | não existe (só o overlay Ctrl+Shift+O) | fica na fila (trabalho de interface grande) |
| Ventoinha desgastada | sim ("fans wearing out") | não existe (temos os sensores, não a comparação) | fica na fila (precisa de base defensável) |
| Histórico em SQLite com reparo | sim (`rigsight.db`, "repair") | JSON (`settings.json`) + CSV | fica na fila (refactor grande) |
| Relatório por dia/semana/mês | 120 strings | temos o diário do dia e a linha do tempo | fica na fila |
| Temas claro/escuro | 22 strings | temos `ThemeManager` | já temos |
| Assistente de IA local (Ollama) | 159 strings | não existe | **fora por decisão do dono** |
| Overlay em jogo | 57 strings (via RivaTuner, para tela cheia) | temos overlay (Ctrl+Shift+O) | já temos (sem o modo tela cheia) |

## O que entrou na v0.27

**TRANAL USO** — a aba nova do grupo TRANAL ASSISTENTE:

1. **Histórico de uso** (`UsoHistorico`): um CSV por dia em
   `%APPDATA%\TranalSentinel\uso\`, com a troca de programa (processo +
   título da janela). Só a troca é gravada — a duração de cada uso é a
   distância até a próxima troca.
2. **Coletor** (`UsoJanela` + `UsoAmostrador`): lê a janela em frente uma
   vez a cada 5 s, pelo mesmo heartbeat que já grava o histórico de
   sensores. É somente leitura e nada sai do computador.
3. **Regra** (`UsoRegras`): função pura — ranking de tempo por programa,
   sessões mais longas, o que está em frente agora e **quem estava em
   frente quando a CPU ficou mais quente**.
4. **A explicação do alerta** (`UsoRegras.ExplicarAlerta`): o alerta passa
   a dizer o que os sensores faziam nos dois minutos antes e qual programa
   estava em frente.
5. **Uma frase, um lugar** (`ResumoAmostras`): o resumo dos sensores passou
   a ser escrito num ponto só, usado pela retrospectiva do travamento e
   pela explicação do alerta. Antes cada um tinha a sua cópia.

### Por que estas três e não as outras

As três fecham um ciclo que o app já tinha metade: o app já grava
o histórico de sensores (`historico.csv`) e já responde "o que a máquina
estava fazendo antes de travar" (`RetrospectivaTravamento`). O que faltava
era **quem** estava usando a máquina — e sem isso, "a CPU ficou a 85 °C" não
diz nada sobre a causa.

As outras (tráfego de internet, widgets, SQLite) são maiores ou exigem
hardware/infra que não temos aqui, e uma delas (IA local) está fora por
decisão do dono.

## Pendências desta auditoria

- Pegar o HWiNFO64 (formulário) e o CPU-Z (link direto) e refazer a
  impressão digital: são os que leem o SMN e podem fechar a lacuna do
  **tRFC**, que ainda não é mostrado no TRANAL MEMÓRIA.
- Extrair as assemblagens .NET do RigSight (o `innoextract` não lê o loader
  novo): traria o código de verdade do concorrente, não só a superfície.
