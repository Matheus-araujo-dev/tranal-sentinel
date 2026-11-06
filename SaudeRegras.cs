namespace TranalSentinel;

/// <summary>
/// Regras do diagnóstico de saúde.
///
/// São funções puras: recebem a fotografia do sistema e devolvem achados.
/// Não tocam em WMI, registro nem log de eventos — por isso dá para testar
/// cada regra sem Windows em cima.
///
/// Os limiares espelham os do script de linha de comando
/// (e a CLI --saude, que reaproveita as mesmas regras), para os dois relatórios
/// contarem a mesma história.
/// </summary>
public static class SaudeRegras
{
    /// <summary>Ordem de exibição: do mais grave para o menos grave.</summary>
    public static int Ordem(Severidade s) => s switch
    {
        Severidade.Critico => 0,
        Severidade.Alto => 1,
        Severidade.Medio => 2,
        Severidade.Baixo => 3,
        _ => 4
    };

    /// <summary>Nome legível do índice de ASPM (0 = desligado).</summary>
    public static string NomeAspm(int indice) => indice switch
    {
        0 => "Desligado",
        1 => "Economia moderada",
        _ => "Economia máxima"
    };

    /// <summary>Avalia tudo e devolve os achados ordenados por gravidade.</summary>
    public static List<Achado> Avaliar(ColetaSaude c)
    {
        var achados = new List<Achado>();
        Travamentos(c, achados);
        Hardware(c, achados);
        Pcie(c, achados);
        Discos(c, achados);
        DiscosSmart(c, achados);
        Antivirus(c, achados);
        Memoria(c, achados);
        Lentidao(c, achados);
        achados.Sort((a, b) => Ordem(a.Severidade).CompareTo(Ordem(b.Severidade)));
        return achados;
    }

    // ---------------------------------------------------------------- travamentos

    private static void Travamentos(ColetaSaude c, List<Achado> saida)
    {
        if (c.Travamentos.Count == 0) return;

        var comBugcheck = c.Travamentos.Count(t => t.Bugcheck != "0");
        var semBotao = c.Travamentos.Count(t => t.Detalhe.StartsWith("Sem botão", StringComparison.OrdinalIgnoreCase));

        // Congelamento total: o Windows nunca conseguiu escrever o bugcheck.
        if (comBugcheck == 0 && c.Minidumps == 0)
        {
            saida.Add(new Achado
            {
                Severidade = Severidade.Critico,
                Area = "Travamentos",
                Mensagem = $"{c.Travamentos.Count} travamentos sem bugcheck e sem minidump (congelamento total)",
                Acao = "Causa provável: hardware ou firmware (drive PCIe, RAM/BIOS, PSU). Não é app comum"
            });
        }
        else
        {
            saida.Add(new Achado
            {
                Severidade = Severidade.Alto,
                Area = "Travamentos",
                Mensagem = $"{c.Travamentos.Count} travamentos ({comBugcheck} com bugcheck, {semBotao} sem botão de energia)",
                Acao = "Leia o bugcheck no Event Viewer e o minidump em C:\\Windows\\Minidump"
            });
        }

        // App que falha junto de quase todo travamento: costuma reexecutar no boot,
        // então atrasa o sistema — mas raramente é a causa do congelamento.
        foreach (var app in c.AppsQueFalharam.OrderByDescending(a => a.Vezes).Take(3))
        {
            if (app.Vezes < c.Travamentos.Count * 0.8) continue;
            saida.Add(new Achado
            {
                Severidade = Severidade.Informativo,
                Area = "Travamentos",
                Mensagem = $"'{app.Nome}' aparece em {app.Vezes} de {c.Travamentos.Count} travamentos",
                Acao = app.AindaInstalado == false
                    ? "Não encontrei mais esse programa instalado: esses travamentos são antigos e não voltam por causa dele"
                    : "Provavelmente reexecuta no boot: verifique se crasha mesmo após reinício limpo"
            });
        }
    }

    // ------------------------------------------------------------------ hardware

    private static void Hardware(ColetaSaude c, List<Achado> saida)
    {
        if (c.EventosWhea > 0)
        {
            saida.Add(new Achado
            {
                Severidade = Severidade.Alto,
                Area = "Hardware",
                Mensagem = $"{c.EventosWhea} eventos WHEA (erro de CPU/RAM/PCIe)",
                Acao = "Teste RAM em 3200, atualize BIOS, verifique a PSU"
            });
        }

        if (c.Minidumps > 0)
        {
            saida.Add(new Achado
            {
                Severidade = Severidade.Alto,
                Area = "Hardware",
                Mensagem = $"{c.Minidumps} minidump(s) de tela azul em C:\\Windows\\Minidump",
                Acao = "Abra o .dmp com o WinDbg para ver a causa exata"
            });
        }

        if (c.ResetsDriverVideo > 0)
        {
            saida.Add(new Achado
            {
                Severidade = Severidade.Medio,
                Area = "Hardware",
                Mensagem = $"{c.ResetsDriverVideo} resets de driver de vídeo (4101)",
                Acao = "Atualize o driver da GPU",
                Acoes = { AcoesSaude.AbrirDrivers() }
            });
        }
    }

    // ---------------------------------------------------------------------- PCIe

    private static void Pcie(ColetaSaude c, List<Achado> saida)
    {
        if (c.AspmAc == null) return;

        if (c.AspmAc.Value != 0)
        {
            saida.Add(new Achado
            {
                Severidade = Severidade.Alto,
                Area = "PCIe/ASPM",
                Mensagem = $"Gerenciamento de energia do link PCIe (ASPM) em '{NomeAspm(c.AspmAc.Value)}'",
                Acao = "Desligue o ASPM do plano de energia ativo (o botão abaixo faz isso e guarda como desfazer)",
                Acoes = { AcoesSaude.DesligarAspm() }
            });
        }

        foreach (var nvme in c.Nvmes)
        {
            if (!NvmeEhAdaptador(nvme.PaiDoController, nvme.Localizacao)) continue;
            saida.Add(new Achado
            {
                Severidade = Severidade.Alto,
                Area = "PCIe/ASPM",
                Mensagem = $"NVMe '{nvme.Nome}' conectado via placa adaptadora M.2->PCIe (caminho pelo chipset)",
                Acao = "Encaixe o M.2 direto no soquete da placa-mãe (M.2_1 vem do CPU) ou desative ASPM/L1 Substates no BIOS",
                Acoes = { AcoesSaude.DesligarAspm() }
            });
        }
    }

    /// <summary>
    /// Diz se um NVMe está numa placa adaptadora M.2-&gt;PCIe ou direto num soquete da placa-mãe.
    ///
    /// O disco pendurado num NVMe é sempre o próprio controller (e o controller
    /// nunca é do fabricante da placa, então olhar o pai do disco não diz nada).
    /// Quem diz é o que vem DEPOIS do controller:
    ///
    ///   • controller direto num soquete/porta da placa → a ponte onde ele está
    ///     é da própria placa (VEN_1022 na AMD) e o caminho de localização tem
    ///     3 degraus (PCIROOT/PCI/PCI);
    ///   • controller numa placa adaptadora → a ponte é da própria placa
    ///     adaptadora (outro fabricante) e o caminho tem 4 degraus ou mais.
    ///
    /// O caminho de localização manda quando existe; a ponte é o plano B.
    /// </summary>
    public static bool NvmeEhAdaptador(string? paiDoController, string? localizacao)
    {
        int degraus = GrausPcie(localizacao);
        if (degraus >= 4) return true;
        if (degraus == 3) return false;

        if (!string.IsNullOrWhiteSpace(paiDoController))
            return !paiDoController.Contains("VEN_1022", StringComparison.OrdinalIgnoreCase);

        return false;
    }

    /// <summary>
    /// Quantos degraus PCIe o caminho de localização tem (PCIROOT conta como um).
    /// "PCIROOT(0)#PCI(0101)#PCI(0000)" = 3 (dispositivo direto na placa).
    /// </summary>
    private static int GrausPcie(string? localizacao)
    {
        if (string.IsNullOrWhiteSpace(localizacao)) return 0;

        // Só o caminho que começa em PCIROOT descreve a topologia PCIe;
        // o caminho ACPI paralelo não serve para contar degraus.
        var caminho = localizacao
            .Split(';', '|')
            .FirstOrDefault(p => p.Contains("PCIROOT(", StringComparison.OrdinalIgnoreCase));
        if (caminho == null) return 0;

        int degraus = 0;
        foreach (var parte in caminho.Split('#'))
        {
            string t = parte.Trim();
            if (t.StartsWith("PCIROOT(", StringComparison.OrdinalIgnoreCase) ||
                t.StartsWith("PCI(", StringComparison.OrdinalIgnoreCase))
                degraus++;
        }
        return degraus;
    }
    // -------------------------------------------------------------------- discos

    /// <summary>
    /// O que o S.M.A.R.T. diz de cada disco.
    ///
    /// <para>
    /// A regra é pura: recebe a fotografia (com a lista de discos lida do
    /// SMART) e devolve os achados. Disco sem dado de SMART não vira achado
    /// nenhum — a nota de quem não tem SMART não muda por causa disso.
    /// </para>
    /// </summary>
    public static List<Achado> AvaliarDiscosSmart(ColetaSaude c)
    {
        var achados = new List<Achado>();

        foreach (var disco in c.DiscosSmart)
        {
            string nome = disco.Modelo.Length > 0 ? disco.Modelo : $"Disco {disco.Numero}";

            if (disco.FalhaPrevista)
            {
                achados.Add(new Achado
                {
                    Severidade = Severidade.Critico,
                    Area = "Disco",
                    Mensagem = $"{nome}: o próprio disco avisa (S.M.A.R.T.) que deve falhar em breve",
                    Acao = "Faça backup agora e troque o disco"
                });
            }

            // O aviso crítico do log SMART/Health do NVMe: o próprio disco
            // avisa que está em risco (reserva baixa, temperatura alta,
            // confiabilidade degradada). Sem isto, um NVMe em aviso crítico
            // apareceria com o selo "OK".
            if (!string.IsNullOrWhiteSpace(disco.AvisoCritico))
            {
                achados.Add(new Achado
                {
                    Severidade = Severidade.Critico,
                    Area = "Disco",
                    Mensagem = $"{nome}: o disco avisa {disco.AvisoCritico}",
                    Acao = "Faça backup agora e prepare a troca do disco"
                });
            }

            long realocados = disco.ValorCru(5) ?? 0;   // Reallocated_Sector_Ct
            if (realocados > 0)
            {
                achados.Add(new Achado
                {
                    Severidade = Severidade.Alto,
                    Area = "Disco",
                    Mensagem = $"{nome}: {realocados} setor(es) realocado(s) pelo disco",
                    Acao = "O disco já perdeu setores: faça backup e prepare a troca"
                });
            }

            long pendentes = disco.ValorCru(197) ?? 0;   // Current_Pending_Sector
            if (pendentes > 0)
            {
                achados.Add(new Achado
                {
                    Severidade = Severidade.Medio,
                    Area = "Disco",
                    Mensagem = $"{nome}: {pendentes} setor(es) pendente(s) (para realocar)",
                    Acao = "Vigie o disco: pendência que vira realocação é aviso de que ele está cedendo"
                });
            }

            // Desgaste alto: NVMe (vida útil que resta) ou ATA (atributo 232).
            int? vida = disco.VidaRestantePercent;
            if (!vida.HasValue)
            {
                // O atributo 232 (Remaining_Lifetime_Perc) é a vida útil que
                // resta, em % — quando o disco tem esse atributo.
                long? cru = disco.ValorCru(232);
                if (cru is long valor && valor > 0) vida = (int)Math.Min(valor, 100);
            }

            if (vida is int restante && restante <= 10)
            {
                achados.Add(new Achado
                {
                    Severidade = Severidade.Medio,
                    Area = "Disco",
                    Mensagem = $"{nome}: {restante}% de vida útil restante",
                    Acao = "O SSD está no fim da vida útil: compre o outro antes"
                });
            }
        }

        return achados;
    }

    private static void DiscosSmart(ColetaSaude c, List<Achado> saida) => saida.AddRange(AvaliarDiscosSmart(c));

    private static void Discos(ColetaSaude c, List<Achado> saida)
    {
        foreach (var d in c.Discos)
        {
            if (!d.EhNvme || !d.EstaComProblema) continue;
            saida.Add(new Achado
            {
                Severidade = Severidade.Critico,
                Area = "Disco",
                Mensagem = $"{d.Modelo} não está pronto ({d.Status})",
                Acao = "O Windows não está vendo o drive: reinicie a frio (desligue e ligue) e confira o SMART"
            });
        }

        if (c.Orfaos.Count > 0)
        {
            saida.Add(new Achado
            {
                Severidade = Severidade.Medio,
                Area = "Disco",
                Mensagem = $"{c.Orfaos.Count} dispositivo(s) de armazenamento órfão(s): {string.Join(", ", c.Orfaos.Select(o => o.Nome))}",
                Acao = "Limpe no Gerenciador de Dispositivos (mostrar ocultos) e desacople do Storage Spaces"
            });
        }
    }

    // ---------------------------------------------------------------- segurança

    private static void Antivirus(ColetaSaude c, List<Achado> saida)
    {
        if (c.DefinicoesAntivirusDias != null && c.DefinicoesAntivirusDias > 7)
        {
            saida.Add(new Achado
            {
                Severidade = Severidade.Alto,
                Area = "Segurança",
                Mensagem = $"Definições do antivírus com {c.DefinicoesAntivirusDias} dias (atualização automática possivelmente quebrada)",
                Acao = "Reinstale o pacote completo ou force a atualização de definições"
            });
        }

        // O Defender desligado só é problema se nenhum antivírus de terceiros assumiu.
        var filtroAtivo = c.EstadoDriverAntivirus != null &&
                          c.EstadoDriverAntivirus.Equals("Running", StringComparison.OrdinalIgnoreCase);
        var terceiro = c.AntivirusRegistrado;

        if (c.ModoDefender == "Not running")
        {
            if (filtroAtivo && !string.IsNullOrEmpty(terceiro))
            {
                saida.Add(new Achado
                {
                    Severidade = Severidade.Informativo,
                    Area = "Segurança",
                    Mensagem = $"Defender delegando para {terceiro} (filtro de kernel rodando)",
                    Acao = "Situação normal com antivírus de terceiros"
                });
            }
            else
            {
                saida.Add(new Achado
                {
                    Severidade = Severidade.Critico,
                    Area = "Segurança",
                    Mensagem = "Defender parado E filtro de kernel do antivírus ausente: sem proteção de arquivo",
                    Acao = "Instale o pacote completo do antivírus ou reative o Defender"
                });
            }
        }

        if (c.AntivirusMemoriaMb > 300)
        {
            saida.Add(new Achado
            {
                Severidade = Severidade.Medio,
                Area = "Lentidão",
                Mensagem = $"Antivírus consumindo {c.AntivirusMemoriaMb} MB de RAM",
                Acao = "Teste o impacto: pause a proteção em tempo real e compare"
            });
        }
    }

    // ------------------------------------------------------------------- memória

    private static void Memoria(ColetaSaude c, List<Achado> saida)
    {
        if (c.MemoriaCompressaoMb > 1500)
        {
            saida.Add(new Achado
            {
                Severidade = Severidade.Alto,
                Area = "Lentidão",
                Mensagem = $"Compressão de memória ativa: {c.MemoriaCompressaoMb} MB (sintoma de pressão de RAM)",
                Acao = "Feche abas de navegador e apps Electron; reduza o autostart",
                Acoes = { AcoesSaude.AbrirOtimizacao(), AcoesSaude.AbrirInicializacao() }
            });
        }

        // Com o SPD em mãos, a memória é comparada com o que o próprio módulo
        // oferece. A regra do "acima de 3200" do Ryzen 5000 só vale como
        // reserva, para quando não sabemos o que o módulo tem gravado.
        //
        // Quem manda é o módulo mais fraco: com módulos diferentes é ele que
        // limita o clock do sistema (a escolha mora em MemoriaRegras).
        SpdModulo? modulo = MemoriaRegras.ModuloQueManda(c.ModulosSpd.Select(m => m.Modulo));
        if (modulo != null)
        {
            int configurada = c.ModulosMemoria.Where(m => m.VelocidadeConfigMHz > 0)
                                               .Select(m => m.VelocidadeConfigMHz)
                                               .DefaultIfEmpty(0)
                                               .Max();

            if (configurada > 0)
            {
                // Os tempos ativos entram na regra: é deles que se descobre se
                // a BIOS apertou os tempos à mão (a causa mais comum de
                // travamento por memória) ou se só subiu o clock.
                Achado? achado = MemoriaRegras.Avaliar(configurada, modulo, c.TimingsAtivosMemoria);
                if (achado != null) saida.Add(achado);
            }

            return;
        }

        foreach (var m in c.ModulosMemoria)
        {
            if (m.VelocidadeConfigMHz <= 3200) continue;
            saida.Add(new Achado
            {
                Severidade = Severidade.Medio,
                Area = "Estabilidade",
                Mensagem = $"RAM em {m.VelocidadeConfigMHz} MHz (slot {m.Slot}) - acima do spec oficial de 3200 do Ryzen 5000",
                Acao = "Teste em 3200 para isolar instabilidade"
            });
        }
    }

    // ------------------------------------------------------------------ lentidão

    private static void Lentidao(ColetaSaude c, List<Achado> saida)
    {
        if (c.AutostartAtivos >= 12)
        {
            saida.Add(new Achado
            {
                Severidade = Severidade.Medio,
                Area = "Lentidão",
                Mensagem = $"{c.AutostartAtivos} programas na inicialização automática",
                Acao = "Desative o que não usa no boot (TRANAL FERRAMENTAS ou Gerenciador de Tarefas)",
                Acoes = { AcoesSaude.AbrirInicializacao() }
            });
        }

        var servicosTerceiros = c.ServicosAuto.Count(s => !s.EhDoSistema);
        if (servicosTerceiros >= 10)
        {
            var suspendiveis = ServicosSuspendiveis.Listar(c.ServicosAuto).Count;
            saida.Add(new Achado
            {
                Severidade = Severidade.Baixo,
                Area = "Lentidão",
                Mensagem = $"{servicosTerceiros} serviços automáticos fora do system32",
                Acao = "Revise os que você não usa (atualizadores, apps de terceiros)",
                Acoes = { AcoesSaude.SuspenderServicos(suspendiveis) }
            });
        }

        if (c.HypervisorAtivo)
        {
            saida.Add(new Achado
            {
                Severidade = Severidade.Informativo,
                Area = "Lentidão",
                Mensagem = "Hypervisor presente (Hyper-V/VBS ativo): consome RAM e adiciona latência",
                Acao = "Se não usa WSL, Docker ou VMs, avalie desativar Hyper-V e VBS"
            });
        }
    }
}
