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
  reinicia nada. Ações que mudam o sistema são exceções explícitas.
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
- **A comparação de snapshots** (`Mudancas.Comparar`) é função pura e não tem
  I/O — é o que os testes cobrem.

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
| `Mudancas.cs` | A linha do tempo do PC: retrato, comparação pura e coleta do retrato. |
| `PoliticaRecursos.cs` | Perfil Completo / Equilibrado / Economia. Funções puras. |
| `RegistroDiario.cs` | O log diário (um arquivo por dia). |
| `Bandeja.cs` | Ícone na bandeja por P/Invoke direto (sem Windows Forms, que criaria ambiguidade de nomes). |
| `AutoUpdate.cs` / `Atualizador.cs` | Checagem de versão nova, download e troca do executável. |
| `Tranal.Setup\` | O instalador (admin, atalhos, tarefa agendada, desinstalação). |
| `MERCADO.md` | Pesquisa de concorrentes e o que foi adotado aqui. |
