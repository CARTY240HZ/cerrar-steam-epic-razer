@echo off
setlocal enabledelayedexpansion
title Cerrar Steam, Epic y Razer
color 0F
set QUOTE=^"

for /F %%E in ('echo prompt $E^| cmd') do set "ESC=%%E"
set "RED=%ESC%[91m"
set "GREEN=%ESC%[92m"
set "YELLOW=%ESC%[93m"
set "CYAN=%ESC%[96m"
set "GRAY=%ESC%[90m"
set "BOLD=%ESC%[1m"
set "RST=%ESC%[0m"

set "found=0"
for %%P in (steam epic razer rz) do (
    tasklist /FI "IMAGENAME eq %%P*" 2>nul | findstr /I /B "%%P" >nul
    if not errorlevel 1 set "found=1"
)

cls
powershell -NoProfile -Command "$e=[char]27;$ax=@('    ###    #     #  #######','   #   #   ##   ##  #      ','  #     #  # # # #  #####  ','  #######  #  #  #  #      ','  #     #  #     #  #######');$colors=@(196,202,208,214,220);if($env:FOUND -eq '1'){$frames=0,10,20,30,40,50,60,70,80,90,100}else{$frames=0,50,100};foreach($p in $frames){Clear-Host;Write-Host '';Write-Host '';for($i=0;$i -lt $ax.Length;$i++){Write-Host ('       '+$e+'[38;5;'+$colors[$i]+'m'+$ax[$i]+$e+'[0m')};Write-Host '';Write-Host '       Cargando...' -ForegroundColor DarkGray;$c=[math]::Floor($p/10);$bar='['+('#'*$c)+('-'*(10-$c))+']';Write-Host ('       '+$bar+' '+$p+'%%') -ForegroundColor Green;Start-Sleep -Milliseconds 68}"
cls
set "LOG=%~dp0log.txt"
echo [%date% %time%] --- Inicio --- >> "%LOG%"

echo   %CYAN%======================================================%RST%
echo   %RED%%BOLD%           CERRAR STEAM / EPIC / RAZER%RST%
echo   %CYAN%======================================================%RST%
echo.

set "total=0"
set "errCount=0"
set "skipCount=0"
for %%P in (steam epic razer rz) do (
    set "pat=%%P*"
    set "esRazer=0"
    if /I "%%P"=="razer" set "esRazer=1"
    if /I "%%P"=="rz" set "esRazer=1"

    for /f "tokens=1 delims=," %%A in ('tasklist /FI "IMAGENAME eq !pat!" /FO CSV /NH 2^>nul') do (
        set "raw=%%A"
        if "!raw:~0,1!"=="!QUOTE!" (
            set "img=%%~A"
            set "padded=!img!                                        "
            set "padded=!padded:~0,22!"
            set "esPerif=0"
            if "!esRazer!"=="1" (
                echo !img! | findstr /I "Manager" >nul
                if not errorlevel 1 set "esPerif=1"
            )
            if "!esPerif!"=="1" (
                echo     %YELLOW%[--]%RST%      !padded! %GRAY%^(periferico, se mantiene activo^)%RST%
                echo [%date% %time%] OMITIDO ^(periferico^): !img! >> "%LOG%"
                set /a skipCount+=1
            ) else (
                title Cerrando: !img!
                taskkill /F /IM "!img!" /T >nul 2>&1
                if errorlevel 1 (
                    echo     %RED%[ER]%RST%      !padded! %GRAY%^(no se pudo cerrar^)%RST%
                    echo [%date% %time%] ERROR al cerrar: !img! >> "%LOG%"
                    set /a errCount+=1
                ) else (
                    echo     %GREEN%[OK]%RST%      !padded!
                    echo [%date% %time%] CERRADO: !img! >> "%LOG%"
                    set /a total+=1
                )
            )
        )
    )
)
title Cerrar Steam, Epic y Razer - Completado

if !total!==0 if !errCount!==0 if !skipCount!==0 echo     %GRAY%^(nada de Steam/Epic/Razer estaba activo^)%RST%

echo.
echo   %CYAN%------------------------------------------------------%RST%
echo     %GREEN%OK: !total!%RST%    %RED%Errores: !errCount!%RST%    %GRAY%Omitidos: !skipCount!%RST%
echo     %RED%%BOLD%Made by AXE%RST%
echo   %CYAN%------------------------------------------------------%RST%
echo.
<nul set /p "=  %GRAY%Cerrando ventana en%RST% "
for %%C in (3 2 1) do (
    <nul set /p "=%YELLOW%%%C%RST%.. "
    ping -n 2 127.0.0.1 >nul
)
exit
