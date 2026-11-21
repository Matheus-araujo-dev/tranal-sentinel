; ---------------------------------------------------------------------------
; TRANAL SENTINEL - instalador (Inno Setup 6)
;
; Segue a DEC-APPS-DISTRIBUICAO-001: um TranalSentinel-Setup.exe só, com o app
; dentro, na pasta {autopf}\Tranal Group\Tranal Sentinel. É o mesmo arquivo que
; o usuário baixa e que o atualizador do app roda em modo silencioso
; (/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS).
;
; Compilação (o CI faz isso no publicar.yml):
;
;   iscc /DMyAppVersion=0.37.0 /DPayloadDir=..\dist /DOutputDir=..\dist-setup installer\tranal-sentinel.iss
;
;   MyAppVersion  versão da release (a mesma gravada no TranalSentinel.exe)
;   PayloadDir    pasta com TranalSentinel.exe, LEIA-ME.txt, COPYING, NOTICE.txt
;                 e PawnIO_setup.exe (o instalador oficial do driver)
;   OutputDir     onde o TranalSentinel-Setup.exe é gravado
;
; Decisões:
;  - O AppId nunca muda: é ele que faz a versão nova substituir a velha (e é o
;    nome da entrada de desinstalação, "{AppId}_is1").
;  - A tarefa agendada de iniciar com o Windows é o mesmo XML que o instalador
;    antigo em C# gravava (TarefaInicializacao.GerarXml, até a v0.36): logon do
;    usuário que instalou, privilégio máximo, sem prazo de execução e sem parar
;    na bateria. Pela linha de comando do schtasks (/sc onlogon) o Windows cria
;    a tarefa com "não iniciar na bateria" ligado, e o notebook ficaria sem o
;    app no login. Por isso o XML, e não o /tr.
;  - A instalação antiga (o TranalSetup.exe em C#, até a v0.36) é migrada aqui:
;    ela é descoberta pelo InstallLocation da entrada "Uninstall\Tranal
;    Sentinel" (o usuário podia ter escolhido qualquer pasta), e a pasta velha,
;    os atalhos e a entrada de desinstalação saem depois que a instalação nova
;    está no lugar. A tarefa agendada tem o mesmo nome e é regravada com o
;    caminho novo. As configurações (%AppData%\TranalSentinel) e o histórico
;    (%LOCALAPPDATA%\TranalSentinel) não são tocados - nem aqui, nem na
;    desinstalação.
;  - A raiz que o usuário escolheu para a instalação antiga é respeitada: quem
;    tinha o app em E:\Program Files\Tranal Sentinel passa para
;    E:\Program Files\Tranal Group\Tranal Sentinel (e não para o disco C:).
;  - O app reabre no fim também na instalação silenciosa (a entrada do [Run]
;    não tem skipifsilent): é o reinício automático da atualização.
;
; Este arquivo é gravado em UTF-8 COM BOM: sem o BOM o Inno Setup 6 lê o
; script como ANSI e os acentos saem errados.
; ---------------------------------------------------------------------------

#ifndef PayloadDir
  #define PayloadDir AddBackslash(SourcePath) + "..\dist"
#endif
; Sem /DMyAppVersion (build local pelo publicar.bat), a versão é a gravada no
; próprio TranalSentinel.exe: "0.36.0.0" vira "0.36.0". O instalador nunca sai
; com um número diferente do app que ele carrega.
#ifndef MyAppVersion
  #define MyAppVersion RemoveFileExt(GetVersionNumbersString(PayloadDir + "\TranalSentinel.exe"))
#endif
#ifndef OutputDir
  #define OutputDir AddBackslash(SourcePath) + "..\dist-setup"
#endif

#define AppName "TRANAL SENTINEL"
#define AppPasta "Tranal Sentinel"
#define AppGroup "Tranal Group"
#define AppPublisher "Tranal"
#define AppExeName "TranalSentinel.exe"
#define AppHomeUrl "https://github.com/Matheus-araujo-dev/tranal-sentinel"
#define NomeTarefa "TRANAL SENTINEL"
; O GUID do AppId (nunca muda). A entrada de desinstalação do Inno é "{GUID}_is1".
#define AppGuid "6E2B9C41-7D3A-4F18-B05E-93A1C47D2F86"

[Setup]
AppId={{{#AppGuid}}
AppName={#AppName}
AppVersion={#MyAppVersion}
AppVerName={#AppName} {#MyAppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppHomeUrl}
AppSupportURL={#AppHomeUrl}/issues
AppUpdatesURL={#AppHomeUrl}/releases/latest
DefaultDirName={code:PastaPadrao}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
DisableDirPage=auto
UsePreviousAppDir=yes
UsePreviousTasks=yes
OutputBaseFilename=TranalSentinel-Setup
OutputDir={#OutputDir}
; zip, e não lzma2: no Tranal Uninstaller o McAfee bloqueava o setup com carga
; LZMA (falso positivo conhecido). O executável do app é self-contained e já
; vem comprimido por dentro: a diferença de tamanho é pequena.
Compression=zip
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
LicenseFile={#PayloadDir}\LICENSE
UninstallDisplayName={#AppName}
UninstallDisplayIcon={app}\{#AppExeName}
SetupIconFile={#SourcePath}\..\Assets\app.ico
; force: o app que segura um arquivo da pasta é fechado sem perguntar - é o
; que a atualização silenciosa precisa. Quem reabre é o [Run].
CloseApplications=force
RestartApplications=no
SetupLogging=yes
VersionInfoVersion={#MyAppVersion}
VersionInfoProductName={#AppName}
VersionInfoProductTextVersion={#MyAppVersion}

[Languages]
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"

[CustomMessages]
brazilianportuguese.IniciarComWindows=Abrir o TRANAL SENTINEL junto com o Windows (na bandeja, sem pedir UAC)
brazilianportuguese.Desinstalar=Desinstalar o TRANAL SENTINEL

[Tasks]
Name: "iniciarcomwindows"; Description: "{cm:IniciarComWindows}"
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; ignoreversion: a versão nova sempre substitui a instalada, mesmo quando o
; número do arquivo não sobe (um rebuild local sai com o piso do
; Directory.Build.props).
Source: "{#PayloadDir}\{#AppExeName}"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PayloadDir}\LEIA-ME.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PayloadDir}\LICENSE"; DestDir: "{app}"; Flags: ignoreversion
; Licença do PawnIO (o driver dos sensores): vai junto do app.
Source: "{#PayloadDir}\COPYING"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PayloadDir}\NOTICE.txt"; DestDir: "{app}"; Flags: ignoreversion
; O instalador oficial do driver PawnIO (namazso, assinado), sem modificação:
; sem o driver, o app não lê temperatura nem consumo da CPU. Ele vai para a
; pasta temporária e só roda quando o PawnIO ainda não está instalado (o
; [Run] abaixo). O CI baixa a versão fixada e confere o hash e a assinatura.
Source: "{#PayloadDir}\PawnIO_setup.exe"; DestDir: "{tmp}"; Flags: deleteafterinstall; Check: PrecisaDoPawnIO

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"
Name: "{autoprograms}\{cm:Desinstalar}"; Filename: "{uninstallexe}"
; O atalho da área de trabalho também nasce quando a instalação antiga tinha
; um: a migração não pode tirar do usuário o atalho que ele já usava.
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"; Check: QuerAtalhoAreaTrabalho

[Run]
; O driver PawnIO, antes de o app abrir: é ele que dá a temperatura e o consumo
; da CPU. Só quando ainda não está instalado (a atualização não o reinstala),
; e na instalação silenciosa também. O desinstalador do app NÃO remove o
; PawnIO: o driver é compartilhado (LibreHardwareMonitor, FanControl e outros
; usam o mesmo) e tem a sua própria entrada em "Apps e recursos".
Filename: "{tmp}\PawnIO_setup.exe"; Parameters: "-install -silent"; \
    StatusMsg: "Instalando o driver PawnIO (temperatura e consumo da CPU)..."; \
    Flags: runhidden waituntilterminated; Check: PrecisaDoPawnIO
; Reabre o app no fim. Sem skipifsilent: na atualização silenciosa (o app
; fechou para ela acontecer) é este passo que traz o app de volta.
; runascurrentuser: o postinstall rodaria como o usuário NÃO elevado, e o app
; exige administrador ("CreateProcess falhou; código 740").
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchProgram,{#AppName}}"; \
    Flags: nowait postinstall runascurrentuser

[UninstallRun]
; O app (e o coletor de sensores, que é o mesmo executável) sai antes dos
; arquivos; depois a tarefa agendada, para não sobrar um login que tenta abrir
; um executável apagado.
Filename: "{sys}\taskkill.exe"; Parameters: "/F /T /IM {#AppExeName}"; Flags: runhidden waituntilterminated; RunOnceId: "FecharApp"
Filename: "{sys}\schtasks.exe"; Parameters: "/Delete /TN ""{#NomeTarefa}"" /F"; Flags: runhidden waituntilterminated; RunOnceId: "RemoverTarefa"

[Code]
{ O PawnIO falta? É a mesma chave que o app confere
  (SensorMonitor.IsPawnIOInstalled), na visão de 64 bits do registro. }
function PrecisaDoPawnIO: Boolean;
begin
  Result := not RegKeyExists(HKLM64, 'SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO');
end;

const
  { A entrada que o instalador antigo em C# (TranalSetup.exe, até a v0.36)
    gravava em "Apps e recursos". O Inno usa outra ("AppId_is1"). }
  ChaveLegada = 'Software\Microsoft\Windows\CurrentVersion\Uninstall\Tranal Sentinel';
  NomeAtalhoLegado = 'TRANAL SENTINEL.lnk';
  ChaveRun = 'Software\Microsoft\Windows\CurrentVersion\Run';

var
  { Pasta da instalação antiga ('' quando não existe). }
  PastaLegada: String;
  { A instalação antiga tinha atalho na área de trabalho do usuário. }
  TinhaAtalhoLegado: Boolean;
  { A chave antiga existe (mesmo sem InstallLocation): ela sempre sai no fim.
    É a mesma regra do AutoUpdate.InstalacaoAntigaPresente, que só olha a
    chave - se o setup deixasse a chave, o app ofereceria a mesma versão para
    sempre. }
  TemChaveLegada: Boolean;
  { A tarefa agendada já existia quando o setup começou. }
  TinhaTarefa: Boolean;
  { Já havia uma instalação deste instalador (Inno) antes. }
  TinhaInstalacaoInno: Boolean;

{ ------------------------------------------------------------------------- }
{ Instalação antiga                                                          }
{ ------------------------------------------------------------------------- }

function LerPastaLegada: String;
begin
  Result := '';
  { O instalador antigo gravava na visão de 64 bits; a de 32 é conferida por
    garantia (um build x86 antigo cairia lá). }
  if not RegQueryStringValue(HKLM64, ChaveLegada, 'InstallLocation', Result) then
    if not RegQueryStringValue(HKLM32, ChaveLegada, 'InstallLocation', Result) then
      Result := '';
  Result := Trim(Result);
  if Result <> '' then
    Result := RemoveBackslashUnlessRoot(Result);
end;

function AtalhoLegadoAreaTrabalho: String;
begin
  Result := ExpandConstant('{userdesktop}\') + NomeAtalhoLegado;
end;

function TarefaExiste: Boolean;
var
  Codigo: Integer;
begin
  Result := Exec(ExpandConstant('{sys}\schtasks.exe'), '/Query /TN "{#NomeTarefa}"', '', SW_HIDE,
                 ewWaitUntilTerminated, Codigo) and (Codigo = 0);
end;

function InitializeSetup(): Boolean;
begin
  TemChaveLegada := RegKeyExists(HKLM64, ChaveLegada) or RegKeyExists(HKLM32, ChaveLegada);
  TinhaInstalacaoInno := RegKeyExists(HKLM64, 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{{#AppGuid}}_is1');
  TinhaTarefa := TarefaExiste;
  PastaLegada := LerPastaLegada;
  TinhaAtalhoLegado := (PastaLegada <> '') and FileExists(AtalhoLegadoAreaTrabalho);
  if PastaLegada <> '' then
    Log('Instalação antiga (TranalSetup.exe) encontrada em: ' + PastaLegada);
  Result := True;
end;

{ A regra única do destino (todo app Tranal mora em Tranal Group): a pasta
  escolhida é a RAIZ, e o app fica em <raiz>\Tranal Group\Tranal Sentinel.
  Quem aponta para a própria pasta do app (ou para a do grupo) não ganha uma
  pasta dentro da outra:
    X\Tranal Group\Tranal Sentinel  ->  ela mesma
    X\Tranal Group                  ->  X\Tranal Group\Tranal Sentinel
    X\Tranal Sentinel               ->  X\Tranal Group\Tranal Sentinel
    X                               ->  X\Tranal Group\Tranal Sentinel }
function PastaNoGrupo(const Escolhida: String): String;
var
  Pasta, Nome, Pai: String;
begin
  Pasta := RemoveBackslashUnlessRoot(Trim(Escolhida));
  Nome := ExtractFileName(Pasta);
  Pai := ExtractFileDir(Pasta);

  if (CompareText(Nome, '{#AppPasta}') = 0) and (CompareText(ExtractFileName(Pai), '{#AppGroup}') = 0) then
    Result := Pasta
  else if CompareText(Nome, '{#AppGroup}') = 0 then
    Result := AddBackslash(Pasta) + '{#AppPasta}'
  else if CompareText(Nome, '{#AppPasta}') = 0 then
    Result := AddBackslash(Pai) + '{#AppGroup}\{#AppPasta}'
  else
    Result := AddBackslash(Pasta) + '{#AppGroup}\{#AppPasta}';
end;

{ Pasta padrão. Sem instalação antiga: autopf\Tranal Group\Tranal Sentinel.
  Com ela, a raiz que o usuário escolheu na época é mantida:
  E:\Program Files\Tranal Sentinel vira E:\Program Files\Tranal Group\Tranal Sentinel. }
function PastaPadrao(Param: String): String;
begin
  if PastaLegada = '' then
    Result := ExpandConstant('{autopf}\{#AppGroup}\{#AppPasta}')
  else
    Result := PastaNoGrupo(PastaLegada);
end;

{ A pasta da tela de destino passa pela mesma regra. }
function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  if CurPageID = wpSelectDir then
    WizardForm.DirEdit.Text := PastaNoGrupo(WizardForm.DirEdit.Text);
end;

function QuerAtalhoAreaTrabalho: Boolean;
begin
  Result := WizardIsTaskSelected('desktopicon') or TinhaAtalhoLegado;
end;

{ ------------------------------------------------------------------------- }
{ O app aberto                                                               }
{ ------------------------------------------------------------------------- }

{ O app pode estar em qualquer pasta (a instalação antiga fica fora da pasta nova):
  o CloseApplications só enxerga a pasta nova. Por isso o processo é procurado
  pelo nome, no WMI. }
function AppAberto: Boolean;
var
  Locator, Servico, Lista: Variant;
begin
  Result := False;
  try
    Locator := CreateOleObject('WbemScripting.SWbemLocator');
    Servico := Locator.ConnectServer('.', 'root\CIMV2');
    Lista := Servico.ExecQuery('SELECT ProcessId FROM Win32_Process WHERE Name = ''{#AppExeName}''');
    Result := Lista.Count > 0;
  except
    { Sem WMI não dá para saber: o taskkill abaixo cuida do caso. }
    Log('Não foi possível consultar os processos pelo WMI: ' + GetExceptionMessage);
    Result := True;
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  Tentativas, Codigo: Integer;
begin
  Result := '';
  NeedsRestart := False;

  { Na atualização, o app fecha sozinho logo depois de chamar o setup (e grava
    o tráfego do dia no fechamento). Ele ganha até 30 s para sair por conta
    própria antes de ser encerrado. }
  if WizardSilent then
  begin
    Tentativas := 0;
    while AppAberto and (Tentativas < 60) do
    begin
      Sleep(500);
      Tentativas := Tentativas + 1;
    end;
  end;

  if AppAberto then
  begin
    { Na instalação com janela, o dono decide: fechar à força perde os últimos
      segundos do tráfego do dia (o fechamento normal é que grava). }
    if not WizardSilent then
      if MsgBox('O TRANAL SENTINEL está aberto e precisa ser fechado para a instalação continuar.' + #13#10#13#10 +
                'Fechar agora? (Para fechar você mesmo: ícone na bandeja -> Sair, e depois Tentar de novo.)',
                mbConfirmation, MB_YESNO) <> IDYES then
      begin
        Result := 'Feche o TRANAL SENTINEL (ícone na bandeja -> Sair) e rode o instalador de novo.';
        Exit;
      end;

    Log('Encerrando o TRANAL SENTINEL que continua aberto.');
    { Sem /T: na atualização este setup é filho do app, e encerrar a árvore
      mataria o próprio instalador. O coletor de sensores é o mesmo
      executável e cai pelo /IM. }
    Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM {#AppExeName}', '', SW_HIDE, ewWaitUntilTerminated, Codigo);
    Sleep(1500);
  end;
end;

{ ------------------------------------------------------------------------- }
{ Tarefa agendada (iniciar com o Windows)                                    }
{ ------------------------------------------------------------------------- }

function EscaparXml(const Valor: String): String;
begin
  Result := Valor;
  StringChangeEx(Result, '&', '&amp;', True);
  StringChangeEx(Result, '<', '&lt;', True);
  StringChangeEx(Result, '>', '&gt;', True);
  StringChangeEx(Result, '"', '&quot;', True);
end;

function UsuarioAtual: String;
var
  Dominio: String;
begin
  Dominio := GetEnv('USERDOMAIN');
  if Dominio = '' then
    Result := GetUserNameString
  else
    Result := Dominio + '\' + GetUserNameString;
end;

{ O mesmo XML do TarefaInicializacao.GerarXml do instalador antigo, sem a
  declaração de XML: o texto vai para o Register-ScheduledTask como string, e
  uma declaração de codificação ali faz o Windows recusar o XML ("não é
  possível alternar codificação", medido em 20/11/2026). }
function XmlDaTarefa(const Exe, Pasta, Usuario: String): String;
var
  U: String;
begin
  U := EscaparXml(Usuario);
  Result :=
    '<Task version="1.4" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">' + #13#10 +
    '  <RegistrationInfo>' + #13#10 +
    '    <Author>TRANAL</Author>' + #13#10 +
    '    <Description>TRANAL SENTINEL - abre junto com o Windows</Description>' + #13#10 +
    '  </RegistrationInfo>' + #13#10 +
    '  <Triggers>' + #13#10 +
    '    <LogonTrigger>' + #13#10 +
    '      <Enabled>true</Enabled>' + #13#10 +
    '      <UserId>' + U + '</UserId>' + #13#10 +
    '    </LogonTrigger>' + #13#10 +
    '  </Triggers>' + #13#10 +
    '  <Principals>' + #13#10 +
    '    <Principal id="TRANAL">' + #13#10 +
    '      <UserId>' + U + '</UserId>' + #13#10 +
    '      <LogonType>InteractiveToken</LogonType>' + #13#10 +
    '      <RunLevel>HighestAvailable</RunLevel>' + #13#10 +
    '    </Principal>' + #13#10 +
    '  </Principals>' + #13#10 +
    '  <Settings>' + #13#10 +
    '    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>' + #13#10 +
    '    <AllowStartOnDemand>true</AllowStartOnDemand>' + #13#10 +
    '    <Enabled>true</Enabled>' + #13#10 +
    '    <Hidden>false</Hidden>' + #13#10 +
    '    <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>' + #13#10 +
    '    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>' + #13#10 +
    '    <Priority>7</Priority>' + #13#10 +
    '    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>' + #13#10 +
    '    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>' + #13#10 +
    '    <AllowHardTerminate>true</AllowHardTerminate>' + #13#10 +
    '    <StartWhenAvailable>false</StartWhenAvailable>' + #13#10 +
    '    <WakeToRun>false</WakeToRun>' + #13#10 +
    '    <IdleSettings>' + #13#10 +
    '      <StopOnIdleEnd>false</StopOnIdleEnd>' + #13#10 +
    '      <RestartOnIdle>false</RestartOnIdle>' + #13#10 +
    '    </IdleSettings>' + #13#10 +
    '  </Settings>' + #13#10 +
    '  <Actions Context="TRANAL">' + #13#10 +
    '    <Exec>' + #13#10 +
    '      <Command>' + EscaparXml('"' + Exe + '"') + '</Command>' + #13#10 +
    '      <Arguments>--iniciar-como-sistema</Arguments>' + #13#10 +
    '      <WorkingDirectory>' + EscaparXml(Pasta) + '</WorkingDirectory>' + #13#10 +
    '    </Exec>' + #13#10 +
    '  </Actions>' + #13#10 +
    '</Task>';
end;

{ A tarefa é gravada pelo Register-ScheduledTask, e não pelo schtasks /XML: o
  schtasks só aceita o arquivo em UTF-16 (medido em 20/11/2026: em UTF-8, com
  ou sem BOM, ele responde "O XML está mal construído"), e o Inno não grava
  UTF-16. O PowerShell lê o arquivo em UTF-8 e entrega o texto. }
procedure GravarTarefa;
var
  Arquivo, Caminho, Parametros: String;
  Linhas: TArrayOfString;
  Codigo: Integer;
begin
  Arquivo := ExpandConstant('{tmp}\tranal-tarefa.xml');
  SetArrayLength(Linhas, 1);
  Linhas[0] := XmlDaTarefa(ExpandConstant('{app}\{#AppExeName}'), ExpandConstant('{app}'), UsuarioAtual);
  if not SaveStringsToUTF8File(Arquivo, Linhas, False) then
  begin
    Log('Não foi possível gravar o XML da tarefa agendada.');
    Exit;
  end;

  { -Force regrava a tarefa do mesmo nome: é assim que a tarefa da instalação
    antiga passa a apontar para a pasta nova. O caminho vai entre aspas
    simples do PowerShell (a aspa simples de dentro é dobrada). }
  Caminho := Arquivo;
  StringChangeEx(Caminho, '''', '''''', True);
  Parametros := '-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command "' +
    'Register-ScheduledTask -TaskName ''{#NomeTarefa}'' -Xml ([IO.File]::ReadAllText(''' + Caminho + ''')) -Force | Out-Null; ' +
    'if (-not $?) { exit 1 }"';
  if Exec(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'), Parametros, '', SW_HIDE,
          ewWaitUntilTerminated, Codigo) and (Codigo = 0) then
    Log('Tarefa agendada "{#NomeTarefa}" gravada.')
  else
    Log('A tarefa agendada não foi gravada (código ' + IntToStr(Codigo) + ').');
  DeleteFile(Arquivo);
end;

procedure RemoverTarefa;
var
  Codigo: Integer;
begin
  { Remover uma tarefa que não existe devolve erro: não é problema. }
  Exec(ExpandConstant('{sys}\schtasks.exe'), '/Delete /TN "{#NomeTarefa}" /F', '', SW_HIDE, ewWaitUntilTerminated, Codigo);
end;

{ ------------------------------------------------------------------------- }
{ Migração da instalação antiga                                              }
{ ------------------------------------------------------------------------- }

procedure ApagarArquivo(const Caminho: String);
begin
  if FileExists(Caminho) then
  begin
    if DeleteFile(Caminho) then
      Log('Removido da instalação antiga: ' + Caminho)
    else
      Log('Não foi possível remover (fica para trás): ' + Caminho);
  end;
end;

procedure MigrarInstalacaoAntiga;
var
  Pasta, Pai: String;
  MesmaPasta: Boolean;
begin
  if PastaLegada = '' then
  begin
    { Chave sem pasta (mexida à mão, instalação pela metade): não há o que
      mover, mas a chave sai - senão o app oferece a mesma versão para sempre. }
    if TemChaveLegada then
    begin
      RegDeleteKeyIncludingSubkeys(HKLM64, ChaveLegada);
      RegDeleteKeyIncludingSubkeys(HKLM32, ChaveLegada);
      Log('Entrada de desinstalação antiga (sem pasta) removida.');
    end;
    Exit;
  end;

  Pasta := AddBackslash(PastaLegada);
  MesmaPasta := CompareText(RemoveBackslashUnlessRoot(PastaLegada), RemoveBackslashUnlessRoot(ExpandConstant('{app}'))) = 0;

  { O que é só da instalação antiga sai sempre: o instalador em C# (que
    desinstalava) e o backup do executável. }
  ApagarArquivo(Pasta + 'TranalSetup.exe');
  ApagarArquivo(Pasta + '{#AppExeName}.bak');

  { Na pasta que mudou, saem também os arquivos do app: a cópia nova já está
    no lugar novo. Só os arquivos conhecidos são apagados - nunca a pasta
    inteira, que pode ter algo do usuário. }
  if not MesmaPasta then
  begin
    ApagarArquivo(Pasta + '{#AppExeName}');
    ApagarArquivo(Pasta + 'LEIA-ME.txt');
    ApagarArquivo(Pasta + 'LICENSE');
    ApagarArquivo(Pasta + 'COPYING');
    ApagarArquivo(Pasta + 'NOTICE.txt');
    if RemoveDir(PastaLegada) then
    begin
      Log('Pasta da instalação antiga removida: ' + PastaLegada);
      { A pasta do grupo da v0.36.2 só sai se ficou vazia (outro app Tranal
        pode morar nela). }
      Pai := ExtractFileDir(PastaLegada);
      if CompareText(ExtractFileName(Pai), '{#AppGroup}') = 0 then
        RemoveDir(Pai);
    end
    else
      Log('A pasta da instalação antiga não ficou vazia e foi mantida: ' + PastaLegada);
  end;

  { Atalhos que o instalador antigo criava no perfil do usuário (os novos
    ficam no perfil de todos os usuários). }
  ApagarArquivo(ExpandConstant('{userprograms}\') + NomeAtalhoLegado);
  ApagarArquivo(AtalhoLegadoAreaTrabalho);

  { Valor da chave Run de uma instalação manual antiga: o app usa a tarefa
    agendada, e a Run pediria UAC a cada login. }
  RegDeleteValue(HKCU, ChaveRun, 'TranalSentinel');

  { A entrada de "Apps e recursos" do instalador antigo: o botão Desinstalar
    dela apontaria para um TranalSetup.exe que não existe mais. }
  if RegDeleteKeyIncludingSubkeys(HKLM64, ChaveLegada) then
    Log('Entrada de desinstalação antiga removida.');
  RegDeleteKeyIncludingSubkeys(HKLM32, ChaveLegada);
end;

{ Abrir junto com o Windows é escolha do dono, e a atualização silenciosa não
  muda a escolha: com uma instalação anterior (deste instalador ou do antigo),
  vale o estado real da tarefa no PC - quem desligou continua desligado, e quem
  tem a tarefa ganha o caminho novo. A caixa da tela vale na instalação com
  janela e na primeira instalação. (Não dá para usar só a caixa: na migração o
  Inno não tem escolha anterior para lembrar e a caixa nasce marcada.) }
function QuerTarefa: Boolean;
begin
  if WizardSilent and (TinhaInstalacaoInno or TemChaveLegada) then
    Result := TinhaTarefa
  else
    Result := WizardIsTaskSelected('iniciarcomwindows');
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
  begin
    if QuerTarefa then
      GravarTarefa
    else
      RemoverTarefa;

    { Por último: se algo falhou antes, a instalação antiga continua inteira. }
    MigrarInstalacaoAntiga;
  end;
end;
