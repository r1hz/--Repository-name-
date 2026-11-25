@echo off
rem Gera o GEOLAB TI TOOLS pronto para levar a outro PC (nao precisa instalar o .NET la).
rem Saida: ..\Geolab-TI-Tools-dist\GeolabTITools.exe + LEIA-ME.txt
rem Sempre recompila do zero a partir do codigo atual desta pasta.
rem Para Windows de 32 bits: publicar.bat x86
cd /d "%~dp0"
echo Fechando o GEOLAB TI TOOLS se estiver aberto ...
taskkill /f /im GeolabTITools.exe >nul 2>&1
echo Gerando GeolabTITools.exe a partir do codigo atual ...
rmdir /s /q obj\Release >nul 2>&1
if /i "%~1"=="x86" (set RID=win-x86) else (set RID=win-x64)
dotnet publish "ResetExplode.csproj" -c Release -r %RID% --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -o "..\Geolab-TI-Tools-dist" -nologo -v q
if errorlevel 1 (
    echo.
    echo FALHOU. Veja os erros acima. O .exe antigo NAO foi atualizado.
    pause
    exit /b 1
)
copy /y "LEIA-ME.txt" "..\Geolab-TI-Tools-dist\LEIA-ME.txt" >nul
echo.
echo Pronto: %~dp0..\Geolab-TI-Tools-dist
for %%F in ("..\Geolab-TI-Tools-dist\GeolabTITools.exe") do echo Arquivo gerado em %%~tF (confira se e de agora^)
pause
