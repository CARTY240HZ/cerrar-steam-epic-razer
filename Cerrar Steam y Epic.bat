@echo off
setlocal enabledelayedexpansion
title Cerrar Steam, Epic y Razer
color 0F
set QUOTE=^"

for /F %%E in ('echo prompt $E^| cmd') do set "ESC=%%E"
set "RED=%ESC%[38;2;248;113;113m"
set "GREEN=%ESC%[38;2;74;222;128m"
set "YELLOW=%ESC%[38;2;251;191;36m"
set "CYAN=%ESC%[38;2;103;232;249m"
set "BRAND=%ESC%[38;2;229;72;77m"
set "LINE=%ESC%[38;2;63;63;70m"
set "WHITE=%ESC%[38;2;244;244;245m"
set "GRAY=%ESC%[38;2;113;113;122m"
set "BOLD=%ESC%[1m"
set "RST=%ESC%[0m"

set "found=0"
for %%P in (steam epic razer rz) do (
    tasklist /FI "IMAGENAME eq %%P*" 2>nul | findstr /I /B "%%P" >nul
    if not errorlevel 1 set "found=1"
)

cls
powershell -NoProfile -Command "$e=[char]27;$ax=@('    ###    ##   ##  #######','   ## ##    ## ##   ##     ','  ##   ##    ###    #####  ','  #######   ## ##   ##     ','  ##   ##  ##   ##  #######');$blk=[string][char]0x2588;$ax=$ax|ForEach-Object{$_.Replace('#',$blk)};$colors=@('250;110;110','242;95;98','236;82;87','229;72;77','214;58;64');if($env:FOUND -eq '1'){$frames=0,10,20,30,40,50,60,70,80,90,100}else{$frames=0,50,100};foreach($p in $frames){Clear-Host;Write-Host '';Write-Host '';for($i=0;$i -lt $ax.Length;$i++){Write-Host ('       '+$e+'[38;2;'+$colors[$i]+'m'+$ax[$i]+$e+'[0m')};Write-Host '';Write-Host ('       '+$e+'[38;2;113;113;122mCargando...'+$e+'[0m');$c=[math]::Floor($p/10);$bar=$e+'[38;2;229;72;77m'+($blk*$c)+$e+'[38;2;63;63;70m'+(([string][char]0x2591)*(10-$c))+$e+'[0m';Write-Host ('       '+$bar+' '+$e+'[38;2;244;244;245m'+$p+'%%'+$e+'[0m');Start-Sleep -Milliseconds 68}"
cls
set "LOG=%~dp0log.txt"
if defined AXE_LOG set "LOG=%AXE_LOG%"
echo [%date% %time%] --- Inicio --- >> "%LOG%"

echo   %LINE%======================================================%RST%
echo   %WHITE%%BOLD%           CERRAR %BRAND%STEAM %LINE%/ %BRAND%EPIC %LINE%/ %BRAND%RAZER%RST%
echo   %LINE%======================================================%RST%
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
echo   %LINE%------------------------------------------------------%RST%
echo     %GREEN%OK: !total!%RST%    %RED%Errores: !errCount!%RST%    %GRAY%Omitidos: !skipCount!%RST%
echo     %GRAY%Made by %BRAND%%BOLD%AXE%RST%
echo   %LINE%------------------------------------------------------%RST%
echo.
<nul set /p "=  %GRAY%Cerrando ventana en%RST% "
for %%C in (3 2 1) do (
    <nul set /p "=%YELLOW%%%C%RST%.. "
    ping -n 2 127.0.0.1 >nul
)
exit
