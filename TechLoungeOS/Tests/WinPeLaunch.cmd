@echo off
if /i not "%SystemDrive%"=="X:" exit /b 1
if not exist X:\Windows\System32\wpeutil.exe exit /b 1
setlocal EnableExtensions EnableDelayedExpansion
echo TL_BOOT_START
wpeinit
set TARGET=
for %%D in (C D E F G H I J K L M N O P Q R S T U V W Y Z) do (
  if exist %%D:\TL-VALIDATION-ID.txt (
    echo Marker found on %%D:
    set FOUND=
    set /p FOUND=<"%%D:\TL-VALIDATION-ID.txt"
    if /i "!FOUND!"=="899aaed3-b0d4-421e-9772-8678e1085ea2" set TARGET=%%D:
  )
)
if not defined TARGET goto fail
echo TL_VOLUME_FOUND_%TARGET%
set DOTNET_EnableDiagnostics=0
set MODE=--pe-service
if exist "%TARGET%\TL-APPLIED-IMAGE.txt" set MODE=--pe-applied-service
if exist "%TARGET%\TL-VERIFY-COMPLETED.txt" set MODE=--pe-verify-completed
"%TARGET%\worker\Tests.exe" %MODE% "%TARGET%\config.json" "%TARGET%\."
set RESULT=%ERRORLEVEL%
copy /y X:\Windows\Logs\DISM\dism.log "%TARGET%\winpe-dism.log" >nul
echo TL_WORKER_EXIT_%RESULT%
>"%TARGET%\TL-RESULT.txt" echo %RESULT%

wpeutil shutdown
exit /b
:workerfail
type "%TARGET%\worker-console.log"
cmd.exe /k
exit /b
:fail
echo TL_NO_MATCHING_DISK_NO_CHANGES
echo list volume>X:\list-volumes.txt
diskpart /s X:\list-volumes.txt
cmd.exe /k
