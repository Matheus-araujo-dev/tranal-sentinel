using System.IO;
using System.Text;
using TranalSetup.Dominio;
using TranalSetup.Infra;

namespace TranalSetup.Servicos;

/// <summary>
/// Monta o texto do plano mostrado no modo --simular.
///
/// O modo --simular não escreve nada no sistema: ele só mostra o que seria
/// feito. Por isso ele usa os mesmos planos da instalação real — o texto não
/// pode divergir do que o instalador faria.
/// </summary>
public sealed class ServicoSimulacao
{
    private const string Divisa = "------------------------------------------------------------";

    private readonly ContextoSistema _contexto;
    private readonly IRegistro _registro;
    private readonly ITarefaAgendada _tarefa;

    /// <summary>Cria o simulador com o contexto do sistema, o registro e o Agendador de Tarefas (só leitura).</summary>
    public ServicoSimulacao(ContextoSistema contexto, IRegistro registro, ITarefaAgendada tarefa)
    {
        _contexto = contexto;
        _registro = registro;
        _tarefa = tarefa;
    }

    /// <summary>Plano das três operações: instalar, desinstalar e o que a janela faria.</summary>
    public string MontarPlanoCompleto(OpcoesInstalacao opcoes, string pastaPayload)
    {
        var texto = new StringBuilder();
        texto.AppendLine(Cabecalho("PLANO GERAL"));
        texto.AppendLine();

        texto.Append(MontarPlanoInstalacao(opcoes, pastaPayload));
        texto.AppendLine();
        texto.Append(MontarPlanoDesinstalacao());
        texto.AppendLine();
        texto.Append(MontarPlanoJanela(opcoes, pastaPayload));

        return texto.ToString();
    }

    /// <summary>Plano detalhado da instalação.</summary>
    public string MontarPlanoInstalacao(OpcoesInstalacao opcoes, string pastaPayload)
    {
        var plano = new ConstrutorPlano(_registro, _tarefa).MontarPlanoInstalacao(_contexto, opcoes, pastaPayload);
        var texto = new StringBuilder();

        texto.AppendLine(Secao("1) INSTALAR"));
        texto.AppendLine($"Payload ...........: {plano.PastaPayload}");
        texto.AppendLine($"Destino ...........: {plano.Destino.Pasta}");
        texto.AppendLine($"Instalação atual ..: {(plano.InstalacaoExistente ? "sim (será substituída)" : "não")}");
        texto.AppendLine($"Versão do payload .: {(plano.VersaoPayload.Length == 0 ? "(não informada)" : plano.VersaoPayload)}");
        texto.AppendLine($"Payload completo ..: {(plano.PayloadCompleto ? "sim" : "NÃO")}");
        texto.AppendLine();
        texto.AppendLine("Arquivos:");
        foreach (ArquivoPlano arquivo in plano.Arquivos)
        {
            texto.AppendLine($"  [{Rotulo(arquivo.Situacao)}] {arquivo.Nome}");
            texto.AppendLine($"      origem: {arquivo.Origem}{(arquivo.Situacao == SituacaoArquivo.AusenteNoPayload ? "  (arquivo ausente no payload)" : "")}");
            texto.AppendLine($"      destino: {arquivo.Destino}");
            if (arquivo.Situacao == SituacaoArquivo.Substituir && arquivo.TamanhoInstalado > 0)
            {
                texto.AppendLine($"      substitui: {arquivo.TamanhoInstalado} bytes de {arquivo.ModificadoInstalado:yyyy-MM-dd HH:mm:ss}");
            }
            if (arquivo.Situacao == SituacaoArquivo.Igual)
                texto.AppendLine("      nada a fazer: o arquivo instalado é idêntico ao do payload");
        }
        texto.AppendLine($"  [COPIAR] {CaminhosInstalacao.NomeInstalador}");
        texto.AppendLine($"      origem: {Environment.ProcessPath ?? "(o próprio instalador)"}");
        texto.AppendLine($"      destino: {plano.CaminhoInstaladorInstalado}");
        texto.AppendLine("      (o instalador fica na pasta de instalação para poder desinstalar)");
        texto.AppendLine();
        texto.AppendLine("Atalhos:");
        foreach (PlanoAtalho atalho in plano.Atalhos)
        {
            texto.AppendLine($"  [CRIAR] {atalho.Descricao}");
            texto.AppendLine($"      atalho: {atalho.Caminho}");
            texto.AppendLine($"      aponta para: {atalho.Dados.Alvo}");
            texto.AppendLine($"      ícone: {atalho.Dados.Icone}");
        }
        texto.AppendLine();
        texto.AppendLine("Registro e inicialização automática:");
        if (plano.DescricaoTarefaIniciar is null)
        {
            texto.AppendLine("  [NÃO GRAVAR] inicialização automática (opção desligada)");
        }
        else
        {
            texto.AppendLine($"  [CRIAR] tarefa agendada \"{plano.NomeTarefaIniciar}\"");
            texto.AppendLine($"      {plano.DescricaoTarefaIniciar}");
            texto.AppendLine("      (tarefa com privilégio máximo: abre junto com o Windows sem pedir UAC)");
        }
        texto.AppendLine($"  [GRAVAR] HKLM\\{CaminhosInstalacao.ChaveDesinstalacao}");
        EntradaDesinstalacao entrada = plano.EntradaDesinstalacao;
        texto.AppendLine($"      DisplayName = {entrada.DisplayName}");
        texto.AppendLine($"      DisplayVersion = {entrada.DisplayVersion}");
        texto.AppendLine($"      Publisher = {entrada.Publisher}");
        texto.AppendLine($"      InstallLocation = {entrada.InstallLocation}");
        texto.AppendLine($"      DisplayIcon = {entrada.DisplayIcon}");
        texto.AppendLine($"      UninstallString = {entrada.UninstallString}");
        texto.AppendLine("      NoModify = 1");
        texto.AppendLine("      NoRepair = 1");
        texto.AppendLine();

        if (plano.PastaInstalacaoAnterior is not null)
        {
            texto.AppendLine("Troca de pasta (a instalação anterior é removida depois):");
            texto.AppendLine($"  [REMOVER] {plano.PastaInstalacaoAnterior}");
            texto.AppendLine("      (os arquivos de lá saem depois que a nova instalação estiver pronta)");
            texto.AppendLine();
        }

        texto.AppendLine($"Log ...............: {CaminhosInstalacao.ArquivoLog(_contexto)}");

        return texto.ToString();
    }

    /// <summary>Plano detalhado da desinstalação.</summary>
    public string MontarPlanoDesinstalacao()
    {
        var plano = new ConstrutorPlano(_registro, _tarefa).MontarPlanoDesinstalacao(_contexto);
        var texto = new StringBuilder();

        texto.AppendLine(Secao("2) DESINSTALAR"));
        texto.AppendLine($"Destino ...........: {plano.Destino.Pasta}");
        texto.AppendLine($"Origem do destino : {plano.OrigemDestino}");
        texto.AppendLine();
        texto.AppendLine("Arquivos removidos:");
        foreach (string arquivo in plano.ArquivosRemovidos)
            texto.AppendLine($"  [REMOVER] {arquivo}{(File.Exists(arquivo) ? "" : "  (não existe)")}");
        texto.AppendLine();
        texto.AppendLine("Atalhos removidos:");
        foreach (string atalho in plano.AtalhosRemovidos)
            texto.AppendLine($"  [REMOVER] {atalho}{(File.Exists(atalho) ? "" : "  (não existe)")}");
        texto.AppendLine();
        texto.AppendLine("Registro e inicialização automática:");
        texto.AppendLine($"  [REMOVER] tarefa agendada \"{plano.NomeTarefaIniciar}\""
            + (plano.TarefaIniciarExiste ? "" : "  (não existe)"));
        texto.AppendLine($"  [REMOVER] HKLM\\{plano.ChaveDesinstalacao}"
            + (plano.ChaveDesinstalacaoExiste ? "" : "  (não existe)"));
        texto.AppendLine();
        texto.AppendLine("Preservado (configurações do usuário):");
        texto.AppendLine($"  [MANTER] {plano.PastaConfiguracoesPreservada}");

        return texto.ToString();
    }

    /// <summary>O que a janela de instalação faria (se o usuário confirmar).</summary>
    private string MontarPlanoJanela(OpcoesInstalacao opcoes, string pastaPayload)
    {
        var texto = new StringBuilder();

        texto.AppendLine(Secao("3) JANELA DE INSTALAÇÃO"));
        texto.AppendLine("Título ............: Instalar TRANAL SENTINEL");
        texto.AppendLine($"Destino ...........: {opcoes.Destino.Pasta}");
        texto.AppendLine("Campo de destino ..: editável, com botão para escolher outra pasta");
        texto.AppendLine($"Payload detectado .: {pastaPayload}");
        texto.AppendLine($"[{(opcoes.IniciarComWindows ? "X" : " ")}] Iniciar com o Windows");
        texto.AppendLine($"[{(opcoes.AtalhoAreaTrabalho ? "X" : " ")}] Atalho na área de trabalho");
        texto.AppendLine($"[{(opcoes.ReinstalarSobreInstalacaoExistente ? "X" : " ")}] Já instalar (substitui uma instalação existente)");
        texto.AppendLine("Botões ............: INSTALAR / CANCELAR");
        texto.AppendLine("Ao terminar .......: mostra o resultado (sucesso ou erro) e fecha.");

        return texto.ToString();
    }

    /// <summary>Cabeçalho do texto de simulação (usado também nos planos de uma operação só).</summary>
    public static string Cabecalho(string titulo) => string.Join(Environment.NewLine, new[]
    {
        "TRANAL SENTINEL - INSTALADOR",
        "============================================================",
        $"SIMULAÇÃO ({titulo}): nada será criado, alterado ou removido.",
        string.Empty,
    });

    private static string Secao(string titulo) => string.Join(Environment.NewLine, new[]
    {
        Divisa,
        titulo,
        Divisa,
    });

    private static string Rotulo(SituacaoArquivo situacao) => situacao switch
    {
        SituacaoArquivo.Copiar => "COPIAR",
        SituacaoArquivo.Substituir => "SUBSTITUIR",
        SituacaoArquivo.Igual => "IGUAL",
        SituacaoArquivo.AusenteNoPayload => "AUSENTE",
        _ => situacao.ToString().ToUpperInvariant(),
    };
}
