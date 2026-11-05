@echo off
rem Gera o TRANAL SENTINEL pronto para levar a outro PC (nao precisa instalar o .NET la).
rem Saida: ..\TranalSentinel-dist\TranalSentinel.exe + LEIA-ME.txt + TranalSetup.exe
rem Sempre recompila do zero a partir do codigo atual desta pasta.
cd /d "%~dp0"

echo Rodando os testes ...
dotnet test TranalSentinel.Tests\TranalSentinel.Tests.csproj -c Release -nologo -v q
if errorlevel 1 (
    echo.
    echo FALHOU nos testes. Nada foi gerado.
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

echo Gerando o instalador (TranalSetup.exe) ...
dotnet publish Tranal.Setup\Tranal.Setup.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -o "..\TranalSentinel-dist" -nologo -v q
if errorlevel 1 (
    echo.
    echo O app foi gerado, mas o instalador FALHOU. Veja os erros acima.
    pause
    exit /b 1
)

echo.
echo Pronto: %~dp0..\TranalSentinel-dist
for %%F in ("..\TranalSentinel-dist\TranalSentinel.exe") do echo App gerado em %%~tF (confira se e de agora^)
for %%F in ("..\TranalSentinel-dist\TranalSetup.exe") do echo Instalador gerado em %%~tF
echo.
echo Para instalar: abra o TranalSetup.exe (ele precisa do TranalSentinel.exe na mesma pasta).
pause
