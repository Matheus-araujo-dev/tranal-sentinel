using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Management;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace TranalSentinel;

/// <summary>
/// Coleta de dados do sistema para o diagnóstico de saúde.
///
/// Tudo aqui é somente leitura: log de eventos, WMI, registro e processos.
/// Nenhuma configuração é alterada, nada é instalado, nada é reiniciado.
/// </summary>
internal static class SaudeColeta
{
    /// <summary>Coleta tudo e devolve a fotografia do sistema.</summary>
    public static ColetaSaude Coletar()
    {
        var dados = new ColetaSaude();
        ColetarTravamentos(dados);
        ColetarHardware(dados);
        ColetarPcie(dados);
        ColetarDiscos(dados);
        ColetarSmart(dados);
        ColetarAntivirus(dados);
        ColetarMemoria(dados);
        ColetarLentidao(dados);
        return dados;
    }

    // ---------------------------------------------------------------- travamentos

    private static void ColetarTravamentos(ColetaSaude dados)
    {
        var inicio = DateTime.Now.AddDays(-90);

        // Os desligamentos inesperados (evento 6008): cada um diz a HORA REAL em
        // que a máquina parou. O Kernel-Power 41 só diz quando o Windows voltou
        // a subir — se o dono só reinicia o PC horas depois, o 41 não serve
        // para saber a hora do congelamento.
        var desligamentos = new List<(DateTime Boot, DateTime? Instante)>();

        // Uma passada só pelo log System: travamentos, WHEA e resets de vídeo.
        SaudeEventos.Ler("System", registro =>
        {
            var quando = registro.TimeCreated ?? DateTime.MinValue;
            if (quando < inicio) return;

            if (registro.Id == 41 && registro.ProviderName == "Microsoft-Windows-Kernel-Power")
            {
                var e = SaudeEventos.Converter(registro);

                // Layout real do Kernel-Power 41 no Windows 11:
                //   [0] BugcheckCode   [6] PowerButtonTimestamp   [8] Checkpoint
                var bugcheck = e.Valor(0) ?? "0";
                var botao = e.Valor(6) ?? "0";

                dados.Travamentos.Add(new TravamentoInfo
                {
                    Data = e.Data,
                    Bugcheck = string.IsNullOrWhiteSpace(bugcheck) ? "0" : bugcheck,
                    Detalhe = botao == "0"
                        ? "Sem botão de energia (travou sozinho)"
                        : $"Botão de energia em {botao}"
                });
            }
            else if (registro.Id == 6008)
            {
                var e = SaudeEventos.Converter(registro);
                desligamentos.Add((e.Data, LerInstanteDesligamento(e)));
            }
            else if (registro.ProviderName == "Microsoft-Windows-WHEA-Logger")
            {
                dados.EventosWhea++;
            }
            else if (registro.Id == 4101)
            {
                dados.ResetsDriverVideo++;
            }
        });

        // Cada travamento ganha a hora em que a máquina parou (quando o Windows
        // registrou o desligamento inesperado). É ela que ancora a janela do
        // log de sensores: sem ela, o app diria "não estava rodando" num
        // congelamento em que estava.
        foreach (var travamento in dados.Travamentos)
        {
            travamento.InstanteDesligamento = DesligamentoDoBoot(travamento.Data, desligamentos);
        }

        // Quem falhou na janela de cada travamento: costuma ser o app que roda no
        // boot, não a causa do congelamento.
        //
        // A conta é por TRAVAMENTO (e não por evento): o mesmo app pode gerar
        // 1000 e 1002 na mesma janela, e aí contaria duas vezes. O script de
        // linha de comando conta do mesmo jeito.
        var falhas = new List<(string App, DateTime Data)>();
        SaudeEventos.Ler("Application", registro =>
        {
            if (registro.Id != 1000 && registro.Id != 1002) return;

            var e = SaudeEventos.Converter(registro);
            if (e.Data < inicio) return;

            var app = e.Valor(0) ?? "";
            if (!EhNomeDeExecutavel(app)) return;

            falhas.Add((app, e.Data));
        });

        var contagem = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var travamento in dados.Travamentos)
        {
            var appsDaJanela = falhas
                .Where(f => Math.Abs((f.Data - travamento.Data).TotalMinutes) <= 20)
                .Select(f => f.App)
                .Distinct(StringComparer.OrdinalIgnoreCase);

            foreach (var app in appsDaJanela)
                contagem[app] = contagem.TryGetValue(app, out var n) ? n + 1 : 1;
        }

        foreach (var (app, vezes) in contagem)
            dados.AppsQueFalharam.Add(new AppFalhouInfo
            {
                Nome = app,
                Vezes = vezes,
                AindaInstalado = ProgramaInstalado(app)
            });

        AnexarRetrospectivas(dados, inicio);
    }

    /// <summary>
    /// O instante em que a máquina parou, lido do evento 6008 ("o desligamento
    /// anterior às HH:MM:SS do dia DD/MM/AAAA não era esperado").
    ///
    /// <para>
    /// O texto vem no formato do Windows (e com marcas de direção invisíveis
    /// no meio). A data é ambígua quando o dia e o mês são dois números
    /// pequenos: nesse caso segue o padrão curto do Windows atual.
    /// </para>
    /// </summary>
    internal static DateTime? LerInstanteDesligamento(EventoWindows e)
    {
        string hora = Limpar(e.Valor(0));
        string data = Limpar(e.Valor(1));
        if (hora.Length == 0 || data.Length == 0) return null;

        string[] partes = data.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (partes.Length != 3) return null;
        if (!int.TryParse(partes[0], out int primeiro) ||
            !int.TryParse(partes[1], out int segundo) ||
            !int.TryParse(partes[2], out int ano)) return null;

        int dia, mes;
        if (primeiro > 12) { dia = primeiro; mes = segundo; }
        else if (segundo > 12) { mes = primeiro; dia = segundo; }
        else
        {
            // Dia e mês ambíguos: quem decide é o Windows (o evento usa o
            // formato curto da configuração regional).
            string padrao = CultureInfo.CurrentCulture.DateTimeFormat.ShortDatePattern;
            int posDia = padrao.IndexOf('d', StringComparison.OrdinalIgnoreCase);
            int posMes = padrao.IndexOf('M', StringComparison.OrdinalIgnoreCase);
            bool diaPrimeiro = posMes < 0 || (posDia >= 0 && posDia < posMes);
            if (diaPrimeiro) { dia = primeiro; mes = segundo; }
            else { mes = primeiro; dia = segundo; }
        }

        if (ano < 1900 || mes < 1 || mes > 12 || dia < 1 || dia > 31) return null;
        if (!TimeSpan.TryParse(hora, CultureInfo.InvariantCulture, out var momento)) return null;

        try
        {
            return new DateTime(ano, mes, dia, momento.Hours, momento.Minutes, momento.Seconds, DateTimeKind.Local);
        }
        catch (ArgumentOutOfRangeException)
        {
            // Dia que não existe no mês (o Windows às vezes escreve 31/02).
            return null;
        }
    }

    /// <summary>Tira as marcas de direção e os caracteres de controle do texto do evento.</summary>
    private static string Limpar(string? texto)
    {
        if (string.IsNullOrEmpty(texto)) return "";

        var sb = new System.Text.StringBuilder(texto.Length);
        foreach (char c in texto)
        {
            if (c == '\u200e' || c == '\u200f' || c == '\u202a' || c == '\u202b') continue;
            if (char.IsControl(c)) continue;
            sb.Append(c);
        }

        return sb.ToString().Trim();
    }

    /// <summary>
    /// O desligamento inesperado que corresponde a um boot: o Windows grava o
    /// 6008 logo depois do Kernel-Power 41 do mesmo boot.
    ///
    /// <para>
    /// A lista vem do log na ordem inversa (do mais novo para o mais antigo):
    /// o que é de outro boot é pulado, e o que é mais antigo que o boot
    /// encerra a busca (os próximos são mais antigos ainda).
    /// </para>
    /// </summary>
    internal static DateTime? DesligamentoDoBoot(DateTime boot, List<(DateTime Boot, DateTime? Instante)> desligamentos)
    {
        foreach (var (quando, instante) in desligamentos)
        {
            if (quando < boot) break;
            if (quando > boot.AddMinutes(15)) continue;
            if (instante == null) continue;
            return instante;
        }

        return null;
    }

    /// <summary>
    /// Cruza os travamentos com o log contínuo de sensores e escreve, em cada
    /// travamento, o que a máquina estava fazendo antes do congelamento.
    ///
    /// <para>
    /// É a parte que responde "por que travou": o evento Kernel-Power 41 diz
    /// que o PC foi desligado sem limpeza; o log de sensores diz a
    /// temperatura, a carga e o clock na hora.
    /// </para>
    /// </summary>
    private static void AnexarRetrospectivas(ColetaSaude dados, DateTime inicio)
    {
        try
        {
            // Sem travamento, não há o que correlacionar: nem precisa ler o log.
            if (dados.Travamentos.Count == 0) return;

            // Só interessa o log de antes do travamento mais antigo (com uma
            // folga): ler o arquivo inteiro a cada análise seria caro.
            var maisAntigo = dados.Travamentos
                .Select(t => t.InstanteDesligamento ?? t.Data)
                .Min();
            var desde = maisAntigo - TimeSpan.FromMinutes(15);
            if (desde < inicio) desde = inicio;

            var leitura = SensorHistorico.Ler(desde);

            // A retrospectiva é a correlação entre o travamento e o que os
            // sensores faziam. Sem o histórico, não há o que correlacionar:
            // o app diz que não sabe, em vez de inventar uma explicação.
            if (!leitura.Completa)
            {
                return;
            }

            var amostras = leitura.Linhas;
            var retrospectivas = CorrelatorTravamento.Correlacionar(dados.Travamentos, amostras);
            dados.Retrospectivas.AddRange(retrospectivas);

            // O texto vai para dentro do próprio travamento: a tela e o
            // relatório mostram a lista de travamentos, não a de retrospectivas.
            //
            // O pareamento é por índice: o correlator devolve uma retrospectiva
            // por travamento, na mesma ordem. Reemparelhar por proximidade
            // aqui duplicaria a regra (e erraria quando dois travamentos
            // aconteceram com menos de 2 minutos de distância).
            for (int i = 0; i < dados.Travamentos.Count && i < retrospectivas.Count; i++)
                dados.Travamentos[i].Retrospectiva = retrospectivas[i].Resumo;
        }
        catch
        {
            // O log de sensores é um extra: sem ele, o diagnóstico continua
            // funcionando igual (só não conta o que os sensores faziam).
        }
    }

    /// <summary>
    /// Procura o programa no registro de desinstalação para saber se ele ainda
    /// está instalado. Serve para não mandar o usuário investigate um programa
    /// que já foi desinstalado (o registro só cobre programas registrados, então
    /// false significa "não achei", não "não existe").
    /// </summary>
    private static bool? ProgramaInstalado(string nomeExe)
    {
        try
        {
            string alvo = nomeExe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                ? nomeExe[..^4]
                : nomeExe;
            if (alvo.Length < 3) return null;

            foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
            {
                foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
                {
                    using var chave = RegistryKey.OpenBaseKey(hive, view)
                        .OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
                    if (chave == null) continue;

                    foreach (var sub in chave.GetSubKeyNames())
                    {
                        using var item = chave.OpenSubKey(sub);
                        string? nome = item?.GetValue("DisplayName") as string;
                        if (string.IsNullOrWhiteSpace(nome)) continue;
                        if (nome.IndexOf(alvo, StringComparison.OrdinalIgnoreCase) >= 0) return true;
                    }
                }
            }
            return false;
        }
        catch { return null; }
    }

    /// <summary>Eventos 1000/1002 trazem o nome do app; mensagens soltas não interessam.</summary>
    private static bool EhNomeDeExecutavel(string nome) =>
        !string.IsNullOrWhiteSpace(nome) &&
        nome.Length < 100 &&
        nome.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);

    // ------------------------------------------------------------------ hardware

    private static void ColetarHardware(ColetaSaude dados)
    {
        // WHEA e resets de vídeo são contados na mesma passagem pelo log System.
        try
        {
            dados.Minidumps = Directory.Exists(@"C:\Windows\Minidump")
                ? Directory.GetFiles(@"C:\Windows\Minidump", "*.dmp").Length
                : 0;
        }
        catch { }
    }

    // ---------------------------------------------------------------------- PCIe

    private static void ColetarPcie(ColetaSaude dados)
    {
        try
        {
            var (ac, dc) = LerAspm();
            dados.AspmAc = ac;
            dados.AspmDc = dc;
        }
        catch { }

        try
        {
            ColetarNvmes(dados);
        }
        catch { }
    }

    /// <summary>
    /// Índices de ASPM (0 = desligado).
    ///
    /// O powercfg escreve o rótulo traduzido ("Correntes Alternadas" / "Correntes Contínuas"),
    /// que muda conforme o idioma e a codepage do console. As duas linhas de valor sempre
    /// saem na mesma ordem (alternada primeiro, contínua depois), então lemos pela ordem
    /// em vez de caçar o texto traduzido.
    /// </summary>
    private static (int? Ac, int? Dc) LerAspm()
    {
        var texto = ExecutarPowerCfg("/query SCHEME_CURRENT SUB_PCIEXPRESS ASPM");
        if (string.IsNullOrWhiteSpace(texto)) return (null, null);

        var valores = new List<int>();
        foreach (var linha in texto.Split('\n'))
        {
            var m = Regex.Match(linha, @"(0x[0-9A-Fa-f]{1,8})\s*$");
            if (!m.Success) continue;
            valores.Add(Convert.ToInt32(m.Groups[1].Value, 16));
        }

        if (valores.Count == 0) return (null, null);
        return (valores[0], valores.Count > 1 ? valores[1] : null);
    }

    private static string ExecutarPowerCfg(string argumentos)
    {
        try
        {
            var info = new ProcessStartInfo("powercfg.exe", argumentos)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var processo = Process.Start(info);
            if (processo == null) return "";
            var saida = processo.StandardOutput.ReadToEnd();
            processo.WaitForExit(5000);
            return saida;
        }
        catch { return ""; }
    }

    /// <summary>
    /// Levanta os NVMe do PC com a topologia de onde cada um está pendurado:
    /// disco → controller NVMe → ponte PCIe → caminho de localização.
    ///
    /// Quem julga se há placa adaptadora é a regra; aqui são só fatos.
    /// </summary>
    private static void ColetarNvmes(ColetaSaude dados)
    {
        using var busca = new ManagementObjectSearcher(
            "SELECT Name, DeviceID, Status FROM Win32_PnPEntity WHERE PNPClass='DiskDrive'");

        foreach (ManagementObject disco in busca.Get())
        {
            var nome = disco["Name"]?.ToString() ?? "";
            if (!nome.Contains("NVMe", StringComparison.OrdinalIgnoreCase) &&
                !nome.Contains("NVM Express", StringComparison.OrdinalIgnoreCase)) continue;

            // O pai do disco é o controller NVMe; o pai do controller é a ponte
            // onde ele está de fato pendurado.
            string controller = PropriedadePnp(disco["DeviceID"]?.ToString() ?? "", "DEVPKEY_Device_Parent") ?? "";
            string paiDoController = controller.Length > 0
                ? PropriedadePnp(controller, "DEVPKEY_Device_Parent") ?? ""
                : "";
            string localizacao = controller.Length > 0
                ? PropriedadePnpMultiplas(controller, "DEVPKEY_Device_LocationPaths") ?? ""
                : "";

            dados.Nvmes.Add(new InfoNvme
            {
                Nome = nome,
                Status = disco["Status"]?.ToString() ?? "",
                Controller = controller,
                PaiDoController = paiDoController,
                Localizacao = localizacao
            });
        }
    }

    /// <summary>Lê uma DEVPKEY de um dispositivo PnP (mesma técnica usada pelo GpuInfoCollector).</summary>
    private static string? PropriedadePnp(string deviceId, string chave)
    {
        return PropriedadePnpBruta(deviceId, chave)?.ToString();
    }

    /// <summary>
    /// Lê uma DEVPKEY que pode ter vários valores (LocationPaths, por exemplo) e
    /// devolve tudo numa string só, separado por "; ".
    /// </summary>
    private static string? PropriedadePnpMultiplas(string deviceId, string chave)
    {
        object? bruto = PropriedadePnpBruta(deviceId, chave);
        if (bruto is string[] varios) return string.Join("; ", varios);
        return bruto?.ToString();
    }

    private static object? PropriedadePnpBruta(string deviceId, string chave)
    {
        if (deviceId.Length == 0) return null;
        try
        {
            using var busca = new ManagementObjectSearcher(
                $"SELECT * FROM Win32_PnPEntity WHERE DeviceID='{deviceId.Replace("\\", "\\\\")}'");
            var objeto = busca.Get().Cast<ManagementObject>().FirstOrDefault();
            if (objeto == null) return null;

            var entrada = objeto.GetMethodParameters("GetDeviceProperties");
            entrada["devicePropertyKeys"] = new[] { chave };
            var saida = objeto.InvokeMethod("GetDeviceProperties", entrada, null);
            if (saida?["deviceProperties"] is ManagementBaseObject[] propriedades)
                foreach (var p in propriedades)
                    if (p["KeyName"] is string k && k == chave)
                        return p["Data"];
        }
        catch { }
        return null;
    }

    // -------------------------------------------------------------------- discos

    private static void ColetarDiscos(ColetaSaude dados)
    {
        try
        {
            foreach (var linha in Wmi.Consultar("SELECT Model, PNPDeviceID, Status, Size FROM Win32_DiskDrive"))
            {
                dados.Discos.Add(new DiscoInfo
                {
                    Modelo = linha["Model"],
                    Status = linha["Status"],
                    PnpDeviceId = linha["PNPDeviceID"],
                    TamanhoBytes = linha.ValorLong("Size")
                });
            }
        }
        catch { }

        try
        {
            foreach (var orfao in SaudePnp.OrfaosArmazenamento())
                dados.Orfaos.Add(orfao);
        }
        catch { }
    }

    /// <summary>
    /// O S.M.A.R.T. de cada disco (a tabela de atributos).
    ///
    /// <para>
    /// Só leitura: o SMART é lido, nunca enviado. Disco que não expõe SMART
    /// aparece na lista com a observação dizendo por quê — e, sem dado, não
    /// entra nenhum achado novo no diagnóstico.
    /// </para>
    /// </summary>
    private static void ColetarSmart(ColetaSaude dados)
    {
        try
        {
            foreach (var disco in SmartLeitor.LerTodos())
                dados.DiscosSmart.Add(disco);
        }
        catch { }
    }

    // ---------------------------------------------------------------- segurança

    private static void ColetarAntivirus(ColetaSaude dados)
    {
        try { dados.ModoDefender = LerDefenderModo(); } catch { }
        try { dados.AntivirusRegistrado = LerAntivirusRegistrado(); } catch { }
        try { LerDriverAntivirus(dados); } catch { }
        try { dados.AntivirusMemoriaMb = MemoriaAntivirusMb(); } catch { }
        try { (dados.DefinicoesAntivirusDias, dados.DefinicoesAntivirusData) = IdadeDefinicoes(); } catch { }
    }

    /// <summary>Modo do Defender: Active, Passive, Not running, Disabled.</summary>
    private static string? LerDefenderModo()
    {
        try
        {
            // O WMI clássico não aceita namespace no FROM: o caminho vai pelo scope.
            var escopo = new ManagementScope(@"root\Microsoft\Windows\Defender");
            escopo.Connect();

            using var busca = new ManagementObjectSearcher(escopo, new ObjectQuery("SELECT AMRunningMode FROM MSFT_MpComputerStatus"));
            using var objetos = busca.Get();
            foreach (ManagementObject o in objetos)
            {
                var v = o["AMRunningMode"];
                if (v == null) continue;
                return ModoDoDefender(v);
            }
        }
        catch { }
        return null;
    }

    /// <summary>
    /// Normaliza o AMRunningMode do Defender.
    /// No WMI atual o valor já vem como texto ("Active", "Passive", "Not running",
    /// "Disabled"); em versões antigas vinha como número (1 a 4).
    /// </summary>
    private static string ModoDoDefender(object valor)
    {
        var texto = valor.ToString() ?? "";

        if (texto is "Active" or "Passive" or "Not running" or "Disabled")
            return texto;

        if (int.TryParse(texto, out var numero))
            return numero switch
            {
                1 => "Active",
                2 => "Passive",
                3 => "Not running",
                4 => "Disabled",
                _ => texto
            };

        return texto;
    }

    /// <summary>
    /// Antivírus de terceiros registrado no Security Center do Windows.
    /// É a fonte mais confiável para saber se há um antivírus de verdade instalado:
    /// o Defender some da lista quando outro produto assume a proteção.
    /// </summary>
    private static string? LerAntivirusRegistrado()
    {
        try
        {
            foreach (var linha in Wmi.ConsultarEm(@"root\SecurityCenter2", "SELECT displayName, productState FROM AntiVirusProduct"))
            {
                var nome = linha["displayName"];
                if (nome.Length == 0) continue;
                if (nome.Contains("Defender", StringComparison.OrdinalIgnoreCase)) continue;
                return nome;
            }
        }
        catch { }
        return null;
    }

    /// <summary>Driver de filtro de kernel de antivírus de terceiros (o mfesec.sys do McAfee).</summary>
    private static void LerDriverAntivirus(ColetaSaude dados)
    {
        try
        {
            foreach (var linha in Wmi.Consultar("SELECT Name, State, PathName FROM Win32_SystemDriver"))
            {
                var caminho = linha["PathName"];
                if (!caminho.Contains("mfesec.sys", StringComparison.OrdinalIgnoreCase)) continue;

                dados.DriverAntivirus = linha["Name"];
                dados.EstadoDriverAntivirus = linha["State"];
                return;
            }
        }
        catch { }
    }

    /// <summary>Memória ocupada pelos processos do antivírus.</summary>
    private static int MemoriaAntivirusMb()
    {
        try
        {
            long soma = 0;
            foreach (var p in Process.GetProcesses())
            {
                try
                {
                    var nome = p.ProcessName;
                    if (!nome.StartsWith("mc-", StringComparison.OrdinalIgnoreCase) &&
                        !nome.StartsWith("mfe", StringComparison.OrdinalIgnoreCase) &&
                        !nome.StartsWith("mcafee", StringComparison.OrdinalIgnoreCase)) continue;
                    soma += p.WorkingSet64;
                }
                finally { p.Dispose(); }
            }
            return (int)Math.Round(soma / 1024.0 / 1024.0);
        }
        catch { return 0; }
    }

    /// <summary>Idade das definições de antivírus (dias) e data da última atualização.</summary>
    private static (int? Dias, string Data) IdadeDefinicoes()
    {
        try
        {
            var raiz = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "McAfee", "wps");
            if (!Directory.Exists(raiz)) return (null, "");

            var arquivo = Directory.EnumerateFiles(raiz, "av.list", SearchOption.AllDirectories)
                                       .OrderByDescending(File.GetLastWriteTime)
                                       .FirstOrDefault();
            if (arquivo == null) return (null, "");

            var quando = File.GetLastWriteTime(arquivo);
            return ((int)Math.Round((DateTime.Now - quando).TotalDays), quando.ToString("dd/MM/yyyy"));
        }
        catch { return (null, ""); }
    }

    // ------------------------------------------------------------------- memória

    private static void ColetarMemoria(ColetaSaude dados)
    {
        try
        {
            long soma = 0;
            foreach (var p in Process.GetProcessesByName("Memory Compression"))
            {
                try { soma += p.WorkingSet64; } finally { p.Dispose(); }
            }
            dados.MemoriaCompressaoMb = (int)Math.Round(soma / 1024.0 / 1024.0);
        }
        catch { }

        try
        {
            foreach (var linha in Wmi.Consultar("SELECT DeviceLocator, Capacity, Speed, ConfiguredClockSpeed, Manufacturer FROM Win32_PhysicalMemory"))
            {
                dados.ModulosMemoria.Add(new ModuloMemoria
                {
                    Slot = linha["DeviceLocator"],
                    Fabricante = linha["Manufacturer"],
                    CapacidadeBytes = linha.ValorLong("Capacity"),
                    VelocidadeMHz = (int)linha.ValorLong("Speed"),
                    VelocidadeConfigMHz = (int)linha.ValorLong("ConfiguredClockSpeed")
                });
            }
        }
        catch { }

        // O SPD dos módulos (capacidades, timings e perfis XMP/EXPO) vem do
        // barramento SMBus. Sem o driver PawnIO a lista fica vazia e as
        // regras de memória continuam funcionando com o que o Windows informa.
        try
        {
            MemoriaColetaResultado memoria = MemoriaColeta.Coletar();
            dados.ModulosSpd.AddRange(memoria.Modulos);
            dados.TimingsAtivosMemoria = memoria.TimingsAtivos;
        }
        catch { }
    }

    // ------------------------------------------------------------------ lentidão

    private static void ColetarLentidao(ColetaSaude dados)
    {
        try
        {
            var inicializacao = StartupManager.List();
            dados.AutostartTotal = inicializacao.Count;
            dados.AutostartAtivos = inicializacao.Count(e => e.Enabled);
        }
        catch { }

        try
        {
            foreach (var linha in Wmi.Consultar("SELECT Name, DisplayName, StartMode, PathName FROM Win32_Service"))
            {
                if (!linha["StartMode"].Equals("Auto", StringComparison.OrdinalIgnoreCase)) continue;
                dados.ServicosAuto.Add(new ServicoInfo
                {
                    Nome = linha["Name"],
                    ModoInicializacao = linha["StartMode"],
                    Caminho = linha["PathName"]
                });
            }
        }
        catch { }

        try
        {
            foreach (var linha in Wmi.Consultar("SELECT HypervisorPresent FROM Win32_ComputerSystem"))
                dados.HypervisorAtivo = linha.ValorBool("HypervisorPresent");
        }
        catch { }
    }

    // ---------------------------------------------------------------- infraestrutura
    //
    // O acesso ao WMI (Wmi.cs) e a leitura de programas instalados são compartilhados
    // com a linha do tempo do PC: uma única implementação, para as duas telas não
    // divergirem sobre o que o Windows está fazendo.
}
