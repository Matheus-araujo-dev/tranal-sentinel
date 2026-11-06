using System.Globalization;
using System.Management;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace TranalSentinel;

/// <summary>
/// Leitor de S.M.A.R.T.: a tabela de atributos de cada disco.
///
/// <para>
/// São dois caminhos, porque o Windows não expõe o SMART dos dois jeitos:
///
///   • discos ATA/SATA (SanDisk, Kingston...): o SMART chega pelo WMI, nas
///     classes MSStorageDriver_FailurePredict* do root\wmi;
///   • discos NVMe: o WMI não cobre — a página SMART/Health é lida direto do
///     disco, pelo IOCTL_STORAGE_QUERY_PROPERTY.
///
/// </para>
///
/// <para>
/// A leitura é somente leitura: o SMART é lido, nunca enviado. O SMART
/// write (que manda comandos ao disco) não existe aqui — nem deve.
///
/// </para>
///
/// <para>
/// A decodificação (<see cref="Decodificar"/>) é pura e não depende do
/// Windows: é ela que os testes exercitam com os bytes reais do hardware.
/// </para>
/// </summary>
public static class SmartLeitor
{
    /// <summary>Quantos atributos a tabela ATA comporta (o id 0x00 marca o fim).</summary>
    private const int MaximoAtributos = 30;

    /// <summary>De quantos bytes cada atributo é feito no registro.</summary>
    private const int TamanhoAtributo = 12;

    /// <summary>O offset do primeiro atributo dentro do registro de 512 bytes.</summary>
    private const int OffsetPrimeiro = 2;

    /// <summary>
    /// A máscara do bit que diz "este atributo é de pré-falha".
    ///
    /// <para>
    /// Descoberto nos bytes reais dos dois SanDisk SSD PLUS (240 GB e 1 TB):
    /// as flags chegam como 0x0032 (0b0011_0010), 0x0022 (0b0010_0010) e
    /// 0x0030 (0b0011_0000) na maioria dos atributos, e o único atributo com
    /// um bit diferente é o 0xE8 (232 = Remaining_Lifetime_Perc), que vem como
    /// 0x0033 (0b0011_0011) nos dois discos.
    ///
    /// </para>
    /// <para>
    /// A diferença entre 0x0032 e 0x0033 é justamente o bit 0 (0x0001), e ele
    /// aparece no único atributo que de fato prevê a falha do disco — a vida
    /// útil que resta. É o mesmo bit que o CrystalDiskInfo mostra na coluna
    /// TYPE como "Pre-fail". Os demais atributos (setores realocados, horas
    /// ligado, contadores do fabricante) são "old age": o valor normalizado
    /// ainda é informativo, mas não é o que define a falha.
    /// </para>
    /// </summary>
    private const int MascaraPreFail = 0x0001;

    /// <summary>O atributo que guarda os setores realocados pelo disco.</summary>
    private const int IdSetoresRealocados = 5;

    /// <summary>O atributo que guarda os setores pendentes (para realocar).</summary>
    private const int IdSetoresPendentes = 197;

    /// <summary>O atributo que guarda a vida útil que resta, em %.</summary>
    private const int IdVidaUtilRestante = 232;

    // ==================================================================== leitura

    /// <summary>
    /// Lê o S.M.A.R.T. de todos os discos do PC.
    ///
    /// <para>
    /// Nunca estoura exceção: a falha de leitura de um disco não pode
    /// derrubar o app. O que deu certo volta; o que não deu volta com a
    /// observação explicando por quê.
    /// </para>
    /// </summary>
    public static List<SmartDisco> LerTodos()
    {
        var lista = new List<SmartDisco>();
        try
        {
            // A mesma lista de discos que a página "Discos" usa (o Windows
            // não é consultado duas vezes).
            var discos = DiskHealth.LerDiscos(out _);
            if (discos.Count == 0) return lista;

            var instancias = LerInstanciasWmi();

            foreach (var disco in discos)
            {
                try { lista.Add(LerDisco(disco, instancias)); }
                catch { /* um disco problemático não derruba os outros */ }
            }
        }
        catch { }
        return lista;
    }

    /// <summary>Junta o que o WMI disse (dados, limiares e aviso de falha) por instância de disco.</summary>
    internal sealed class InstanciaSmart
    {
        public string InstanceName = "";
        public byte[] Dados = Array.Empty<byte>();
        public byte[] Limiares = Array.Empty<byte>();
        public bool PredictFailure;
    }

    /// <summary>
    /// Lê as três classes WMI do SMART (root\wmi) e devolve o que elas
    /// disseram, casado pelo InstanceName do disco.
    /// </summary>
    private static Dictionary<string, InstanciaSmart> LerInstanciasWmi()
    {
        var mapa = new Dictionary<string, InstanciaSmart>(StringComparer.OrdinalIgnoreCase);

        try
        {
            using var busca = new ManagementObjectSearcher(@"root\wmi", "SELECT InstanceName, VendorSpecific FROM MSStorageDriver_FailurePredictData");
            foreach (ManagementObject o in busca.Get())
            {
                string inst = o["InstanceName"]?.ToString() ?? "";
                if (inst.Length == 0) continue;
                if (!mapa.TryGetValue(inst, out var item)) mapa[inst] = item = new InstanciaSmart { InstanceName = inst };
                item.Dados = Bytes(o["VendorSpecific"]);
            }
        }
        catch { /* sem a classe de dados (ou sem permissão): segue sem SMART */ }

        try
        {
            using var busca = new ManagementObjectSearcher(@"root\wmi", "SELECT InstanceName, VendorSpecific FROM MSStorageDriver_FailurePredictThresholds");
            foreach (ManagementObject o in busca.Get())
            {
                string inst = o["InstanceName"]?.ToString() ?? "";
                if (inst.Length == 0) continue;
                if (!mapa.TryGetValue(inst, out var item)) mapa[inst] = item = new InstanciaSmart { InstanceName = inst };
                item.Limiares = Bytes(o["VendorSpecific"]);
            }
        }
        catch { }

        try
        {
            using var busca = new ManagementObjectSearcher(@"root\wmi", "SELECT InstanceName, PredictFailure FROM MSStorageDriver_FailurePredictStatus");
            foreach (ManagementObject o in busca.Get())
            {
                string inst = o["InstanceName"]?.ToString() ?? "";
                if (inst.Length == 0) continue;
                if (!mapa.TryGetValue(inst, out var item)) mapa[inst] = item = new InstanciaSmart { InstanceName = inst };
                item.PredictFailure = o["PredictFailure"] is bool b && b;
            }
        }
        catch { }

        return mapa;
    }

    /// <summary>
    /// Casa a instância de SMART com o disco. Sem correspondência, o disco
    /// fica "sem SMART" — o app nunca chuta qual tabela é de qual disco.
    /// </summary>
    internal static InstanciaSmart? InstanciaDoDisco(DiscoFisico disco, Dictionary<string, InstanciaSmart> instancias)
    {
        if (instancias.Count == 0) return null;

        // 1) pelo id do dispositivo: o PnP do disco e o instance name do SMART
        //    terminam no mesmo pedaço (o SMART é o PnP + "_0").
        string idDisco = NormalizaIdDispositivo(disco.PnpDeviceId);
        if (idDisco.Length > 0)
            foreach (var inst in instancias.Values)
                if (NormalizaIdDispositivo(inst.InstanceName) == idDisco) return inst;

        // 2) pelo modelo: o "Prod_" do instance name contra o modelo do disco
        //    (SCSI\Disk&Ven_SanDisk&Prod_SSD_PLUS_240GB\... -> "SSD_PLUS_240GB").
        string modelo = NormalizaTexto(disco.Modelo);
        if (modelo.Length > 0)
        {
            var candidatos = new List<InstanciaSmart>();
            foreach (var inst in instancias.Values)
            {
                string produto = NormalizaTexto(ProdutoDoInstanceName(inst.InstanceName));
                if (produto.Length == 0) continue;
                if (modelo.Contains(produto, StringComparison.Ordinal)) candidatos.Add(inst);
            }

            // Com mais de um candidato, o app não adivinha: o disco fica sem SMART.
            if (candidatos.Count == 1) return candidatos[0];
        }

        return null;
    }

    /// <summary>O id do dispositivo (o pedaço depois da última barra), sem o "_0" do SMART.</summary>
    private static string NormalizaIdDispositivo(string caminho)
    {
        if (string.IsNullOrEmpty(caminho)) return "";
        int corte = caminho.LastIndexOf('\\');
        string parte = corte >= 0 ? caminho[(corte + 1)..] : caminho;

        // O instance name do SMART acrescenta "_0" (ou outro número) no fim.
        int underscore = parte.LastIndexOf('_');
        if (underscore > 0 && parte[(underscore + 1)..].All(char.IsDigit))
            parte = parte[..underscore];

        return parte.ToUpperInvariant();
    }

    /// <summary>O produto do instance name ("Prod_SSD_PLUS_240GB" -> "SSD_PLUS_240GB").</summary>
    private static string ProdutoDoInstanceName(string instance)
    {
        const string marca = "&Prod_";
        int i = instance.IndexOf(marca, StringComparison.OrdinalIgnoreCase);
        if (i < 0) return "";
        int inicio = i + marca.Length;
        int fim = inicio;
        while (fim < instance.Length && instance[fim] != '&' && instance[fim] != '\\') fim++;
        return instance[inicio..fim];
    }

    /// <summary>Só letras e dígitos, em maiúsculas (para comparar nomes de modelo).</summary>
    private static string NormalizaTexto(string texto)
    {
        if (string.IsNullOrEmpty(texto)) return "";
        var sb = new System.Text.StringBuilder(texto.Length);
        foreach (char c in texto)
            if (char.IsLetterOrDigit(c)) sb.Append(char.ToUpperInvariant(c));
        return sb.ToString();
    }

    /// <summary>Junta o que o disco disse numa frase curta.</summary>
    private static SmartDisco LerDisco(DiscoFisico disco, Dictionary<string, InstanciaSmart> instancias)
    {
        var observacoes = new List<string>();
        var atributos = new List<SmartAtributo>();
        var contadores = new List<SmartContador>();
        bool falhaPrevista = false;
        int? vidaRestante = null;
        bool ehNvme = disco.EhNvme;

        // SATA/ATA: o SMART chega pelo WMI.
        var instancia = InstanciaDoDisco(disco, instancias);
        if (instancia != null)
        {
            atributos = Decodificar(instancia.Dados, instancia.Limiares);
            falhaPrevista = instancia.PredictFailure;
            vidaRestante = VidaRestanteAta(atributos);
        }
        else if (ehNvme)
        {
            observacoes.Add("O WMI do Windows não expõe o SMART dos discos NVMe (só dos ATA/SATA).");
        }
        else
        {
            observacoes.Add("Este disco não devolveu SMART pelo WMI (pode estar atrás de RAID/USB, ou o Windows não expôs a tabela).");
        }

        // NVMe: a página SMART/Health é lida direto do disco.
        string avisoCritico = "";
        if (ehNvme)
        {
            var leitura = LerNvme(disco.Index);
            contadores.AddRange(leitura.Contadores);
            if (leitura.VidaRestante.HasValue) vidaRestante = leitura.VidaRestante;
            if (leitura.AvisoCritico.Length > 0) avisoCritico = leitura.AvisoCritico;
            if (leitura.Observacao.Length > 0) observacoes.Add(leitura.Observacao);
        }

        string resumo = ResumoDoDisco(falhaPrevista, avisoCritico, atributos, vidaRestante, contadores.Count > 0);

        return new SmartDisco
        {
            Numero = disco.Index,
            Modelo = disco.Modelo,
            Serial = disco.Serial,
            CapacidadeBytes = disco.TamanhoBytes,
            Tipo = ehNvme ? "NVMe" : (disco.Barramento == "USB" ? "USB" : "SATA"),
            FalhaPrevista = falhaPrevista,
            AvisoCritico = avisoCritico,
            Atributos = atributos,
            Contadores = contadores,
            Resumo = resumo,
            Observacao = string.Join(" ", observacoes),
            VidaRestantePercent = vidaRestante,
        };
    }

    /// <summary>A vida útil que resta, pelo atributo 232 (Remaining_Lifetime_Perc).</summary>
    private static int? VidaRestanteAta(List<SmartAtributo> atributos)
    {
        foreach (var a in atributos)
        {
            if (a.Id != IdVidaUtilRestante) continue;
            long cru = SmartDisco.NumeroCru(a.Cru);
            // O cru é a porcentagem; quando o disco não preencheu o cru, o
            // valor normalizado é a mesma informação.
            if (cru > 0) return (int)Math.Min(cru, 100);
            if (a.Valor > 0) return Math.Min(a.Valor, 100);
        }
        return null;
    }

    /// <summary>Uma frase com a situação do disco.</summary>
    private static string ResumoDoDisco(bool falhaPrevista, string avisoCritico, List<SmartAtributo> atributos, int? vidaRestante, bool temNvme)
    {
        if (falhaPrevista) return "Falha prevista: o próprio disco avisa que vai falhar";
        if (avisoCritico.Length > 0) return "Atenção: o disco avisa " + avisoCritico;

        long realocados = ValorCru(atributos, IdSetoresRealocados);
        if (realocados > 0) return $"Atenção: {realocados} setor(es) realocado(s)";

        long pendentes = ValorCru(atributos, IdSetoresPendentes);
        if (pendentes > 0) return $"Atenção: {pendentes} setor(es) pendente(s)";

        if (vidaRestante is int vida && vida <= 10) return $"Atenção: {vida}% de vida útil restante";

        if (atributos.Count > 0 || temNvme) return "OK";
        return "Sem dados de SMART";
    }

    /// <summary>O valor cru (número) de um atributo da tabela.</summary>
    private static long ValorCru(List<SmartAtributo> atributos, int id)
    {
        foreach (var a in atributos)
            if (a.Id == id) return SmartDisco.NumeroCru(a.Cru);
        return 0;
    }

    /// <summary>Converte o que o WMI devolveu em bytes (aceita byte[] e hexadecimal em texto).</summary>
    private static byte[] Bytes(object? valor)
    {
        if (valor is byte[] b) return (byte[])b.Clone();

        if (valor is Array arr && arr.Rank == 1)
        {
            var saida = new byte[arr.Length];
            for (int i = 0; i < arr.Length; i++)
            {
                try { saida[i] = Convert.ToByte(arr.GetValue(i)); }
                catch { return Array.Empty<byte>(); }
            }
            return saida;
        }

        if (valor is string s && s.Length > 0)
        {
            string limpo = s.Replace(" ", "").Replace("\r", "").Replace("\n", "");
            if (limpo.Length % 2 == 0)
            {
                try
                {
                    var saida = new byte[limpo.Length / 2];
                    for (int i = 0; i < saida.Length; i++) saida[i] = Convert.ToByte(limpo.Substring(i * 2, 2), 16);
                    return saida;
                }
                catch { }
            }
        }

        return Array.Empty<byte>();
    }

    // ====================================================================== NVMe

    private const uint IoctlStorageQueryProperty = 0x2D1400;

    /// <summary>
    /// O id da propriedade "protocolo específico" (o log do NVMe).
    ///
    /// <para>
    /// O 48 é outra coisa (<c>StorageDeviceIoCapabilityProperty</c>): pedi-lo
    /// por engano devolve a descrição de capacidade de I/O, e não o log do
    /// disco. O 49 é o do protocolo.
    /// </para>
    /// </summary>
    private const int StorageDeviceProtocolSpecificProperty = 49;

    private const int PropertyStandardQuery = 0;
    private const byte ProtocolTypeNvme = 3;
    private const byte NvmeDataTypeLogPage = 1;
    private const byte NvmeLogPageSmartHealth = 0x02;
    private const uint GenericRead = 0x80000000;
    private const uint ShareReadWrite = 3;
    private const uint OpenExisting = 3;
    private const uint TamanhoPaginaNvme = 512;

    /// <summary>O tamanho do pedido específico de protocolo (40 bytes).</summary>
    private const int TamanhoPedidoNvme = 40;

    /// <summary>
    /// Onde o pedido específico de protocolo começa no buffer de entrada: é o
    /// campo <c>AdditionalParameters</c> da consulta, logo depois do
    /// cabeçalho de 8 bytes.
    /// </summary>
    private const int OffsetPedidoNvme = 8;

    /// <summary>
    /// A consulta de propriedade de armazenamento.
    ///
    /// <para>
    /// O cabeçalho tem 8 bytes (o id da propriedade e o tipo da consulta) e o
    /// pedido específico de protocolo vem **logo depois, no offset 8** — é o
    /// que a especificação do Windows diz: o buffer de entrada é o cabeçalho
    /// seguido do pedido.
    /// </para>
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct STORAGE_PROPERTY_QUERY
    {
        public int PropertyId;
        public int QueryType;
    }

    /// <summary>
    /// O pedido específico de protocolo (40 bytes, o layout do winioctl.h —
    /// o contrato que vale para quem chama de user-mode).
    ///
    /// <para>
    /// São dez <c>DWORD</c>: o tipo do protocolo, o tipo do dado, o valor e
    /// o subvalor pedidos, o offset e o tamanho dos dados, os dados fixos e
    /// mais três subvalores. Cada campo tem 4 bytes e o offset de um é
    /// importantíssimo: é um buffer de memória, não uma struct do C#.
    /// </para>
    ///
    /// <para>
    /// <c>ProtocolDataOffset</c> é o offset do payload dentro deste bloco
    /// (o valor típico é o próprio tamanho do bloco, 40) e
    /// <c>ProtocolDataLength</c> é o tamanho do payload (a página
    /// SMART/Health tem 512 bytes).
    /// </para>
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct STORAGE_PROTOCOL_SPECIFIC_DATA
    {
        public uint ProtocolType;                 // 0
        public uint DataType;                     // 4
        public uint ProtocolDataRequestValue;     // 8
        public uint ProtocolDataRequestSubValue;  // 12
        public uint ProtocolDataOffset;           // 16
        public uint ProtocolDataLength;           // 20
        public uint FixedProtocolReturnData;      // 24
        public uint ProtocolDataRequestSubValue2; // 28
        public uint ProtocolDataRequestSubValue3; // 32
        public uint ProtocolDataRequestSubValue4; // 36
    }

    /// <summary>
    /// O cabeçalho da resposta do IOCTL (48 bytes): a versão, o tamanho e o
    /// bloco do protocolo devolvido (que começa no offset 8).
    ///
    /// <para>
    /// No bloco devolvido, o offset do payload está no offset 24 e o
    /// tamanho no 28 — e o payload em si fica em 8 + offset.
    /// </para>
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct STORAGE_PROTOCOL_DATA_DESCRIPTOR
    {
        public int Version;              // 0
        public int Size;                 // 4
        public uint ProtocolType;        // 8
        public uint DataType;            // 12
        public uint RequestValue;        // 16
        public uint RequestSubValue;     // 20
        public uint ProtocolDataOffset;  // 24
        public uint ProtocolDataLength;  // 28
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeFileHandle CreateFile(
        string lpFileName, uint dwDesiredAccess, uint dwShareMode, IntPtr lpSecurityAttributes,
        uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(
        SafeFileHandle hDevice, uint dwIoControlCode, byte[]? lpInBuffer, int nInBufferSize,
        byte[]? lpOutBuffer, int nOutBufferSize, out int lpBytesReturned, IntPtr lpOverlapped);

    /// <summary>O que a leitura do NVMe devolveu (contadores, vida útil, aviso e o que faltou).</summary>
    private readonly record struct LeituraNvme(List<SmartContador> Contadores, int? VidaRestante, string AvisoCritico, string Observacao);

    /// <summary>
    /// Lê a página SMART/Health do NVMe direto do disco
    /// (IOCTL_STORAGE_QUERY_PROPERTY em \\.\PhysicalDriveN).
    ///
    /// <para>
    /// O caminho não é testável sem privilégio de administrador: o Windows
    /// nega o IOCTL de SMART para quem não é admin. Quando isso acontece, o
    /// disco aparece com o que deu e a observação contando o que faltou.
    /// </para>
    /// </summary>
    private static LeituraNvme LerNvme(int numero)
    {
        var contadores = new List<SmartContador>();
        string caminho = $@"\\.\PhysicalDrive{numero}";
        SafeFileHandle? disco = null;

        try
        {
            disco = CreateFile(caminho, GenericRead, ShareReadWrite, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);
            if (disco.IsInvalid)
            {
                int erro = Marshal.GetLastWin32Error();
                string motivo = erro == 5
                    ? "o Windows negou o acesso (a leitura do SMART precisa de execução como administrador)"
                    : "não deu para abrir o disco";
                return new LeituraNvme(contadores, null, "", $"{motivo} (erro {erro}).");
            }

            int tamanhoConsulta = Marshal.SizeOf<STORAGE_PROPERTY_QUERY>();

            var consulta = new STORAGE_PROPERTY_QUERY
            {
                PropertyId = StorageDeviceProtocolSpecificProperty,
                QueryType = PropertyStandardQuery,
            };
            var spec = new STORAGE_PROTOCOL_SPECIFIC_DATA
            {
                ProtocolType = ProtocolTypeNvme,
                DataType = NvmeDataTypeLogPage,
                ProtocolDataRequestValue = NvmeLogPageSmartHealth,
                // O subvalor é o namespace: 0 é o que o exemplo da Microsoft
                // usa para a página SMART/Health.
                ProtocolDataRequestSubValue = 0,
                // O offset é de onde a página começa dentro do bloco do
                // protocolo: logo depois do pedido (40 bytes).
                ProtocolDataOffset = TamanhoPedidoNvme,
                // A página SMART/Health tem 512 bytes.
                ProtocolDataLength = TamanhoPaginaNvme,
                FixedProtocolReturnData = 0,
                ProtocolDataRequestSubValue2 = 0,
                ProtocolDataRequestSubValue3 = 0,
                ProtocolDataRequestSubValue4 = 0,
            };

            // O buffer de entrada leva o cabeçalho, o pedido e espaço para a
            // página que o driver vai devolver.
            var entrada = new byte[OffsetPedidoNvme + TamanhoPedidoNvme + (int)TamanhoPaginaNvme];
            IntPtr buffer = Marshal.AllocHGlobal(entrada.Length);
            try
            {
                Marshal.StructureToPtr(consulta, buffer, false);
                Marshal.Copy(buffer, entrada, 0, tamanhoConsulta);
                Marshal.StructureToPtr(spec, buffer + OffsetPedidoNvme, false);
                Marshal.Copy(buffer + OffsetPedidoNvme, entrada, OffsetPedidoNvme, TamanhoPedidoNvme);
            }
            finally { Marshal.FreeHGlobal(buffer); }

            var saida = new byte[4096];
            if (!DeviceIoControl(disco, IoctlStorageQueryProperty, entrada, entrada.Length, saida, saida.Length, out int recebidos, IntPtr.Zero))
            {
                int erro = Marshal.GetLastWin32Error();
                string motivo = erro switch
                {
                    5 => "o Windows negou o acesso (sem privilégio de administrador)",
                    1 => "o driver do disco recusou este pedido (o caminho de protocolo do NVMe não é implementado por todos os drivers)",
                    87 => "o Windows achou o pedido malformado",
                    _ => "o Windows recusou a leitura",
                };
                return new LeituraNvme(contadores, null, "", $"o Windows recusou a leitura do log SMART/Health: {motivo} (erro {erro}).");
            }

            byte[]? pagina = AcharPaginaSmart(saida, recebidos);
            if (pagina == null)
                return new LeituraNvme(contadores, null, "", "o disco respondeu, mas a página SMART/Health não veio na resposta.");

            var lidos = DecodificarNvme(pagina, out int? vidaRestante);

            // O aviso crítico do próprio log: ele não pode ficar só como
            // contador, tem que chegar ao selo do disco e ao diagnóstico.
            string avisoCritico = pagina[0] != 0 ? TextoAvisoNvme(pagina[0]) : "";

            return new LeituraNvme(lidos, vidaRestante, avisoCritico, "");
        }
        catch (Exception ex)
        {
            return new LeituraNvme(contadores, null, "", "falha ao ler o NVMe: " + ex.Message);
        }
        finally { disco?.Dispose(); }
    }

    /// <summary>
    /// Acha a página SMART/Health (512 bytes) dentro da resposta do IOCTL.
    ///
    /// <para>
    /// A resposta começa pelo descritor de 48 bytes: a versão, o tamanho e o
    /// bloco do protocolo devolvido (que começa no offset 8). No bloco
    /// devolvido, o offset do payload está no 24 e o tamanho no 28; o
    /// payload em si fica em 8 + offset.
    /// </para>
    ///
    /// <para>
    /// Cada driver empacota de um jeito, então o app tenta as posições
    /// conhecidas e aceita a primeira que parece mesmo uma página SMART (a
    /// prova exige cinco campos coerentes ao mesmo tempo).
    /// </para>
    /// </summary>
    private static byte[]? AcharPaginaSmart(byte[] saida, int recebidos)
    {
        const int tamanhoDescritor = 48;
        const int inicioBloco = 8;

        if (recebidos < tamanhoDescritor || saida.Length < tamanhoDescritor) return null;

        var descritor = new STORAGE_PROTOCOL_DATA_DESCRIPTOR();
        IntPtr buffer = Marshal.AllocHGlobal(tamanhoDescritor);
        try
        {
            Marshal.Copy(saida, 0, buffer, tamanhoDescritor);
            descritor = (STORAGE_PROTOCOL_DATA_DESCRIPTOR)Marshal.PtrToStructure(buffer, typeof(STORAGE_PROTOCOL_DATA_DESCRIPTOR))!;
        }
        finally { Marshal.FreeHGlobal(buffer); }

        var candidatos = new List<int>();

        // 1) O bloco devolvido diz onde está o payload (8 + offset).
        if (descritor.ProtocolDataOffset > 0)
        {
            long posicao = inicioBloco + descritor.ProtocolDataOffset;
            if (posicao > 0 && posicao < saida.Length) candidatos.Add((int)posicao);
        }

        // 2) Logo depois do bloco do protocolo (que tem 40 bytes).
        candidatos.Add(inicioBloco + TamanhoPedidoNvme);

        // 3) Logo depois do descritor, sem o bloco.
        candidatos.Add(tamanhoDescritor);

        foreach (int offset in candidatos)
        {
            if (offset < 0 || offset + 512 > saida.Length) continue;
            if (!ParecePaginaSmart(saida, offset)) continue;

            var pagina = new byte[512];
            Array.Copy(saida, offset, pagina, 0, 512);
            return pagina;
        }

        return null;
    }

    /// <summary>
    /// Uma página SMART/Health tem o aviso crítico nos 6 bits primeiros, a
    /// reserva e o percentual de uso em porcentagem, e a temperatura num
    /// valor plausível. São cinco campos independentes: é bem difícil um
    /// pedaço qualquer da resposta passar por essa prova.
    /// </summary>
    private static bool ParecePaginaSmart(byte[] dados, int offset)
    {
        if (offset + 512 > dados.Length) return false;

        // O aviso crítico só usa 6 bits (os 2 do alto são zero).
        if ((dados[offset] & 0xC0) != 0) return false;

        // Reserva disponível, reserva mínima e percentual de uso: 0 a 100 %.
        if (dados[offset + 3] > 100) return false;
        if (dados[offset + 4] > 100) return false;
        if (dados[offset + 5] > 100) return false;

        // A temperatura: Kelvin de verdade (200 a 400 K).
        int temperatura = dados[offset + 1] | (dados[offset + 2] << 8);
        if (temperatura < 200 || temperatura > 400) return false;

        return true;
    }

    /// <summary>
    /// Decodifica a página SMART/Health do NVMe (512 bytes) nos contadores da
    /// tela.
    ///
    /// <para>
    /// Só são lidos os campos que a especificação do NVMe coloca no mesmo
    /// lugar em todas as revisões (os 6 primeiros bytes e as 8 temperaturas):
    /// o aviso crítico, a temperatura composta, a reserva, a reserva mínima,
    /// o percentual de uso e as 8 temperaturas.
    /// </para>
    ///
    /// <para>
    /// Os contadores grandes (capacidade, unidades lidas e gravadas, horas
    /// ligado) ficam depois, em blocos de 128 bits cujo tamanho muda entre
    /// as revisões da spec — e o número errado é pior do que o número
    /// ausente. Eles não são decodificados aqui.
    /// </para>
    /// </summary>
    internal static List<SmartContador> DecodificarNvme(byte[] pagina, out int? vidaRestante)
    {
        var contadores = new List<SmartContador>();
        vidaRestante = null;

        contadores.Add(new SmartContador { Nome = "Aviso do disco", Valor = TextoAvisoNvme(pagina[0]) });
        contadores.Add(new SmartContador { Nome = "Temperatura", Valor = TextoTemperaturaNvme(pagina[1] | (pagina[2] << 8)) });
        contadores.Add(new SmartContador { Nome = "Reserva disponível", Valor = pagina[3].ToString(CultureInfo.CurrentCulture) + " %" });

        // Byte 5: o percentual de uso (quanto o disco já foi usado). A vida
        // que resta é o complemento — é assim que a spec define.
        int usado = pagina[5];
        int vida = 100 - usado;
        if (vida < 0) vida = 0;
        vidaRestante = vida;
        contadores.Add(new SmartContador { Nome = "Vida útil restante", Valor = vida + " % (uso " + usado + " %)" });

        // Bytes 17 a 32: as 8 temperaturas (2 bytes cada, em Kelvin).
        // Só entram na tela as que são plausíveis: se o layout do log for
        // outro, a área vem zerada ou com lixo e nada aparece — é melhor
        // não mostrar do que mostrar número errado.
        var sensores = new List<SmartContador>();
        for (int sensor = 0; sensor < 8; sensor++)
        {
            int offset = 17 + sensor * 2;
            if (offset + 2 > pagina.Length) break;
            int temperatura = pagina[offset] | (pagina[offset + 1] << 8);
            if (temperatura < 200 || temperatura > 400) continue;
            sensores.Add(new SmartContador
            {
                Nome = $"Temperatura do sensor {sensor + 1}",
                Valor = TextoTemperaturaNvme(temperatura),
            });
        }
        contadores.AddRange(sensores);

        return contadores;
    }

    /// <summary>
    /// A temperatura do NVMe.
    ///
    /// <para>
    /// A spec do NVMe guarda a temperatura em Kelvin: 313 K são 40 °C. O
    /// valor bruto vai à mostra junto, porque um disco fora da faixa
    /// (200 a 400 K) é sinal de que o controller fala outra coisa — melhor
    /// mostrar o número cru do que converter por conta própria.
    /// </para>
    /// </summary>
    private static string TextoTemperaturaNvme(int bruto)
    {
        if (bruto <= 0) return "—";
        string celsius = bruto >= 200 && bruto <= 400
            ? (bruto - 273).ToString("0.#", CultureInfo.CurrentCulture) + " °C"
            : "—";
        return celsius + " (bruto " + bruto.ToString(CultureInfo.CurrentCulture) + ")";
    }

    /// <summary>
    /// Os 5 bits de aviso do log SMART/Health (os 3 do alto são reservados
    /// pela spec e são ignorados).
    /// </summary>
    private static string TextoAvisoNvme(int aviso)
    {
        if (aviso == 0) return "Nenhum";
        var partes = new List<string>();
        if ((aviso & 0x01) != 0) partes.Add("reserva disponível abaixo do limiar");
        if ((aviso & 0x02) != 0) partes.Add("temperatura acima do limiar");
        if ((aviso & 0x04) != 0) partes.Add("confiabilidade degradada");
        if ((aviso & 0x08) != 0) partes.Add("mídia somente leitura");
        if ((aviso & 0x10) != 0) partes.Add("falha no backup da memória volátil");
        return partes.Count == 0 ? "Nenhum" : string.Join(", ", partes);
    }

    // =================================================================== decodificação

    /// <summary>
    /// Decodifica os 512 bytes do SMART Read Data na tabela de atributos,
    /// casando o limiar de cada atributo pelo id (do SMART Read Thresholds).
    ///
    /// <para>
    /// A função é pura e tolerante: registro curto ou truncado devolve o que
    /// deu para ler, sem estourar exceção.
    /// </para>
    /// </summary>
    /// <param name="dados">Os bytes do SMART Read Data (512 bytes; menos que isso também serve).</param>
    /// <param name="limiares">Os bytes do SMART Read Thresholds (null = sem limiar nenhum).</param>
    public static List<SmartAtributo> Decodificar(byte[] dados, byte[]? limiares)
    {
        var lista = new List<SmartAtributo>();
        if (dados == null || dados.Length <= OffsetPrimeiro) return lista;

        var mapaLimiar = LerLimiares(limiares);

        for (int i = 0; i < MaximoAtributos; i++)
        {
            int offset = OffsetPrimeiro + i * TamanhoAtributo;

            // Registro truncado: devolve o que deu, sem quebrar.
            if (offset + TamanhoAtributo > dados.Length) break;

            int id = dados[offset];
            if (id == 0x00) break; // fim da tabela

            int flags = dados[offset + 1] | (dados[offset + 2] << 8);
            var cru = new byte[6];
            Array.Copy(dados, offset + 5, cru, 0, 6);

            lista.Add(new SmartAtributo
            {
                Id = id,
                Nome = SmartNomes.De(id),
                Flags = flags,
                Valor = dados[offset + 3],
                Pior = dados[offset + 4],
                Limiar = mapaLimiar.TryGetValue(id, out int limiar) ? limiar : null,
                Cru = cru,
                PreFail = (flags & MascaraPreFail) != 0,
                CruTexto = TextoCru(cru),
            });
        }

        return lista;
    }

    /// <summary>
    /// O limiar de cada atributo, pelo id (limiar 0 = "não se aplica", então
    /// o atributo fica sem limiar).
    /// </summary>
    private static Dictionary<int, int> LerLimiares(byte[]? limiares)
    {
        var mapa = new Dictionary<int, int>();
        if (limiares == null) return mapa;

        for (int i = 0; i < MaximoAtributos; i++)
        {
            int offset = OffsetPrimeiro + i * TamanhoAtributo;
            if (offset + 2 > limiares.Length) break;

            int id = limiares[offset];
            if (id == 0x00) break;

            int limiar = limiares[offset + 1];
            if (limiar > 0) mapa[id] = limiar;
        }

        return mapa;
    }

    /// <summary>
    /// O valor cru na tela: os 6 bytes em hexadecimal e o número que eles
    /// representam (little-endian, como a ATA guarda).
    ///
    /// <para>
    /// Alguns atributos empacotam mais de uma informação nos 6 bytes (a
    /// temperatura, por exemplo, traz o valor atual e o pior já registrado).
    /// Nesses, o número gigante não significa nada: o hexadecimal é que
    /// importa, e é o que o CrystalDiskInfo mostra.
    /// </para>
    /// </summary>
    internal static string TextoCru(byte[] cru)
    {
        if (cru.Length == 0) return "";

        var hex = "0x" + string.Concat(cru.Select(b => b.ToString("X2", CultureInfo.InvariantCulture)));

        // Tudo 0xFF é o "não informado" dos drivers: não vira número na tela.
        if (cru.All(b => b == 0xFF)) return hex + " (sem valor)";

        ulong numero = 0;
        for (int i = cru.Length - 1; i >= 0; i--) numero = (numero << 8) | cru[i];

        // Acima de um milhão o valor não é um contador: é um campo
        // empacotado. Mostrar o número seria mostrar um número sem
        // significado.
        if (numero > 1_000_000) return hex;

        return hex + " (" + numero.ToString(CultureInfo.InvariantCulture) + ")";
    }
}
