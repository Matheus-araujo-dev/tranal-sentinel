namespace ResetExplode;

/// <summary>
/// RESET SAÚDE: diagnóstico de travamentos e lentidão.
///
/// Complementa o RESET EXPLODE, que mede o hardware em tempo real (sensores,
/// SMART, benchmarks) mas não responde "por que meu PC trava?". Aqui a análise
/// é feita sobre o histórico do Windows: travamentos totais (Kernel-Power 41),
/// erros de hardware (WHEA), o estado real do antivírus, o gerenciamento de
/// energia do link PCIe (ASPM) e o bloat de inicialização.
///
/// Tudo é somente leitura: nada é alterado, instalado ou reiniciado.
///
/// O fluxo é sempre o mesmo:
///   SaudeColeta  -&gt; fotografa o sistema (leitura crua)
///   SaudeRegras  -&gt; transforma a foto em achados (lógica pura, testável)
///   SaudeDiagnostico -&gt; junta as duas coisas e entrega o relatório
/// </summary>
public static class SaudeDiagnostico
{
    /// <summary>Executa a análise completa. Nunca lança: cada bloco de coleta é isolado.</summary>
    public static RelatorioSaude Executar()
    {
        var dados = SaudeColeta.Coletar();

        var relatorio = new RelatorioSaude
        {
            GeradoEm = DateTime.Now,
            Dados = dados
        };

        relatorio.Travamentos.AddRange(dados.Travamentos);

        relatorio.AppsNoBoot.AddRange(dados.AppsQueFalharam);

        relatorio.Achados.AddRange(SaudeRegras.Avaliar(dados));
        return relatorio;
    }
}
