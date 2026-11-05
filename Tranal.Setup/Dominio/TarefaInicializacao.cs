namespace TranalSetup.Dominio;

/// <summary>
/// A tarefa agendada que abre o TRANAL SENTINEL junto com o Windows.
/// </summary>
/// <remarks>
/// <para>
/// <b>Por que tarefa agendada e não a chave Run?</b> O app roda como
/// administrador (precisa ler MSRs, Super I/O e sensores). Um programa com
/// <c>requireAdministrator</c> na chave <c>HKCU\...\Run</c> abre um pedido de
/// UAC a cada login — para um app que fica morando na bandeja, isso é
/// insuportável. Uma tarefa registrada com privilégio máximo sobe já elevada,
/// sem prompt.
/// </para>
/// <para>
/// O XML é gerado por uma função pura: o que o instalador mostra no plano e o
/// que ele grava no Agendador de Tarefas são o mesmo texto, e os testes
/// cobrem a geração sem precisar de Agendador de Tarefas.
/// </para>
/// </remarks>
public static class TarefaInicializacao
{
    /// <summary>Nome da tarefa no Agendador de Tarefas do Windows.</summary>
    public const string NomeTarefa = "TRANAL SENTINEL";

    /// <summary>Nome de usuário no formato DOMINIO\conta (o que o Windows espera).</summary>
    public static string UsuarioAtual()
    {
        string dominio = Environment.UserDomainName;
        string usuario = Environment.UserName;
        return string.IsNullOrWhiteSpace(dominio) ? usuario : $"{dominio}\\{usuario}";
    }

    /// <summary>
    /// Gera o XML da tarefa.
    /// </summary>
    /// <param name="caminhoExecutavel">Caminho completo do executável instalado.</param>
    /// <param name="argumento">Argumento passado para o executável (ex.: <c>--iniciar-como-sistema</c>).</param>
    /// <param name="usuario">Conta que executa a tarefa (DOMINIO\conta).</param>
    public static string GerarXml(string caminhoExecutavel, string argumento, string usuario)
    {
        string pasta = System.IO.Path.GetDirectoryName(caminhoExecutavel) ?? "";
        string comando = $"\"{caminhoExecutavel}\"";

        return $@"<?xml version=""1.0"" encoding=""UTF-16""?>
<Task version=""1.4"" xmlns=""http://schemas.microsoft.com/windows/2004/02/mit/task"">
  <RegistrationInfo>
    <Author>TRANAL</Author>
    <Description>TRANAL SENTINEL - abre junto com o Windows</Description>
  </RegistrationInfo>
  <Triggers>
    <LogonTrigger>
      <Enabled>true</Enabled>
      <UserId>{Escapar(usuario)}</UserId>
    </LogonTrigger>
  </Triggers>
  <Principals>
    <Principal id=""TRANAL"">
      <UserId>{Escapar(usuario)}</UserId>
      <LogonType>InteractiveToken</LogonType>
      <RunLevel>HighestAvailable</RunLevel>
    </Principal>
  </Principals>
  <Settings>
    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
    <AllowStartOnDemand>true</AllowStartOnDemand>
    <Enabled>true</Enabled>
    <Hidden>false</Hidden>
    <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>
    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
    <Priority>7</Priority>
    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
    <AllowHardTerminate>true</AllowHardTerminate>
    <StartWhenAvailable>false</StartWhenAvailable>
    <WakeToRun>false</WakeToRun>
    <IdleSettings>
      <StopOnIdleEnd>false</StopOnIdleEnd>
      <RestartOnIdle>false</RestartOnIdle>
    </IdleSettings>
  </Settings>
  <Actions Context=""TRANAL"">
    <Exec>
      <Command>{Escapar(comando)}</Command>
      <Arguments>{Escapar(argumento)}</Arguments>
      <WorkingDirectory>{Escapar(pasta)}</WorkingDirectory>
    </Exec>
  </Actions>
</Task>";
    }

    /// <summary>Texto curto que descreve a tarefa no plano e no log.</summary>
    public static string Descrever(string caminhoExecutavel, string argumento)
        => $"tarefa agendada \"{NomeTarefa}\": \"{caminhoExecutavel}\" {argumento} (privilégio máximo, sem UAC no login)";

    /// <summary>Escapa os caracteres que o XML não aceita.</summary>
    private static string Escapar(string valor) => valor
        .Replace("&", "&amp;")
        .Replace("<", "&lt;")
        .Replace(">", "&gt;")
        .Replace("\"", "&quot;");
}
