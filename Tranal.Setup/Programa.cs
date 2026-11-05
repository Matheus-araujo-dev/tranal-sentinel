using System.Windows;
using TranalSetup.Dominio;
using TranalSetup.Infra;
using TranalSetup.Servicos;

namespace TranalSetup;

/// <summary>
/// Ponto de entrada do instalador do TRANAL SENTINEL.
///
/// Jeitos de chamar:
///   TranalSetup.exe                           → abre a janela de instalação
///   TranalSetup.exe --instalar "C:\payload"   → instala em modo silencioso
///   TranalSetup.exe --desinstalar             → desinstala em modo silencioso
///   TranalSetup.exe --simular                 → mostra o plano e não altera nada
///
/// O --simular também pode vir junto de --instalar ou --desinstalar, e nesse
/// caso mostra o plano só da operação pedida.
/// </summary>
public static class Programa
{
    /// <summary>Saída: a operação terminou bem.</summary>
    public const int SaidaOk = 0;

    /// <summary>Saída: a linha de comando não faz sentido.</summary>
    public const int SaidaUsoInvalido = 1;

    /// <summary>Saída: a operação falhou (e o que já tinha sido feito foi desfeito).</summary>
    public const int SaidaFalha = 2;

    /// <summary>Executa o instalador conforme os argumentos recebidos.</summary>
    [STAThread]
    public static int Main(string[] args)
    {
        ArgumentosLinhaComando argumentos = ArgumentosLinhaComando.Analisar(args);

        if (!argumentos.Valido)
        {
            ConsoleAnexo.Anexar();
            ConsoleAnexo.EscreverErro("TRANAL SENTINEL - INSTALADOR");
            ConsoleAnexo.EscreverErro(argumentos.Erro ?? "");
            ConsoleAnexo.EscreverErro("");
            ConsoleAnexo.EscreverErro(Uso());
            return SaidaUsoInvalido;
        }

        if (argumentos.Simular) return ExecutarSimulacao(argumentos);

        return argumentos.Modo switch
        {
            ModoExecucao.Instalar => ExecutarInstalacao(argumentos.PastaPayload ?? ""),
            ModoExecucao.Desinstalar => ExecutarDesinstalacao(),
            _ => ExecutarInterface(),
        };
    }

    /// <summary>
    /// Mostra o plano e não altera nada: nem arquivos, nem registro, nem log.
    /// Serve para conferir o que será feito antes de instalar de verdade.
    /// </summary>
    private static int ExecutarSimulacao(ArgumentosLinhaComando argumentos)
    {
        ConsoleAnexo.Anexar();

        var contexto = ContextoSistema.DoSistema();
        var simulador = new ServicoSimulacao(contexto, new RegistroWindows(), new TarefaAgendadaWindows());
        var opcoes = new OpcoesInstalacao();
        string payload = argumentos.PastaPayload ?? contexto.PastaInstalador;

        string texto = argumentos.Modo switch
        {
            ModoExecucao.Instalar => ServicoSimulacao.Cabecalho("INSTALAR") + Environment.NewLine
                + simulador.MontarPlanoInstalacao(opcoes, payload),
            ModoExecucao.Desinstalar => ServicoSimulacao.Cabecalho("DESINSTALAR") + Environment.NewLine
                + simulador.MontarPlanoDesinstalacao(),
            _ => simulador.MontarPlanoCompleto(opcoes, payload),
        };

        ConsoleAnexo.Escrever(texto);
        ConsoleAnexo.Escrever("FIM DA SIMULAÇÃO - nada foi alterado no sistema.");
        return SaidaOk;
    }

    /// <summary>Instala em modo silencioso (sem interface).</summary>
    private static int ExecutarInstalacao(string pastaPayload)
    {
        ConsoleAnexo.Anexar();

        var contexto = ContextoSistema.DoSistema();
        var log = new LogInstalador(CaminhosInstalacao.ArquivoLog(contexto));
        var servico = new ServicoInstalacao(contexto, new RegistroWindows(), new AtalhoWindows(), new TarefaAgendadaWindows(), log);

        ResultadoOperacao resultado = servico.Instalar(new OpcoesInstalacao(), pastaPayload);

        foreach (string detalhe in resultado.Detalhes) ConsoleAnexo.Escrever($"  {detalhe}");
        ConsoleAnexo.Escrever(resultado.Mensagem);
        return resultado.Sucesso ? SaidaOk : SaidaFalha;
    }

    /// <summary>Desinstala em modo silencioso (sem interface).</summary>
    private static int ExecutarDesinstalacao()
    {
        ConsoleAnexo.Anexar();

        var contexto = ContextoSistema.DoSistema();
        var log = new LogInstalador(CaminhosInstalacao.ArquivoLog(contexto));
        var servico = new ServicoInstalacao(contexto, new RegistroWindows(), new AtalhoWindows(), new TarefaAgendadaWindows(), log);

        ResultadoOperacao resultado = servico.Desinstalar();

        foreach (string detalhe in resultado.Detalhes) ConsoleAnexo.Escrever($"  {detalhe}");
        ConsoleAnexo.Escrever(resultado.Mensagem);
        return resultado.Sucesso ? SaidaOk : SaidaFalha;
    }

    /// <summary>Abre a janela de instalação.</summary>
    private static int ExecutarInterface()
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
        app.Run(new JanelaInstalador());
        return 0;
    }

    /// <summary>Texto de ajuda da linha de comando.</summary>
    public static string Uso() => string.Join(Environment.NewLine, new[]
    {
        "Uso:",
        "  TranalSetup.exe                            abre a janela de instalação",
        "  TranalSetup.exe --instalar <pasta>         instala em modo silencioso",
        "  TranalSetup.exe --desinstalar              desinstala o TRANAL SENTINEL",
        "  TranalSetup.exe --simular                  mostra o plano sem alterar nada",
        "  TranalSetup.exe --simular --instalar <p>   mostra o plano só da instalação",
        "  TranalSetup.exe --simular --desinstalar    mostra o plano só da desinstalação",
    });
}
