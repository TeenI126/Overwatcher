@echo off
echo Building OW Tracker...
echo.
echo NOTE: Single-file bundling is intentionally disabled.
echo Tesseract's native DLLs (x64\tesseract50.dll etc.) must sit next to
echo the exe — they cannot survive single-file packaging.
echo The publish folder is the distribution unit; zip it if you want to share it.
echo.

dotnet publish OwTracker.App/OwTracker.App.csproj ^
    --runtime win-x64 --self-contained true ^
    -p:PublishSingleFile=false ^
    -c Release -o publish/ -v q

if %errorlevel% neq 0 (
    echo.
    echo Build failed.
    pause
    exit /b 1
)
echo.
echo Done. Launch: publish\OwTracker.App.exe
echo.
explorer publish
