namespace ResetExplode;

/// <summary>
/// Regras do diagnóstico de saúde.
///
/// São funções puras: recebem a fotografia do sistema e devolvem achados.
/// Não tocam em WMI, registro nem log de eventos — por isso dá para testar
/// cada regra sem Windows em cima.
///
/// Os limiares espelham os do script de linha de comando
/// (diagnostico-resetexplode.ps1), para os dois relatórios contarem a mesma história.
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
        foreach (var app in c.AppsQueFalharam.OrderByDescending(a => a.Value).Take(3))
        {
            if (app.Value < c.Travamentos.Count * 0.8) continue;
            saida.Add(new Achado
            {
                Severidade = Severidade.Informativo,
                Area = "Travamentos",
                Mensagem = $"'{app.Key}' aparece em {app.Value} de {c.Travamentos.Count} travamentos",
                Acao = "Provavelmente reexecuta no boot: verifique se crasha mesmo após reinício limpo"
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
                Acao = "Atualize o driver da GPU"
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
                Acao = "Teste com ASPM Off: powercfg /setacvalueindex SCHEME_CURRENT SUB_PCIEXPRESS ASPM 0"
            });
        }

        if (c.NvmeEmAdaptador)
        {
            saida.Add(new Achado
            {
                Severidade = Severidade.Alto,
                Area = "PCIe/ASPM",
                Mensagem = "NVMe conectado via adaptador M.2->PCIe (caminho do chipset)",
                Acao = "Slots x1 do chipset + ASPM causam NVMe não aparecer em reinício a quente"
            });
        }
    }

    // -------------------------------------------------------------------- discos

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
                Acao = "Feche abas de navegador e apps Electron; reduza o autostart"
            });
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
                Acao = "Desative o que não usa no boot (RESET FERRAMENTAS ou Gerenciador de Tarefas)"
            });
        }

        var servicosTerceiros = c.ServicosAuto.Count(s => !s.EhDoSistema);
        if (servicosTerceiros >= 10)
        {
            saida.Add(new Achado
            {
                Severidade = Severidade.Baixo,
                Area = "Lentidão",
                Mensagem = $"{servicosTerceiros} serviços automáticos fora do system32",
                Acao = "Revise os que você não usa (atualizadores, apps de terceiros)"
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
