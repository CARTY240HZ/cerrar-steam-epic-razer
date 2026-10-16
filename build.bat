@echo off
rem Compila "Cerrar Steam y Epic.exe": ventana WPF + WebView2, con todo incrustado (un solo archivo).
cd /d "%~dp0"
set "FW=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319"
"%FW%\csc.exe" /nologo /target:winexe /platform:x64 /optimize+ ^
  /win32icon:icon.ico ^
  /r:"%FW%\WPF\PresentationFramework.dll" /r:"%FW%\WPF\PresentationCore.dll" /r:"%FW%\WPF\WindowsBase.dll" ^
  /r:"%FW%\System.Xaml.dll" /r:System.Web.Extensions.dll /r:System.Drawing.dll ^
  /r:lib\Microsoft.Web.WebView2.Core.dll /r:lib\Microsoft.Web.WebView2.Wpf.dll ^
  /resource:lib\Microsoft.Web.WebView2.Core.dll,Microsoft.Web.WebView2.Core.dll ^
  /resource:lib\Microsoft.Web.WebView2.Wpf.dll,Microsoft.Web.WebView2.Wpf.dll ^
  /resource:lib\WebView2Loader.dll,WebView2Loader.dll ^
  /resource:src\ui.html,ui.html /resource:src\splash.html,splash.html /resource:src\xmark.svg,xmark.svg ^
  /resource:src\brandmark.svg,brandmark.svg ^
  /resource:"Cerrar Steam y Epic.bat",script.bat ^
  /out:"Cerrar Steam y Epic.exe" src\App.cs
if errorlevel 1 (echo Error al compilar & exit /b 1)
echo Listo: Cerrar Steam y Epic.exe
