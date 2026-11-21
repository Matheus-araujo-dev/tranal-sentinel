@echo off
rem Gera o TRANAL SENTINEL pronto para levar a outro PC (nao precisa instalar o .NET la).
rem Saida: ..\TranalSentinel-dist\TranalSentinel.exe (portatil) + TranalSentinel-Setup.exe (instalador)
rem Sempre recompila do zero a partir do codigo atual desta pasta.
rem O instalador e o do Inno Setup (installer\tranal-sentinel.iss): instale o Inno com
rem   winget install JRSoftware.InnoSetup
cd /d "%~dp0"

echo Rodando os testes do app ...
dotnet test TranalSentinel.Tests\TranalSentinel.Tests.csproj -c Release -nologo -v q
if errorlevel 1 (
    echo.
    echo FALHOU nos testes do app. Nada foi gerado.
    pause
    exit /b 1
)

echo Fechando o TranalSentinel se estiver aberto ...
taskkill /f /im TranalSentinel.exe >nul 2>&1
echo Gerando TranalSentinel.exe a partir do codigo atual ...
rmdir /s /q obj\Release >nul 2>&1
dotnet publish TranalSentinel.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -o "..\TranalSentinel-dist" -nologo -v q
if errorlevel 1 (
    echo.
    echo FALHOU. Veja os erros acima. O .exe antigo NAO foi atualizado.
    pause
    exit /b 1
)
copy /y "LEIA-ME.txt" "..\TranalSentinel-dist\LEIA-ME.txt" >nul
copy /y "LICENSE" "..\TranalSentinel-dist\LICENSE" >nul
copy /y "Assets\PawnIO\COPYING" "..\TranalSentinel-dist\COPYING" >nul
copy /y "Assets\PawnIO\NOTICE.txt" "..\TranalSentinel-dist\NOTICE.txt" >nul

rem O instalador do driver PawnIO vai dentro do Setup (a mesma versao e o mesmo
rem hash do publicar.yml). Precisa do gh (GitHub CLI) logado.
echo Baixando e conferindo o instalador do PawnIO ...
gh release download 2.2.0 -R namazso/PawnIO.Setup -p PawnIO_setup.exe -D "..\TranalSentinel-dist" --clobber
powershell -NoProfile -Command "$h=(Get-FileHash '..\TranalSentinel-dist\PawnIO_setup.exe' -Algorithm SHA256).Hash; $s=Get-AuthenticodeSignature '..\TranalSentinel-dist\PawnIO_setup.exe'; if ($h -ne '1F519A22E47187F70A1379A48CA604981C4FCF694F4E65B734AAA74A9FBA3032' -or $s.Status -ne 'Valid' -or $s.SignerCertificate.Subject -notmatch 'O=namazso') { exit 1 }"
if errorlevel 1 (
    echo.
    echo O PawnIO_setup.exe nao foi baixado ou nao confere ^(hash ou assinatura^). O instalador NAO foi gerado.
    pause
    exit /b 1
)

set "ISCC="
if exist "%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe" set "ISCC=%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe"
if exist "%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe" set "ISCC=%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe"
if exist "%ProgramFiles%\Inno Setup 6\ISCC.exe" set "ISCC=%ProgramFiles%\Inno Setup 6\ISCC.exe"
if not defined ISCC (
    echo.
    echo O app foi gerado, mas o Inno Setup nao foi encontrado: instale com winget install JRSoftware.InnoSetup
    pause
    exit /b 1
)

echo Gerando o instalador (TranalSentinel-Setup.exe) ...
"%ISCC%" /Q "/DPayloadDir=%~dp0..\TranalSentinel-dist" "/DOutputDir=%~dp0..\TranalSentinel-dist" installer\tranal-sentinel.iss
if errorlevel 1 (
    echo.
    echo O app foi gerado, mas o instalador FALHOU. Veja os erros acima.
    pause
    exit /b 1
)

echo.
echo Pronto: %~dp0..\TranalSentinel-dist
for %%F in ("..\TranalSentinel-dist\TranalSentinel.exe") do echo App gerado em %%~tF (confira se e de agora^)
for %%F in ("..\TranalSentinel-dist\TranalSentinel-Setup.exe") do echo Instalador gerado em %%~tF
echo.
echo Para instalar: abra o TranalSentinel-Setup.exe (o app vai dentro dele).
pause
