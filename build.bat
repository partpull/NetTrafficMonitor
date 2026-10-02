@echo off
setlocal

rem ============================================================
rem  NetTrafficMonitorV1 - build script
rem  Uses the .NET Framework compiler shipped with Windows 11 (csc.exe).
rem  No SDK installation required; output is a single portable exe.
rem  NOTE: this file is intentionally ASCII-only so cmd.exe parses it
rem  correctly regardless of the active console code page.
rem ============================================================

set "ROOT=%~dp0"
set "FW=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319"
set "CSC=%FW%\csc.exe"
set "WPF=%FW%\WPF"

if not exist "%CSC%" (
    echo [ERROR] C# compiler not found: %CSC%
    echo         Please make sure Windows 10/11 and .NET Framework 4.x are installed.
    exit /b 1
)

echo Building...
echo.

"%CSC%" /nologo /target:winexe /optimize+ /platform:anycpu /codepage:65001 ^
  /out:"%ROOT%NetTrafficMonitorV1.exe" ^
  /win32manifest:"%ROOT%app.manifest" ^
  /reference:"%FW%\System.dll" ^
  /reference:"%FW%\System.Core.dll" ^
  /reference:"%FW%\System.Xaml.dll" ^
  /reference:"%WPF%\WindowsBase.dll" ^
  /reference:"%WPF%\PresentationCore.dll" ^
  /reference:"%WPF%\PresentationFramework.dll" ^
  "%ROOT%Program.cs" ^
  "%ROOT%Native.cs" ^
  "%ROOT%Config.cs" ^
  "%ROOT%Autostart.cs" ^
  "%ROOT%SystemSampler.cs" ^
  "%ROOT%NetworkSampler.cs" ^
  "%ROOT%ProxyMonitor.cs" ^
  "%ROOT%Fmt.cs" ^
  "%ROOT%MonitorEngine.cs" ^
  "%ROOT%MainWindow.cs" ^
  "%ROOT%SettingsWindow.cs"

if errorlevel 1 (
    echo.
    echo [FAILED] Compilation failed. See messages above.
    exit /b 1
)

rem Create the Chinese-named copy (built from char codes to stay ASCII-safe)
powershell -NoProfile -Command "$n=[string][char]0x7f51+[char]0x7edc+[char]0x6d41+[char]0x91cf+[char]0x76d1+[char]0x63a7+'V1.exe'; Copy-Item -Force (Join-Path '%~dp0' 'NetTrafficMonitorV1.exe') (Join-Path '%~dp0' $n)"

echo.
echo [OK] Built: %ROOT%NetTrafficMonitorV1.exe
for %%F in ("%ROOT%NetTrafficMonitorV1.exe") do echo      size: %%~zF bytes
exit /b 0