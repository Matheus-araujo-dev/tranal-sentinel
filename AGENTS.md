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
| Mutex (instância única) | `Local\TranalSentinel.SingleInstance` (a regra mora em `InstanciaUnica`) |
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
- **Nota de benchmark só existe se for finita e maior que zero.** A regra
  mora em `BenchResultado.NotaUtilizavel` e vale para exportar, importar e
  comparar: benchmark que falhou (nota zero) e nota quebrada (NaN, infinito)
  não viram número na tela. A linha **não** some: ela fica com a nota nula,
  para o app poder dizer "no outro PC este benchmark falhou" — em vez de
  "este teste só existe aqui", que seria mentira. Nota errada é pior que
  nota ausente.

- **O histórico mora num banco SQLite, e só nele** (`Banco`). Os sensores
  (5 em 5 segundos) e as trocas de programa são gravados em
  `%LOCALAPPDATA%\TranalSentinel\historico\historico.db`. O evento do uso é a
  **troca de programa** — não uma linha por instante: guardar o instante
  todocia vez encheria o disco sem responder pergunta nenhuma. O que estava
  em frente é o título da janela no momento da troca.
- **O tráfego de internet é medido pela ETW do kernel** (`ColetorTrafego`),
  e não pela API de estatísticas do TCP: a `GetPerTcpConnectionEStats` foi
  medida nesta máquina e devolve o número exato, mas **só funciona no IPv4**
  (no IPv6 o `SetPerTcp6ConnectionEStats` devolve "sem dados") e só cobre
  TCP — e o tráfego de navegador hoje é QUIC, que é UDP. A ETW cobre TCP e
  UDP, nos dois protocolos, por programa. O erro medido em 09/11/2026 no PC
  do dono foi de **+0,25%** (100 MB baixados apareceram como 105.006.661
  bytes): a diferença são os cabeçalhos TCP/IP, que também são tráfego — e
  o cartão diz isso na tela em vez de esconder.
- **A ETW exige administrador, e o app já exige.** O manifesto pede
  `requireAdministrator` e a tarefa agendada sobe com privilégio máximo. Se
  um dia subir sem, a coleta não derruba o app: o motivo fica guardado e o
  cartão mostra. A sessão do kernel é **global** — se outra ferramenta
  estiver usando (o WPR), a criação falha e o motivo aparece.
- **O total do dia é o espelho do que se sabe, e nunca uma soma.**
  `Banco.GravarTrafego` reescreve a linha (`INSERT OR REPLACE`): o que
  chega é o total do dia, e somar de novo colocaria o mesmo byte na conta
  duas vezes. O nome do processo **não distingue maiúsculas** (`COLLATE
  NOCASE`): o "Chrome" e o "chrome" são o mesmo programa.
- **A base do banco vem antes da medição, e sem ela o app não grava.**
  Quando o app sobe, ele lê o que já estava salvo do dia e semeia o
  coletor com isso (`ColetorTrafego.Semear`). Sem a base, cada abertura do
  app começaria do zero e gravaria por cima do total do dia — apagando as
  horas em que o app não estava rodando. E, se a base não puder ser lida,
  o app **não grava** até conseguir: ele espera, em vez de destruir o dia.
- **A virada do dia fecha o dia anterior.** O relógio de dez em dez
  segundos grava o total; quando o dia vira, o dia que acabou recebe o
  total final e o novo começa do zero. Sem isso, o tráfego da madrugada
  entraria na conta do dia anterior.
- **O que a coleta deixou de fora é guardado, não escondido.** A fila do
  log do kernel tem tamanho fixo: quando enche, o Windows descarta o que
  não coube e o total medido fica menor do que foi o real. A contagem vai
  para o banco (`trafego_perdidos`) e o cartão avisa. `-1` continua
  significando "não deu para contar" — nunca zero.
- **A contagem de programas é uma conta à parte do ranking.** O ranking da
  tela mostra os dez maiores, mas a métrica PROGRAMAS e a frase do cartão
  ("mais 22 programas usaram a rede") contam **todos** os programas que
  usaram a rede (`TrafegoRegras.ContarProgramas`). Usar o tamanho do
  ranking na métrica fazia o cartão dizer "10 programas" ao lado de uma
  frase que dizia 22 — dois números se contradizendo, e um deles errado.
- **O ranking do tráfego é por nome de programa, e o total é de todos.**
  O Chrome abre um processo por aba: a soma é pelo nome (`TrafegoRegras`).
  Quem não trafficou sai da lista, e o total do cartão é a soma de **todos**
  os programas — não só dos dez que o ranking mostra.
- **O SQLite vem do próprio Windows**, por P/Invoke em `Sqlite`
  (`System32\winsqlite3.dll`): o app **não ganha dependência nova**. É a
  mesma biblioteca que o Windows usa internamente.
- **O instante é epoch em milissegundos** (`INTEGER` no banco): ordenável,
  indexável e imune a horário de verão. A hora local volta por
  `DateTimeOffset`.
- **O CSV antigo é legado, e só a migração o toca.** `LegadoCsv` lê o que
  já existia em `%LOCALAPPDATA%\TranalSentinel\sensores\historico.csv` e em
  `%AppData%\TranalSentinel\uso\usage-AAAA-MM-DD.csv` **uma vez só**, na
  primeira vez que o banco é aberto (`PRAGMA user_version`). Os CSV **não
  são apagados**: eles ficam como cópia do histórico anterior.
- **O reparo reconstrói o banco e guarda o velho.** `Banco.Reparar()`
  confere a integridade, copia o que der para ler para um banco novo e
  troca o arquivo no lugar — e o banco estragado vira uma cópia datada
  (`historico.db.avariado-AAAAMMDD-HHMMSS`), que é o **desfazer**. O que
  não pôde ser lido é perdido, e o relato diz isso (quantas linhas se
  perderam, e não só que "deu problema").
- **O reparo conta e lê na mesma transação de leitura.** Contar as linhas e
  ler as linhas em instantes diferentes dá dois números de bancos
  diferentes — e o relato da perda sairia errado. E o que ficou no banco
  novo é a **contagem do banco novo**, não o número de operações de INSERT
  (duas linhas com o mesmo instante viram uma só, e o contador de
  operações mente para cima). Quando a contagem do banco velho não dá para
  fazer, o relato diz "não deu para contar" — nunca "não se perdeu nada".
- **Linha com nulo na coluna obrigatória não entra no banco novo.** O
  esquema não aceita nulo ali, e o `INSERT OR IGNORE` engoliria a linha em
  silêncio. A linha fica de fora e o relato conta a perda.
- **A cópia avariada tem nome exato:** `historico.db.avariado-` mais 15
  caracteres (yyyymmdd-hhmmss). O `-wal` e o `-shm` que acompanham a cópia
  **não são cópias** — e o desfazer nunca pode colocar no lugar do banco o
  WAL de **outro** arquivo (isso destrói o histórico em vez de trazê-lo de
  volta). O WAL **da própria cópia** volta junto com ela: é o par
  banco+WAL que o reparo guardou, e voltar separado deixaria o banco
  restaurado mais velho do que estava.
- **O WAL anda com o banco — ou a troca para.** Banco e WAL são um par: um
  WAL que fica para trás ao lado de outro banco é o jeito de destruir o
  histórico (o SQLite lê o WAL de um arquivo por cima de outro), e apagar
  o WAL do banco velho perde as últimas gravações dele em silêncio. Por
  isso `MoverArquivoDoWal` devolve `bool`: quando o WAL não pôde ir com o
  banco, o reparo e o desfazer **param** (o banco volta para o lugar, com o
  WAL dele) e o relato diz o que aconteceu. O relato nunca afirma que o
  WAL veio junto sem o código saber se veio.
- **O reparo não troca o arquivo sem ler.** Se a leitura falhou (e o
  arquivo não é "não é um banco de dados"), o reparo cancela e mantém o
  arquivo; se o banco tinha linhas e o novo ficou vazio, cancela também.
  Trocar histórico por vazio é pior do que ficar com o banco estragado.
- **O banco novo do reparo é apagado DEPOIS do `using` que o abre.** Com o
  handle do SQLite aberto, o Windows não deixa apagar o arquivo — e o
  `historico.db.reparo` (dezenas de megabytes) ficaria órfão na pasta do
  histórico a cada tentativa cancelada. O cancelamento é anotado e a
  limpeza acontece com o handle fechado; se ainda assim o arquivo
  sobrar, o relato diz — em **todos** os caminhos de cancelamento (a
  gravação que falhou, o cancelamento por banco vazio, a troca que não
  deu certo e a exceção), e a frase é a mesma nos quatro.
- **O reparo conhece todas as tabelas.** A reconstrução do banco lê e
  reescreve as amostras, o uso, o tráfego por programa **e** a contagem de
  pacotes descartados: uma tabela nova que o reparo não conhecesse seria
  perdida **em silêncio** (o relato contaria só as outras duas). E uma
  tabela que **não existe** no banco velho (um banco de antes da v0.29) é
  "não tinha tráfego guardado", e não uma leitura que falhou — senão o
  reparo de um banco antigo seria cancelado à toa.
- **A perda é contada por tabela.** Cada tabela tem a sua contagem e o seu
  "não deu para contar": uma contagem que falhou não pode apagar a conta
  da outra tabela (`Banco.RelatoDaPerda`, que é pura e testada).
- **A conta dos pacotes descartados é por dia.** A sessão do Windows conta
  os descartes desde que ela subiu, e esse número só cresce: usado como
  está, o dia novo nasce com os descartes do dia anterior — e o dia velho
  recebe os do dia novo. Cada leitura compara o total da sessão com o da
  leitura passada, e a diferença vai para o dia em que ela foi vista
  (`ColetorTrafego.EventosPerdidosDoDia`).
- **O total do dia é gravado também quando o app fecha.** O relógio grava a
  cada dez segundos, e o app pode fechar entre um tique e outro: o
  tráfego desses segundos (e o que ainda estava na fila do kernel) seria
  perdido **em silêncio**. A última gravação acontece no `Closed`, depois
  que a coleta para (`MainWindow.Net.GravarTrafegoFinal`).
- **A consulta ao Windows sobre o processo fica fora da trava da coleta.**
  `Process.GetProcessById` é lento: dentro da trava ela pararia a fila de
  pacotes da ETW (o total sairia menor do que o real, sem aviso) e a
  montagem do cartão na tela. O nome é resolvido fora da trava, e o
  ranking usa o nome já guardado (`ColetorTrafego.NomeEmCache`); a
  conferência periódica (o Windows reusa o número do processo) roda na
  thread de fundo.
- **O retrato que falha conta -1 em TODAS as tabelas.** O caminho de
  exceção do `Banco.Ver()` passa a contagem do tráfego como -1 quando o
  arquivo existe: deixar o campo no padrão (0) faria a tela dizer "0
  registros de tráfego" ao lado de "não deu para contar as amostras" —
  um número inventado no meio de dois números honestos.
- **"A tabela não existe" e "a consulta ao esquema quebrou" são coisas
  diferentes.** No reparo, `TabelaExiste` olha o erro do SQLite: se a
  consulta ao esquema falhou, a tabela é tratada como **leitura que
  falhou** (o reparo é cancelado e o relato diz), e não como "não tinha
  tráfego guardado" (a tabela seria descartada em silêncio).
- **A contagem de pacotes descartados é `long` de ponta a ponta**, e o -1
  ("não deu para contar") é **gravado como -1**: gravar 0 seria dizer que
  nenhum pacote foi descartado — outra afirmação, e falsa. No cartão, a
  contagem entra como `long` (`TrafegoRegras.NumeroInteiro`): passar por
  um `int` a cortaria em 2.147.483.647.
- **O cartão fala do dia que ele mostra.** O texto de "não teve
  tráfego" tem duas versões: a do dia corrente (que cita a hora e diz
  que o app só mede enquanto está rodando) e a de um dia guardado no
  banco (que não fala de "hoje"). Um cartão de ontem dizendo "hoje é
  14:32" é uma afirmação falsa sobre a origem do número.
- **A verificação de integridade é uma só: a completa (`integrity_check`).**
  A rápida (`quick_check`) foi descartada porque ela não confere o conteúdo
  dos índices contra o das tabelas — e o esquema tem dois índices na tabela
  de uso, um deles a chave que impede a mesma troca de ser gravada duas
  vezes. Medido em 09/11/2026: o `quick_check` respondeu "ok" num banco de
  40 linhas cujo índice `sqlite_autoindex_uso_1` apontava para uma linha que
  não existia mais, enquanto o `integrity_check` respondia "row 40 missing
  from index sqlite_autoindex_uso_1". E as duas custam o mesmo tempo (cerca
  de 60 ms num banco com 258 mil amostras e 18,6 MB): não há ganho em
  trocar, e um "ok" falso é pior do que a pausa. O estado na tela, o
  relatório da linha de comando e o reparo dizem a mesma coisa sobre o
  mesmo arquivo.
- **"Não tem" e "não deu para ler" são frases diferentes**
  (`Leitura<T>.Completa`). Lista vazia com leitura completa é "não teve uso
  nesse dia"; lista vazia com leitura incompleta é "não deu para ler" — e
  a tela do TRANAL USO diz uma coisa ou outra, nunca as duas parecidas.
  O retrato do banco também: quando ele não pôde ser lido, a contagem é
  -1 ("não deu para contar"), nunca 0.
- **Histórico que não deu para ler não tem explicação.** A explicação de um
  alerta é feita com o histórico ("o app não estava gravando sensores nesse
  momento"). Com o histórico pela metade, essa frase é uma afirmação falsa:
  o app estava gravando — o que não deu foi a *leitura*. A regra mora no
  núcleo puro (`UsoRegras.ExplicarAlerta`, que devolve `null` quando a
  leitura ficou incompleta): a sobrecarga com listas cruas é **interna**,
  então quem está de fora só tem a versão com o sinal `Completa` e não
  tem como contornar a regra. Nada é guardado em cache, e a linha da tela
  (`UsoRegras.TextoDoPorQueDisparou`, também pura e testada) mostra "não
  deu para ler o histórico para explicar este aviso" — silêncio ali seria
  uma resposta errada.
- **O -1 é "não deu para contar", em qualquer ponta da conta.** A contagem do
  banco novo pode falhar depois do COMMIT (disco cheio, antivírus com o
  arquivo): o relato não pode dizer "-1 amostras foram salvos" nem calcular
  "se perderam N+1". Sem as duas pontas, não há diferença — há um número
  inventado (`Banco.RelatoDoSalvo` e `Banco.RelatoDaPerda`, ambas puras e
  testadas).
- **O tamanho do banco no disco é o do disco, mesmo quando a leitura
  falha.** O retrato que não pôde ser montado ainda diz o tamanho do
  arquivo: "0 B" ao lado de "não deu para contar as amostras" seriam dois
  números falsos.
- **A cópia só é oferecida como desfazer depois que ela existe no
  disco.** O nome é montado antes da troca, mas a cópia entra na história
  depois do `File.Move` dar certo — um caminho que aponta para um arquivo
  que não existe não pode virar botão de restaurar.
- **A trava de instância única é sondada, nunca criada.** `EstaRodando`
  abre o mutex existente; se ela o criasse, o app que subisse na mesma
  hora acharia que outra instância estava rodando e não subiria, sem
  mensagem. E a linha de comando **segura** a trava durante o reparo: se o
  app subisse no meio da troca, ele abriria o banco velho e as gravações
  dele iriam para o arquivo renomeado.
- **A migração é uma transação só**, e o `PRAGMA user_version` só é
  gravado quando ela fecha bem. Gravar a versão com a importação pela
  metade faria o app nunca mais tentar importar o CSV — e o histórico
  antigo ficaria órfão, sem aviso.
- **A migração vai em lote, com o comando preparado reaproveitado** e só
  importa o que ainda está dentro do prazo de cada tabela (15 dias de
  amostras, 30 de uso). Preparar e finalizar um comando por linha custava
  23 segundos em 260 mil linhas (medido); com o comando reaproveitado, a
  mesma migração leva cerca de 2 segundos (medido). A versão do esquema é
  conferida de novo dentro da transação: a thread da tela e a thread dos
  sensores abrem o banco ao mesmo tempo, e as duas não podem importar o
  CSV duas vezes.
- **Transação que falha sempre fecha.** Se o COMMIT falha, o ROLLBACK
  explícito desfaz o que ficou pelo caminho — sem ele, a transação ficava
  aberta e as escritas seguintes falhavam uma por uma, sem aviso.
- **O reparo pela linha de comando exige `--confirmar`.** Na tela a
  confirmação é um botão; na linha de comando é a flag. Trocar o arquivo do
  histórico é decisão do usuário, e uma linha de comando digitada sem
  querer não pode tomar essa decisão.
- **Cada tabela tem o seu prazo de poda**: as amostras ficam 15 dias e o
  histórico de uso fica 30. Uma poda com o prazo errado perde dado que a
  regra promete guardar.
- **Tudo que lê ou escreve no banco passa pela trava** (`Banco.EmBanco`):
  o reparo fecha a instância e troca o arquivo, e uma leitura fora da
  trava leria um arquivo que está sendo trocado (ou pior: usaria um
  handle já fechado).
- **Um banco que não abre não derruba o app.** A abertura é uma instância
  única, com retentativa a cada 30 s; se falhar, o app continua funcionando
  e a tela diz que o histórico não está disponível.
- **O dia local é uma faixa de instantes, e a faixa muda no horário de
  verão.** Por isso a lista de dias com histórico é montada em C#
  (`Banco.DiasComUso`), e não com `date(...,'localtime')` do SQLite (que
  depois de `'unixepoch'` não devolve nada na biblioteca do Windows).
- **A regra do uso é pura** (`UsoRegras`): entra a lista de trocas e a de
  amostras, sai o relatório (ranking, sessões mais longas, quem fez o calor).
  Quem lê o arquivo é a coleta; quem formata o texto é `UsoTexto`. A tela e a
  explicação do alerta consomem as duas, não cada uma com o seu texto.
- **O resumo dos sensores tem um lugar só** (`ResumoAmostras.Descrever`): a
  retrospectiva do travamento e a explicação de por que o alerta disparou
  usam a mesma frase. Duas frases para o mesmo número é o jeito mais rápido de
  o app se contradizer.
- **A explicação do alerta é honesta quando falta dado.** Sem amostra de
  sensor na janela, a resposta é "o app não estava gravando sensores nesse
  momento" — nunca um número inventado e nunca um motivo chutado.
- **A janela em foco é lida, nunca adivinhada.** `UsoJanela` usa
  `GetForegroundWindow`/`GetWindowThreadProcessId` do user32. A janela do
  próprio TRANAL SENTINEL também entra no histórico, com o nome do app: o
  tempo em que a pessoa olha o monitor não pode ser creditado no programa
  que estava em frente antes (era o que inflava o ranking sem ninguém
  perceber). A leitura nunca lança: falha na coleta de uso não pode
  derrubar o app.

- **O período de um dia vai da primeira evidência de que o app rodava à
  última** (`UsoRegras.Periodo`). Hoje o fim é "agora"; num dia passado é a
  última amostra de sensor gravada (o log escreve de 5 em 5 segundos
  enquanto o app roda) ou, sem ela, a última troca de uso. O começo é a
  primeira evidência do dia. Sem essas duas pontas, as horas em que o app
  estava fechado (PC desligado, app fechado) seriam creditadas a um
  programa. Tempo inflado é pior que tempo ausente.
- **A sessão é cortada pelo período do dia** (`UsoRegras.MontarSessoes`) e a
  leitura de um dia traz também a última troca do dia anterior: é a que
  atravessa a meia-noite. Sem ela, a noite inteira ficaria sem dono quando o
  app rodou a noite toda sem trocar de programa.
- **O log de sensores é lido limitado à janela pedida**
  (`SensorHistorico.Ler(desde, ate)`): a consulta vai direto ao índice do
  instante, então o histórico inteiro não é varrido para saber o que
  aconteceu na janela. O banco guarda 15 dias de amostras e o histórico de
  uso guarda 30 dias; o que passa disso é apagado.
- **A explicação do alerta fica em cache por episódio.** O instante do
  episódio não muda enquanto o aviso está na tela, então a explicação é
  calculada uma vez — e o histórico só é lido quando aparece um aviso sem
  explicação.
- **Uma leitura pela metade não vale como importação completa**
  (`Leitura<T>.Completa`). A leitura do CSV antigo pode parar no
  meio (o arquivo trancado, o disco cheio, o antivírus com ele aberto). Quando
  isso acontece, o `user_version` **não** é gravado: a versão diz "o CSV já
  entrou", e gravá-la com a leitura pela metade faria o resto do CSV ficar
  órfão para sempre, sem aviso. A migração é refeita na próxima abertura.
- **O estado e o aviso são canais separados** (`Banco._estado` e `Banco._aviso`).
  O estado diz se o banco está de pé; o aviso diz o que ficou pendente (a
  migração pela metade, por exemplo). O aviso viaja em `Diagnostico.Aviso`
  até a tela e a linha de comando. Quando os dois moravam no mesmo lugar, o
  "ok" da abertura apagava a falha da migração duas linhas depois.
- **O reparo pela linha de comando exige o app fechado.** O Windows segura o
  arquivo do banco enquanto o app roda, e a troca falharia só no último passo
  (depois de todo o trabalho de reconstruir o banco, e com um relatório que
  não dizia a verdade). A recusa vem antes, e o reparo cancelado limpa o
  banco novo: ele ocuparia dezenas de megabytes na pasta do histórico sem
  servir para nada.
- **O banco reconstruído guarda a marca do histórico importado do banco
  velho** (`PRAGMA user_version`). Sem ela, a próxima abertura acharia que o
  CSV nunca entrou e o importaria de novo — por cima do histórico que o
  reparo acabou de recuperar. Quando nada foi recuperado, a marca fica em
  zero de propósito: o CSV antigo volta na abertura seguinte, em vez de
  ficar para sempre fora do app.
- **`user_version` não é a versão do esquema.** Ele marca **só** a importação
  do CSV: as tabelas novas nascem por `CREATE TABLE IF NOT EXISTS` em toda
  abertura e não dependem dele. A próxima versão que precisar migrar
  **dados** precisa de outra marca, para não bater com esta.
- **O retrato do banco é um só** (`Banco.Ver`): estado, tamanho, contagens e
  dias com histórico saem de uma leitura com a trava segurada. A linha de
  comando mostra os números do mesmo retrato — dois momentos diferentes podiam
  dar dois bancos diferentes.

- **A ventoinha é comparada com ela mesma.** O app não tem curva de RPM por
  temperatura nem tabela de fabricante: o que ele compara é o maior RPM que a
  ventoinha já deu (com a temperatura do mesmo instante) contra o RPM de agora.
  Nenhum número inventado entra no caminho — e o teste garante isso.
- **O sinal de degradação tem três travas, e as três moram no núcleo puro**
  (`VentoinhaRegras`): o RPM precisa estar **pelo menos 20% abaixo** do
  recorde, a temperatura de agora precisa ser **igual ou maior** que a do
  recorde, e a condição precisa valer **5 minutos seguidos**. A perda é
  calculada como *diferença dividida pelo recorde*: subtrair depois
  ("1 - rpm/recorde") arredonda o limite de 20% para baixo, e o app calava
  exatamente no limite que ele promete acusar. E sem tempo medido não há
  persistência provada — o selo de degradação não sai. A trava mora no núcleo,
  e não em quem consome o veredito.
- **O recorde só sobe, e a temperatura dele só sobe com medida.** Um recorde
  novo lido sem temperatura (a referência da CPU/GPU não foi lida naquele
  instante) não apaga a temperatura que já foi medida: perder um fato medido é
  pior do que uma temperatura um pouco mais antiga.
- **A fonte da temperatura de referência sai do tipo do hardware dono**
  (medido na biblioteca 0.9.6 do LibreHardwareMonitor): `SuperIO`,
  `Motherboard`, `Cooler` e `EmbeddedController` → CPU; `GpuNvidia`, `GpuAmd` e
  `GpuIntel` → GPU; o resto → nenhuma. Ventoinha de disco não tem referência,
  e o app não compara.
- **"Não tem" e "não deu para resolver" são frases diferentes.** O registro da
  ventoinha vem do banco, mas a fonte da temperatura só é resolvida na
  primeira leitura do ciclo: até lá, o cartão diz que a fonte **ainda não foi
  resolvida** — ele não afirma que a ventoinha não tem referência.
- **O piso de 300 RPM.** Uma ventoinha que nunca passou de 300 RPM não tem
  histórico que sustente um veredito: o cartão diz "sem histórico" e diz o
  porquê. E sensor sem valor não entra como "0 RPM": não deu para ler é
  diferente de zero.
- **A base do dia vem do banco antes da medição.** O app pode ser fechado e
  aberto várias vezes no mesmo dia: sem a base, cada abertura reescreveria o
  agregado do dia pela metade e o histórico da manhã desapareceria sem aviso.
  E, se a base **não puder** ser lida, o app **não grava** o dia: ele espera
  a base aparecer, em vez de destruir o que já estava salvo (a mesma regra do
  tráfego de internet). Quando a base volta depois, ela é **juntada** com o
  que já foi medido — não trocada — e as duas partes formam o dia inteiro.
  Medido no PC do dono em 09/11/2026: com a base, o dia foi de 495 para 506
  amostras na primeira gravação depois de reabrir o app.
- **O recorde não tem prazo de poda; o agregado do dia tem 30.** O recorde é
  uma linha por ventoinha e é justamente o que precisa sobrar quando o
  histórico diário passa. A poda do dia não pode usar `.Date`: com ele, o app
  guardava 31 dias.
- **A ventoinha é regra de alerta como qualquer outro sensor** (a chave é
  `ventoinha:<id>`). A vigia só **avalia** a regra da ventoinha que está
  dando leitura — e a tela diz, para a que não está, que a regra **não está
  vigiando nada**. Uma regra que aponta para uma ventoinha que não existe
  mais na máquina cala em silêncio, e o usuário ficaria achando que está
  protegido.
- **A ventoinha que parou de dar leitura não vale mais com o RPM velho.** A
  coleta entrega uma leva por vez, e o app mede a leva de agora — não a lista
  de todos os sensores que ele já viu (um sensor que saiu da coleta continua
  lá, com o valor antigo). Sem leitura: o cartão diz "SEM LEITURA" e diz
  quando foi a última medida boa, o dia não conta medição que não existiu, a
  queda que estava sendo observada deixa de contar, e a regra de alerta não
  avisa com o número de antes. Número errado é pior que número ausente.
- **A contagem do dia conta leitura, não tique.** A janela redesenha a cada
  segundo e a coleta nem sempre chegou nesse meio tempo: medir a mesma leva
  duas vezes não é ler o sensor duas vezes.
- **A falha de gravação do dia aparece no diário.** A janela está fechando e o
  usuário não vai ver a tela avisando: o diário é onde o app conta o que ele
  fez.
- **O número da tela sai de um lugar só** (`NumeroTexto`): vírgula decimal e
  ponto de milhar, aconteça o que acontecer com a cultura do Windows. Número
  que não é finito vira "—" — nunca um número inventado. Isso vale também
  para o **texto do aviso de alerta**: o aviso aparece na tela (na lista de
  "Alertas ativos") e ele não pode sair escrito de dois jeitos em PCs de
  culturas diferentes. O `AlertasSegundoPlano` e a página de alertas usam o
  mesmo formatador, e o teste `OTextoDoAvisoNaoDependeDaCulturaDoWindows`
  prende isso.

### v0.31 — o painel flutuante

- **O painel flutuante é a segunda janela do app** (`PainelWindow.xaml` e
  `PainelWindow.xaml.cs`), como o overlay: nasce com o TRANAL SENTINEL e morre
  com ele. Quem liga e desliga, quem atualiza as linhas, quem guarda a
  posição e quem devolve o painel para o canto é `MainWindow.Painel.cs` — a
  partial class da janela principal. A página "Painel" (grupo TRANAL
  ASSISTENTE) é onde o dono liga isso.
- **As quatro linhas do painel morem no núcleo puro** (`PainelRegras.Linhas`):
  CPU, GPU, RAM e ventoinha, sempre nesta ordem. `PainelRegras` e
  `SensorTexto` **não conhecem WPF** — devolvem o texto pronto, e a janela só
  mostra. É o que deixa as linhas testáveis sem abrir uma janela (a janela do
  WPF exige uma thread com STA).
- **O que não deu para ler no painel é "—", nunca zero e nunca um valor
  velho.** O traço mora em `SensorTexto.Ausente`; a parte que falta some da
  linha em vez de virar "— · —". Número errado é pior que número ausente.
- **A ventoinha do painel é a de maior RPM entre as que estão dando leitura**
  (`PainelRegras.EscolherVentoinha`), com desempate pelo nome: duas
  ventoinhas com o mesmo RPM têm que dar sempre o mesmo painel. Sem nenhuma
  dando leitura, a linha mostra a primeira que o app conhece, sem número —
  o painel não mostra a ventoinha parada como se fosse a que está trabalhando.
- **`SensorTexto` é o único lugar das unidades dos sensores** (temperatura,
  porcentagem, MHz, GHz, W, V, RPM, GB, MB, MB/s, A, L/h, Hz, mWh e ms) e
  **`NumeroTexto` é o único lugar do número** (vírgula decimal, ponto de
  milhar, independente da cultura do Windows). O overlay, o painel, a aba de
  ventoinhas, a tela de memória e a lista de sensores consomem os dois:
  nenhum deles escreve unidade nem vírgula por conta própria.
- **A posição do painel é guardada em DIPs** (`AppSettings.PainelX` e
  `PainelY`), e a tela virtual é lida na mesma unidade. Se o dono trocou de
  monitor, o painel volta para o canto em vez de ficar fora da tela — e o
  botão "VOLTAR PARA O CANTO" dá a mesma posição.

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
| `UsoHistorico.cs` / `UsoJanela.cs` / `UsoAmostrador.cs` | O histórico de uso: a gravação da troca de programa no banco, a leitura da janela em foco (user32) e o amostrador que grava só na troca de programa. |
| `TrafegoRegras.cs` / `ColetorTrafego.cs` | O tráfego de internet por programa: a regra pura (ranking, bytes, textos do cartão) e o coletor que mede pela ETW do kernel. |
| `Banco.cs` | O banco de dados do histórico: esquema, migração do CSV antigo, consulta por período, contagem, poda e a rotina de reparo (que guarda a cópia do banco estragado). |
| `VentoinhaRegras.cs` | A regra pura do desgaste da ventoinha: o recorde, o veredito, as três travas e todos os textos da tela. |
| `MonitorVentoinhas.cs` | A coleta das ventoinhas: a leitura de cada uma, o recorde em memória, o agregado do dia e a gravação no banco. |
| `NumeroTexto.cs` | A formatação de todo número e data da tela (um lugar só, independente da cultura do Windows). |
| `SensorTexto.cs` | O texto das unidades dos sensores (um lugar só): temperatura, porcentagem, MHz, GHz, W, V, RPM, GB, MB, MB/s, A, L/h, Hz, mWh e ms. |
| `PainelRegras.cs` / `PainelWindow.xaml` / `MainWindow.Painel.cs` | O painel flutuante: as quatro linhas no núcleo puro (`PainelRegras`), a janela que as mostra (`PainelWindow`) e a integração no app — ligar, atualizar, guardar a posição (`MainWindow.Painel.cs`). |
| `MainWindow.Ventoinhas.cs` | A página TRANAL VENTOINHAS: os cartões e o "RPM com o tempo". |
| `Leitura.cs` | A leitura de um histórico: as linhas lidas e se a leitura ficou completa (a mesma regra para o CSV antigo e para o banco). |
| `InstanciaUnica.cs` | A trava de instância única: o app não sobe duas vezes, e a linha de comando só repara o histórico com o app fechado. |
| `Sqlite.cs` / `SqliteLinha.cs` | O acesso ao SQLite do Windows por P/Invoke (`winsqlite3.dll`), sem dependência nova, e a linha tipada da consulta. |
| `LegadoCsv.cs` | O formato do histórico antigo em CSV. Só a migração o usa — e só para ler. |
| `UsoRegras.cs` / `UsoTexto.cs` | A regra pura do histórico de uso (ranking, sessões, quem fez o calor), a explicação do alerta (que não existe quando o histórico não deu para ler) e os textos da tela. |
| `ResumoAmostras.cs` | A frase única do resumo dos sensores (usada pela retrospectiva do travamento e pela explicação do alerta). |
| `Bandeja.cs` | Ícone na bandeja por P/Invoke direto (sem Windows Forms, que criaria ambiguidade de nomes). |
| `SensorValor.cs` | Regra única do valor de sensor: finito ou nada (`Finito`) e as opções de JSON (`OpcoesJson`). |
| `SensorHistorico.cs` | O histórico contínuo de sensores (uma amostra de 5 em 5 segundos): o que o PC estava fazendo antes de travar. |
| `RetrospectivaTravamento.cs` | Correlaciona o log de sensores com os travamentos (função pura): vira a frase da tela. |
| `HardwareColeta.cs` / `HardwareLibre.cs` | O que a coleta precisa de um hardware (interface) e o adaptador do LibreHardwareMonitor. |
| `AutoUpdate.cs` / `Atualizador.cs` | Checagem de versão nova, download e troca do executável. |
| `Tranal.Setup\` | O instalador (admin, atalhos, tarefa agendada, desinstalação). |
| `Tranal.Setup\Dominio\DestinoInstalacao.cs` | Onde o app é instalado. Regra única do destino (pasta escolhida pelo usuário). |
| `MERCADO.md` | Pesquisa de concorrentes e o que foi adotado aqui. |
