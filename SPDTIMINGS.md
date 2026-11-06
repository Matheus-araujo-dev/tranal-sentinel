# Memória: SPD, timings e perfis XMP/EXPO

> Notas de engenharia obtidas por análise estática do **CPU-Z 3.02** (CPUID,
> freeware) para implementar a leitura de SPD e de timings de memória no
> TRANAL SENTINEL sem depender de nenhum programa de terceiros.
>
> **Status:** arquitetura e mecanismo mapeados. Faltam os offsets exatos do
> controlador AMD AM5 (ver [O que falta](#o-que-falta)).

---

## 1. Por que isso importa

Hoje a aba de memória mostra fabricante, part number, voltagem, clock
configurado e o slot. O que **não** aparece:

- os timings ativos (CL, tRCD, tRP, tRAS, tRC, tRFC);
- se o perfil **EXPO** ou **XMP** está ativo, e com que frequência e voltagem;
- o conteúdo bruto do SPD (o que a memória declara que suporta).

Para o diagnóstico é exatamente a peça que falta: o TRANAL SAÚDE aponta que a
RAM está a 3466 MHz acima do spec de 3200, mas sem os timings não dá para
dizer se é **EXPO/XMP ativo** (o usuário escolheu) ou ** overclock manual por
software** (o app pode acusar).

## 2. O que foi analisado

| Item | Valor |
|---|---|
| Pacote | `cpu-z_3.02-en.zip` (portátil, **não instala nada**) |
| Origem | `https://download.cpuid.com/cpu-z/cpu-z_3.02-en.zip` |
| SHA-256 | `FAF62F6E883056A40F291BF9F3F348F51A2AB339D0A512F4724FDAE964F35D8B` |
| Verificação | confere com o `checksum.txt` publicado pela CPUID |
| Binário | `cpuz_x64.exe` (PE32+, 7.515.784 bytes) |

O pacote contém `cpuz.ini`, `cpuz_readme.txt` e os executáveis x86/x64. **O
driver de kernel vem embutido como recurso PE do executável** (RCDATA), não
como arquivo solto: ids 107 (x86), 108 e 109 (x64) — este último com
SHA-256 `a8f23029ca0e1fda27b5a37fa7a915631432ac8c8fa187156224d4c5ac97a7c6`.

## 3. A arquitetura do CPU-Z (extraída dos nomes de classe RTTI)

O binário preserva RTTI, o que expõe o desenho inteiro:

```
CKMDriver              → a ponte com o driver de kernel
CSMBusInterface        → a interface de SMBus (uma por chipset)
CSMBusRequest          → uma transação de SMBus
CSMBusDevice           → um dispositivo no barramento
CSMBusRegisterSpace    → o mapa de registradores (um por chipset)
CSPDInterface          → o protocolo do SPD
```

E as implementações por chipset do mapa de registradores:

| Classe | Chipset |
|---|---|
| `Intel_82801_SMBusControllerStatus` | Intel ICH (família 8xx) |
| `Intel_82801FB_SMBusControllerStatus` | ICH FB |
| `SiS_96x_SMBusControllerStatus` / `SiS_SMBusRegisterSpace` | SiS |
| `ALi_M1533_SMBusControllerStatus` | ALi/MSi |
| `VIA_VT82_SMBusRegisterSpace` | VIA |
| `Zhaoxin_KX_SMBusRegisterSpace` / `Zhaoxin_KX7_...` | Zhaoxin (KX/KX7) |
| `ATI_SB_SMBusRegisterSpace` | ATI/AMD southbridge |

**Lição para nós:** o SMBus não é uniforme. Um SMBus "genérico" (SMBus 2.0,
estilo Intel ICH) funciona em quase todo chipset moderno — inclusive no
AM5 —, mas o caminho AMD é específico. É a mesma ideia do `CSPDInterface`:
uma interface + uma implementação por família.

## 4. O driver de kernel (mecanismo)

O driver (`cpuz.sys`, driver version **168**) importa do `HAL.dll`:

| Import | Para quê |
|---|---|
| `HalGetBusDataByOffset` / `HalSetBusDataByOffset` | ler/escrever **configspace PCI** |
| `READ_PORT_UCHAR` / `_USHORT` / `_ULONG` e os `WRITE_PORT_*` | ler/escrever **portas de I/O** |
| `KeStallExecutionProcessor` | esperar entre transações de SMBus (o protocolo exige microssegundos) |
| `HalCallPal` | método antigo de acesso ao PCI |

E do `ntoskrnl.exe`:

| Import | Para quê |
|---|---|
| `MmMapIoSpace` / `MmUnmapIoSpace` | mapear o registrador do controlador SMBus no kernel |
| `MmAllocateContiguousMemorySpecifyCache` | memória contígua para o buffer |
| `IoCreateDevice` / `IoCreateSymbolicLink` | `\Device\cpuz168` e `\DosDevices\CPUZ168` |
| `IoCreateDeviceSecure` + DACL `D:P(A;;GA;;;BA)(A;;GA;;;SY)` | **só administradores e SYSTEM** abrem o device |
| `ZwOpenKey`/`ZwCreateKey`/`ZwQueryValueKey`/`ZwSetValueKey` | registro do Windows |
| `SeCaptureSecurityDescriptor`, `RtlAddAccessAllowedAce`, ... | validação do SID de quem abre |

O exe, do lado do usuário: `DeviceIoControl` para falar com o driver,
`CreateServiceW`/`StartServiceA`/`DeleteService` para instalar e remover o
driver como serviço, `FindResourceA` + `GetTempPathA` + `WriteFile` para
gravar o driver em `%TEMP%` e rodar.

### Consequência para o TRANAL SENTINEL

**Não precisamos do driver do CPU-Z, nem de um driver novo.** O **PawnIO**,
que o app já usa (via LibreHardwareMonitor), expõe exatamente o mesmo
conjunto de primitivas:

| CPU-Z (driver) | PawnIO (nosso) |
|---|---|
| `HalGetBusDataByOffset` | `READ_PCI_CONFIG` / `WRITE_PCI_CONFIG` |
| `READ_PORT_*` / `WRITE_PORT_*` | `READ_PORT` / `WRITE_PORT` |
| `MmMapIoSpace` | `READ_MEMORY` (memória física) |

Ou seja: o TRANAL SENTINEL lê o SPD e os timings **sem instalar driver
nenhum**, sem serviço novo e sem UAC além do que o app já exige. O driver do
CPU-Z existe só porque um programa em modo de usuário não pode tocar em
portas de I/O nem em configspace PCI — problema que o PawnIO já resolve para
a gente.

## 5. O modelo de timings (os campos que a gente precisa exibir)

Extraído das strings do binário:

| Rótulo do CPU-Z | Campo |
|---|---|
| `CAS# latency (CL)` | `cl` |
| `RAS# to CAS# delay (tRCD)` | `trcd` |
| `RAS# Precharge (tRP)` | `trp` |
| `Cycle Time (tRAS)` | `tras` |
| `Bank Cycle Time (tRC)` | `trc` |
| `Row Refresh Cycle Time (tRFC)` | `trfc` |
| `Total CAS# (tRDRAM)` | latência CAS total |
| `Clock_DUTY %d` | clock por duty cycle (o método alternativo de clock) |

Nomes internos de registro que o binário carrega: `trfc`, `trc`, `tras`,
`trp`, `trcd`, `timings` — e, do lado AMD, **`dram_idle_timer`** e
**`dram_bclk_ratio`** (registros do UMC).

## 6. O modelo de perfis

| Perfil | Observações do binário |
|---|---|
| **JEDEC** | tabela `CL-tRCD-tRP-tRAS-tRC @ frequency` |
| **XMP** | `CL-tRCD-tRP-tRAS-tRC-CR @ frequency (voltage)`, revisão, nome do perfil, `XMP %04d` |
| **EXPO** | idem, com `EXPO %d`, `EXPO %d.%d`, e **EXPO-ULL** (novo na 3.02) |
| **AMP** | `AMP profile`, `AMP timings table` |
| **EPP** | dois formatos: abreviado (`abb`) e completo (`full`) |

Campos serializados: `expo_profiles`, `amp_profiles`, `xmp_profiles`,
`jedec_timings`, `min_trfc_ns`, `min_tras_ns`, `min_trcd_ns`, `min_trp_ns`.

O que o binário **não** tem: nenhum offset de byte do SPD (a leitura do SPD
é feita pelo driver e devolvida pelo `DeviceIoControl`).

## 7. O que a análise estática não entregou — e como foi resolvido

A análise estática do CPU-Z **não** entrega os offsets do SPD, porque o
binário não os traz: ele só exibe o que o driver devolveu. Três itens
ficaram pendentes na primeira versão deste documento:

1. **O BDF e o mapa de registradores do host SMBus** — resolvido sem
   engenharia reversa: o módulo PawnIO do próprio projeto (PIIX4 para o AMD
   FCH, I801 para a Intel) faz a busca do controlador. Não é preciso varrer
   o configspace PCI à mão.
2. **A sequência exata da transação SMBus** — resolvida pelo código público
   do PawnIO e do RAMSPDToolkit: escrita de byte-data no endereço de
   controle de página (0x36/0x37) e leitura de word para os dados.
3. **Os offsets dos registradores de timing do UMC do AMD** (os timings
   *ativos*) — **este continua de fora**: o app mostra o que o módulo
   declara (JEDEC e XMP 2.0) e o clock que a BIOS configurou (via SMBIOS),
   mas não lê os registradores do controlador de memória. É a diferença
   entre o que o módulo *oferece* e o que está *ligado agora*.

## 8. Como ficou (implementado na v0.22)

| Peça | Onde | O que faz |
|---|---|---|
| `PawnIoTransporte.cs` | app | `DeviceIoControl` no driver PawnIO: carrega módulo e chama função |
| `SmbusPawnIo.cs` | app | transações SMBus, seleção de porta do AMD FCH, mutex de serialize |
| `SpdLeitor.cs` | app | lê as 2 páginas de 256 bytes de cada módulo e varre 0x50–0x57 |
| `DecodificadorSpd.cs` | app | decodifica o SPD: identidade, capacidade, JEDEC, XMP 2.0 |
| `MemoriaRegras.cs` | app | classifica: perfil ativo, padrão JEDEC, overclock manual, acima do perfil |
| `MainWindow.Memoria.cs` | app | a aba TRANAL MEMÓRIA |
| `MemoriaCli.cs` | app | `TranalSentinel.exe --memoria` (mesmo caminho, sem janela) |
| `TranalSentinel.Tests/SpdTestes.cs` | testes | 17 testes sobre os 512 bytes reais do módulo |

### Prova real (PC do owner)

Asgard VALKYRIE 32 GB (2×16 GB DDR4-3200) em Ryzen 7 5700X / AM4:

- Endereços SMBus `0x52` e `0x53` → 2 módulos, 512 bytes cada.
- JEDEC: **DDR4-2400**, CL 16, tAA 13,75 ns, tRAS 32 ns, tRC 45,75 ns,
  tFAW 21 ns.
- Perfil **XMP 2.0**: **3200 MT/s**, CL 18, tRAS 42 ciclos, 1,35 V.
- Os 512 bytes viram a fixture dos testes (`TranalSentinel.Tests/Dados/`).

Os números batem com o que o CPU-Z mostra para o mesmo módulo
(`valid.x86.fr/pecrxz`: "Asgard 16384 MB (DDR4-2400) - XMP 2.0", timings
ativos 18-20-20-42).

### Mapa do DDR4 (conferido contra as folhas de SPD da Samsung)

As folhas de SPD oficiais da Samsung (M391A1K43BB1-CRC00, um UDIMM
DDR4-2400) têm os mesmos bytes do módulo do owner, e por isso serviram
para conferir o decodificador inteiro:

| Byte | Campo | Valor no módulo do owner |
|---|---|---|
| 0x02 | Tipo de DRAM | 0x0C (DDR4) |
| 0x03 | Tipo de módulo | 0x02 (UDIMM) |
| 0x04 | Densidade e bancos | 0x86 (16 Gb, 4 BG, 4 bancos) |
| 0x05 | Endereçamento | 0x29 (18 linhas, 10 colunas) |
| 0x0B | Tensão nominal | 0x03 (1,2 V) |
| 0x0C | Organização do módulo | 0x01 (1Rx8) |
| 0x0D | Largura do barramento | 0x03 (64 bits, sem ECC) |
| 0x11 | Grades de tempo | 0x00 (MTB 125 ps, FTB 1 ps) |
| 0x12 | tCK mínimo | 0x07 (875 ps → 2400 MT/s) |
| 0x18 | tAA mínimo | 0x6E (110 → 13,75 ns) |
| 0x1B | Nibbles de tRAS/tRC | 0x11 (tRAS 32 ns, tRC 45,75 ns) |

Duas decisões de decodificação que vieram dessa conferência:

- **O tCK do JEDEC é um byte só** (0x12), não dois. Por isso a velocidade
  lida é sempre um pouco menor que a nominal (7 → 875 ps → 2286 MT/s), e
  o decodificador casa o valor com a velocidade padrão mais próxima acima.
- **O tipo de módulo está no byte 0x03**, não no 0x0E (esse é o sensor
  térmico).

### Princípio (mantido)

> A regra fica em um lugar só e o resto consome. O diagnóstico continua
> **somente leitura**: ler o SPD é leitura; escrever no controlador de
> memória **não** entra no produto.

### O que ainda não entra

- **EXPO**: o cabeçalho "EXPO" fica no offset 768 (1024 bytes de SPD). O
  módulo do owner tem XMP 2.0, não EXPO; o decodificador de EXPO fica
  para quando houver um módulo EXPO para testar.

O driver PawnIO **não** é um bloqueio: o app já o instala com um clique
(mesmo caminho usado para a temperatura da CPU), então a aba TRANAL MEMÓRIA
funciona em qualquer máquina onde os sensores de CPU já funcionam.


## 8-bis. Timings ativos (o que a BIOS programou) — implementado na v0.25

O que o SPD oferece é o **papel**; o que o controlador está fazendo é a
**reality**. A diferença entre os dois é o que explica travamento: tempo
mais apertado do que o perfil é ajuste manual, e é a causa mais comum de
instabilidade por memória.

### O caminho

O módulo PawnIO do AMD (`AMDFamily17.bin`, do projeto PawnIO.Modules)
exporta `ioctl_read_smn`, que lê os registradores do **SMN** (System
Management Network) do processador — o mesmo caminho que o `ryzen_smu` usa
no Linux. O módulo cobre as famílias **17h a 1Ah** (Zen 2, Zen 3 e Zen 4) e
aceita o processador do owner, que é **Zen 3** (família 19h, modelo 21h).

Cada handle do driver PawnIO guarda o seu próprio módulo carregado, então o
leitor de SMN e o leitor de SMBus convivem na mesma máquina.

**Só leitura.** As funções de escrita do módulo (`ioctl_write_msr` e
afins) não são usadas em lugar nenhum.

### O mapa dos registradores (bloco do UMC)

O bloco vive em `0x50200` (canal 0) e `0x150200` (canal 1). O canal 0
responde `0x300` quando está vazio — nesse caso o bloco de verdade é o do
canal 1 (regra do ryzen_smu).

| Endereço | Conteúdo |
|---|---|
| `0x50100` | tipo de memória (bits 1-0: 00 = DDR4, 01 = DDR5) |
| `0x50200` | clock da memória (bits 6-0), taxa de comando (bit 10), gear down (bit 11) |
| `0x50204` | tCL, tRAS, tRCD de leitura, tRCD de escrita |
| `0x50208` | tRC, tRP |
| `0x5020C` | tRRDS, tRRDL, tRTP |
| `0x50210` | tFAW |
| `0x50214` | tCWL, tWTRS, tWTRL |
| `0x50218` | tWR |
| `0x50230` | tREFI (o intervalo de refresh) |
| `0x50250` | tSTAG |
| `0x50254` | tCKE |
| `0x50050` / `0x50058` | troca de banco/grupo de bancos |

Referências: `ryzen_smu` (`userspace/monitor_cpu.c`) e o `zencli.c` do
projeto MemTimings-Linux — as duas descrevem o mesmo bloco.

### O que foi medido no PC do owner

Ryzen 7 5700X, 2×16 GB Asgard 3200 (XMP 2.0), BIOS com o clock em 3466 MT/s:

| Tempo | Ativo (ciclos) | Perfil XMP 2.0 (ciclos) | Situação |
|---|---|---|---|
| tCL | 18 | 18 | igual |
| tRCD | 22 | 22 | igual |
| tRP | 22 | 22 | igual |
| tRAS | 42 | 42 | igual |
| tRC | 80 | 67 | mais solto |
| tFAW | 37 | 34 | mais solto |

Clock lido: 1733 MHz = 3466 MT/s, 1T, gear down ligado. tREFI: 13495
ciclos = 7786 ns ≈ 7,8 µs — o valor canônico do DDR4.

**O veredito:** os tempos ativos são exatamente os do perfil XMP 2.0. A BIOS
aplicou o perfil e só subiu o clock de 3200 para 3466 MT/s. Não há ajuste
manual nos tempos.

### Duas coisas que a análise estática não deu, e como foram resolvidas

1. **O tRFC não é mostrado.** Em `0x50260` está o valor `0x21060138`, que é
   o *default* do registrador, e em `0x50264` o valor `0x7092F3B8`. As duas
   referências discordam sobre o que extrair: o `ryzen_smu` tem uma
   regra de sentinela que troca um pelo outro; o `zencli.c` usa a mesma
   regra para DDR4 e lê quatro registradores para DDR5. Aplicando as duas,
   os valores ficam em 312/192/132 (ordenados ao contrário do esperado) ou
   em 952/606/898 (também ao contrário). Nenhum dos dois conjuntos é
   plausível para tRFC1/tRFC2/tRFC4, que precisam crescer. Como não dá para
   validar nesta máquina, o app **não mostra tRFC** — número ausente é
   melhor que número errado. Os valores crus estão na fixture
   `RegistrosUmcRyzen5700X`, prontos para quando houver como conferir.
2. **O segundo canal é alcançável.** A sonda inicial parecia ler o mesmo
   valor em `0x50200` e `0x150200`, o que sugeria máscara de endereço.
   Não é: `0x250200` devolve `0xFFFFFFFF` (endereço inexistente), e os dois
   canais têm valores **idênticos** porque os dois módulos são iguais.


## 9. Alternativas avaliadas

| Opção | Custo | Rendimento |
|---|---|---|
| Ler o SPD com PawnIO (plano acima) | médio | dado real do seu PC, sem dependência |
| Rodar o CPU-Z e importar o report | baixo, mas instala o driver dele | não serve: o produto não pode depender de terceiro |
| Referência pública (JEDEC + AMD AGESA/registers) | baixo | cobre o SPD; o resto depende do que a doc publica |
| Descompilador (Ghidra) | alto (JDK + ~400 MB na sua máquina) | pins os offsets do UMC do AMD |

## 10. Registro da análise

- Ferramentas: REA (`inspect_pe_resources`, `inspect_pe_imports`,
  `inspect_native_exports` — as portáteis; os providers de análise profunda
  não estavam configurados nesta máquina) e um extrator de strings em Python.
- Nada do CPU-Z foi instalado nem executado no PC: a análise do binário foi
  100 % estática. O que **foi** executado no PC foi a **sonda do PawnIO**,
  escrita do zero a partir do protocolo público do driver: ela abriu o
  dispositivo, carregou o módulo `SmbusPIIX4` e leu o SPD dos dois módulos
  de memória do owner. Nada foi alterado: só leitura do SPD.
- Os binários baixados ficam fora do repositório, em
  `workspace/_rea/cpuz/`.
