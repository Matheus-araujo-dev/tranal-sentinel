# Design da v0.31 — O TRANAL PAINEL (painel flutuante no desktop)

Data: 09/11/2026
Estado: aprovado pelo owner em 09/11/2026 (escopo, conteúdo e comportamento)
Base: v0.30.0 publicada (`ce0320b`)

## 1. Contexto

O TRANAL SENTINEL já tem o `OverlayWindow`: uma janela WPF transparente, sempre no
topo, que ignora cliques e mostra FPS e sensores por cima do jogo. Ela é controlada
por um `DispatcherTimer` de 500 ms e tem o seu próprio catálogo (`OverlayMetrics`,
22 métricas).

O que não existe é a presença do app **fora do jogo**: enquanto o TRANAL SENTINEL
está aberto na frente, o dono não tem os números do PC à vista sem abrir uma aba.

O `MERCADO.md` já aponta isso como a entrada da v0.31 ("painel flutuante com os
números sempre à vista"). A pesquisa de mercado (09/11/2026) confirmou que os dois
concorrentes que fazem isso têm as duas vias:

- **Kinetik Stats** (v1.4.0): barra na barra de tarefas **e** widget flutuante no
  desktop, agrupado por categoria (Processador, Gráficos, Memória, Rede,
  Armazenamento, Sistema, Bateria), com barra de uso colorida e gráfico de
  histórico por métrica.
- **RigSight**: 7 widgets no desktop + leituras ao lado do relógio na barra de
  tarefas + ícone de bandeja com ponto de saúde.

## 2. Objetivo

Uma segunda janela do próprio app, flutuante no desktop, arrastável, que mostra
quatro blocos — **CPU, GPU, RAM e ventoinha** — com os números do instante.

## 3. Não objetivos (ficam no backlog para a v0.32+)

- **Leituras dentro da barra de tarefas do Windows.** Exige injetar na barra do
  `explorer.exe` (toolbar do explorer), e isso quebra em atualização do Windows.
  Risco alto, benefício pequeno para quem já tem o painel na tela.
- **Widget oficial do Windows 11** (Adaptive Cards / Widget Board). Exige virar
  MSIX e mexer no instalador — outro produto, não outra versão.
- **Gráficos de histórico dentro do painel.** O Kinetik tem; exige janela de
  histórico própria e mais um consumidor do banco.

## 4. Decisões (aprovadas)

| Tema | Decisão |
|---|---|
| Escopo | Painel flutuante no desktop (não a barra de tarefas do Windows) |
| Conteúdo | CPU, GPU, RAM e ventoinha |
| Comportamento | Arrastável, "sempre no topo" opcional (default **ligado**) |
| Regra dos números | `NumeroTexto` (independente da cultura); ausente vira "—" |

## 5. Arquitetura

```
AppSettings            -> PainelVisivel, PainelSempreNoTopo, PainelX, PainelY
PainelRegras.cs (novo) -> NUCLEO PURO: retrato -> linhas formatadas
PainelWindow.xaml/.cs  -> janela frameless, transparente, arrastavel
MainWindow.Painel.cs   -> liga/desliga, timer de 500 ms, monta o retrato
MainWindow.xaml        -> pagina "Painel" (grupo TRANAL ASSISTENTE)
```

A regra da casa: **núcleo puro; o resto consome.** `PainelRegras` não conhece WPF,
não conhece `_monitor` e não conhece `_ventoinhas` — ele recebe um retrato com os
números e devolve as linhas prontas. Por isso ele é testável sem abrir janela.

### 5.1 `PainelRegras.cs` (núcleo puro)

```csharp
public sealed record PainelRetrato(
    double? TemperaturaCpu, double? CargaCpu,
    double? TemperaturaGpu, double? CargaGpu,
    double? RamUsadaGb,
    string? NomeVentoinha, double? RpmVentoinha);

public sealed record PainelLinha(string Rotulo, string Valor);

public static class PainelRegras
{
    public static IReadOnlyList<PainelLinha> Linhas(PainelRetrato retrato);
}
```

Regras:

- Cada bloco sai **sempre** (quatro linhas, sempre na mesma ordem): CPU, GPU, RAM,
  ventoinha. Um bloco sem dado mostra "—" — nunca um número antigo.
- Os números saem do `NumeroTexto` (vírgula decimal, ponto de milhar): o painel
  mostra o mesmo número que a tela e que o aviso, em qualquer cultura do Windows.
- A linha da ventoinha é `Nome · RPM` (ex.: "Fan #1 · 3.409 RPM"). O nome vem do
  sensor legível (`VentoinhaRegras.NomeLegivel`), nunca do id interno
  ("ITE IT8655E|Fan #1").

### 5.2 Escolha da ventoinha

O painel mostra **a ventoinha de maior RPM entre as que estão dando leitura** (a
que está trabalhando). Sem isso, o painel mostraria a Fan #2 (parada, 0 RPM) e o
dono pensaria que o PC está com a ventoinha morta.

Regra final:

1. entre as que dão leitura, a de maior RPM (empate: a primeira em ordem alfabética
   de id, para o resultado ser sempre o mesmo);
2. se nenhuma der leitura, a primeira ventoinha que o app conhece, com "—" no RPM
   (sem inventar valor);
3. se o app não conhece nenhuma ventoinha, a linha inteira vira "—".

### 5.3 `PainelWindow`

- `WindowStyle=None`, `AllowsTransparency=True`, `Background=Transparent`,
  `ResizeMode=NoResize`, `ShowInTaskbar=False`, `Topmost` conforme a opção.
- **Não** usa `WS_EX_TRANSPARENT` (o overlay ignora cliques; o painel precisa ser
  arrastável). Também **não** usa `WS_EX_NOACTIVATE`: o painel precisa do foco para
  o duplo-clique abrir a janela principal.
- Arrastável pelo corpo (mouse down + move), com a posição salva.
- Botão "×": **esconde o painel** (não sai do app — o app vive na bandeja e só o
  "Sair" encerra).
- Duplo-clique: traz a janela principal para a frente (o app já tem esse caminho
  na bandeja).
- Se o painel for aberto e a posição salva estiver fora de qualquer monitor
  (monitor trocou), ele volta para o canto superior direito do principal.

### 5.4 `MainWindow.Painel.cs`

Segue o padrão que o app já usa para o overlay (`MainWindow.xaml.cs:1399`):

- `_painel ??= new PainelWindow()`, `Show()`, `DispatcherTimer` de 500 ms,
  `Hide()` quando desliga.
- `UpdatePainel()` monta o retrato a partir de `_monitor` (CPU/GPU/RAM) e
  `_ventoinhas.Cartoes()` (ventoinha) e chama `_painel.Update(linhas)`.
- A posição é gravada no `AppSettings` quando o painel se move e quando o app
  fecha.

### 5.5 Página "Painel"

No grupo **TRANAL ASSISTENTE**, ao lado do Overlay:

- "Mostrar o painel flutuante" (checkbox) — o mesmo interruptor do overlay.
- "Sempre no topo" (checkbox), ligado por padrão.
- "Voltar para o canto" (botão) — devolve o painel ao canto superior direito.

## 6. Refactor que vem junto (e por quê)

O `BuildOverlayLines` (`MainWindow.Overlay.cs:84`) formata os números com
interpolação crua (`$"{x:0.0}"`), ou seja, **depende da cultura do Windows**. Se o
painel usar `NumeroTexto` e o overlay ficar como está, a mesma temperatura da GPU
sairia de dois jeitos no app (o painel com "8,0", o overlay com "8.0" em en-US).

Por isso a v0.31 converte as interpolações do overlay para o `NumeroTexto`. É
troca mecânica, não muda o que aparece no jogo (no PC do dono, pt-BR, o texto
resultante é o mesmo).

## 7. Testes

`TranalSentinel.Tests/PainelRegrasTestes.cs` (núcleo puro, sem WPF):

- as quatro linhas saem sempre, na ordem CPU, GPU, RAM, ventoinha;
- valor ausente vira "—" (e não zero, e não o valor velho);
- a cultura do Windows não muda o texto (virada para en-US);
- a ventoinha escolhida é a de maior RPM entre as que dão leitura;
- ventoinha sem leitura mostra o nome com "—" no RPM (não o RPM velho);
- a linha da ventoinha não vaza o id interno.

## 8. Prova real (no PC do owner)

1. Publicar o app de prova e subir: o painel aparece com os 4 blocos.
2. Arrastar o painel e fechar: a posição fica salva entre aberturas.
3. Captura de tela do painel sobre o desktop + da página "Painel".
4. O app continua funcionando na bandeja com o painel aberto (o painel não é a
   janela principal).

## 9. Riscos

| Risco | Como fica guardado |
|---|---|
| O painel atrapalha o jogo | "Sempre no topo" é opcional e o padrão é ligado; o dono desliga em um clique. O overlay de jogo continua separado. |
| O painel rouba o foco | A janela é pequena e o duplo-clique abre a janela principal por escolha do dono. |
| Posição salva fora da tela depois de trocar de monitor | Ao abrir, o app confere se a posição está em algum monitor; se não, volta ao canto. |
| O painel virar uma segunda tela que "mente" | O painel consome o mesmo núcleo e o mesmo `NumeroTexto` do resto do app; não há número próprio. |

## 10. O que entra no texto do dono

- `AGENTS.md`: a seção da v0.31 (regras do painel, o núcleo puro, o contrato do
  "—" e a conversão do overlay para o `NumeroTexto`).
- `NOVIDADES.md`: a entrada da v0.31.
- `MERCADO.md`: a linha dos widgets com o que a pesquisa mediu (Kinetik v1.4.0 e
  RigSight), e o backlog do que ficou de fora.
- `README.md` / `LEIA-ME.txt`: a página "Painel" na lista de abas.
