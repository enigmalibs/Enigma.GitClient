@echo off
rem Publishes Enigma Git Client (Enigma.GitClient) for Windows x64. Runs, from wherever it is started:
rem
rem     dotnet publish .\src\Enigma.GitClient.Desktop\ -c Release -r win-x64 -p:PublishReadyToRun=true
rem
rem A Release build for win-x64, compiled ReadyToRun so it starts faster, which runs on an installed
rem .NET 10 runtime. The result lands in
rem
rem     src\Enigma.GitClient.Desktop\bin\Release\net10.0\win-x64\publish\
rem
rem and dotnet's last line names that folder. Start Enigma.GitClient.Desktop.exe in it.
rem
rem The same publish as publish.ps1, for a cmd prompt: no PowerShell execution policy applies.

setlocal

rem Resolved from this script rather than the current directory, so it runs from anywhere.
set "project=%~dp0..\..\src\Enigma.GitClient.Desktop"

dotnet publish "%project%" -c Release -r win-x64 -p:PublishReadyToRun=true

rem dotnet's exit code is the script's, so a failed publish fails the script.
exit /b %ERRORLEVEL%
