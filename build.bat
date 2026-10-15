@echo off
rem Compila "Cerrar Steam y Epic.exe" con el .bat incrustado y icon.ico.
cd /d "%~dp0"
"%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /target:exe /optimize+ ^
  /win32icon:icon.ico ^
  /resource:"Cerrar Steam y Epic.bat",script.bat ^
  /out:"Cerrar Steam y Epic.exe" src\Launcher.cs
if errorlevel 1 (echo Error al compilar & exit /b 1)
echo Listo: Cerrar Steam y Epic.exe
