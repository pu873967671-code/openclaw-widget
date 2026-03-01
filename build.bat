@echo off
REM OpenClaw Widget Build Script v2
set CSC=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe

set WPFDIR=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\WPF

echo Building OpenClaw Widget v2...
"%CSC%" /target:winexe /out:OpenClawWidget.exe ^
  /reference:"%WPFDIR%\PresentationFramework.dll" ^
  /reference:"%WPFDIR%\PresentationCore.dll" ^
  /reference:"%WPFDIR%\WindowsBase.dll" ^
  /reference:System.dll ^
  /reference:System.Core.dll ^
  /reference:System.Net.dll ^
  /reference:System.Web.Extensions.dll ^
  /reference:System.Xaml.dll ^
  /reference:System.Drawing.dll ^
  /reference:System.Windows.Forms.dll ^
  Widget.cs

if %ERRORLEVEL% EQU 0 (
    echo.
    echo Done! Run: OpenClawWidget.exe
) else (
    echo.
    echo Build failed.
)
pause
