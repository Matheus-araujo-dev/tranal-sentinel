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

## 7. O que falta (offsets do AMD AM5)

O que a análise estática **não** entrega, porque exige descompilar código:

1. **O BDF e o mapa de registradores do host SMBus no AM5** (Ryzen 7000).
   O binário do exe imprime `SMBus, base address = 0x%X%X` — ele **descobre**
   a base em tempo de execução. Para nós, isso significa: procurar o
   controlador SMBus varrendo o configspace PCI (é o que o CPU-Z faz), e não
   fixar um BDF.
2. **Os offsets dos registradores de timing do UMC do AMD** (onde estão
   CL/tRCD/tRP/tRAS/tRC/tRFC efetivos).
3. **A sequência exata da transação SMBus** (o protocolo SMBus 2.0 "I2C block
   read" com o endereço `0x50` do DIMM), incluindo os atrasos.

O item 1 é o mais importante e o mais fácil: a busca do controlador é
genérica (o exe tem uma classe `CSMBusDevice` justamente para isso).

## 8. Como implementar (plano)

1. **Leitor de SMBus por PawnIO** (`LeitorSmbus.cs`):
   - varre o configspace PCI atrás do host SMBus (AMD: dentro do Data Fabric;
     Intel: ICH), lendo o base address;
   - monta a transação SMBus 2.0 (endereço `0x50`, leitura de bloco).
2. **Leitor de SPD** (`LeitorSpd.cs`): lê os 512 bytes do SPD (DDR5) e decodifica
   pelo layout JEDEC (que é **público** — o binário do CPU-Z não traz os
   offsets, porque ele só exibe).
3. **Perfis**: XMP/EXPO/AMP ficam em páginas/offsets padrão do JEDEC
   (a "profile page"); EPP é campo do perfil.
4. **Timings ativos**: leitura dos registradores do controlador de memória
   (no AMD, o UMC) via configspace PCI.
5. **Apresentação**: uma aba "Memória" no TRANAL SAÚDE com a tabela de
   timings, o perfil ativo e a comparação com o que o SPD declara suportar.
6. **Regra de diagnóstico**: se o clock ativo estiver acima do que o perfil
   JEDEC permite, o TRANAL SAÚDE passa a apontar "EXPO/XMP ativo" (informação,
   não erro) — porque aí é escolha do usuário, não defeito.

### Princípio

> A regra fica em um lugar só e o resto consome. O diagnóstico continua
> **somente leitura**: ler o SPD é leitura; escrever no controlador de
> memória **não** entra no produto.

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
- Nada foi instalado no PC e nada foi executado: a análise foi 100 % estática
  sobre os arquivos baixados.
- Os binários baixados ficam fora do repositório, em
  `workspace/_rea/cpuz/`.
