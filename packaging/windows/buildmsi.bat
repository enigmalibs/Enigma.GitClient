@echo off
rem Builds the Windows installer of Enigma Git Client, artifacts\Enigma.GitClient.X.Y.Z.msi, from a cmd
rem prompt: it runs buildmsi.ps1, beside this script, with no PowerShell execution policy in the way.
rem
rem buildmsi.ps1 cleans the repository (git clean -dxf: every file git does not track is deleted),
rem publishes the release for win-x64 with publish.ps1, and builds the MSI with Enigma.Msi. Its help
rem says the rest: powershell -NoProfile -Command "Get-Help .\packaging\windows\buildmsi.ps1 -Full"

setlocal

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0buildmsi.ps1"

rem buildmsi.ps1's exit code is the script's, so a failed build fails the script.
exit /b %ERRORLEVEL%
