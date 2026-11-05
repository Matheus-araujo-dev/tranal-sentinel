@echo off
rem Gera o TRANAL SENTINEL pronto para levar a outro PC (nao precisa instalar o .NET la).
rem Saida: ..\TranalSentinel-dist\TranalSentinel.exe + LEIA-ME.txt
rem Sempre recompila do zero a partir do codigo atual desta pasta.
cd /d "%~dp0"
echo Fechando o TranalSentinel se estiver aberto ...
taskkill /f /im TranalSentinel.exe >nul 2>&1
echo Gerando TranalSentinel.exe a partir do codigo atual ...
rmdir /s /q obj\Release >nul 2>&1
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -o "..\TranalSentinel-dist" -nologo -v q
if errorlevel 1 (
    echo.
    echo FALHOU. Veja os erros acima. O .exe antigo NAO foi atualizado.
    pause
    exit /b 1
)
copy /y "LEIA-ME.txt" "..\TranalSentinel-dist\LEIA-ME.txt" >nul
echo.
echo Pronto: %~dp0..\TranalSentinel-dist
for %%F in ("..\TranalSentinel-dist\TranalSentinel.exe") do echo Arquivo gerado em %%~tF (confira se e de agora^)
pause
