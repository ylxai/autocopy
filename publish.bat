@echo off
echo ========================================
echo   AutoCopy - Publish Release
echo ========================================
echo.

echo Publishing self-contained executable...
dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true

if %errorlevel% neq 0 (
    echo ERROR: Publish failed
    pause
    exit /b 1
)

echo.
echo ========================================
echo   Publish Successful!
echo ========================================
echo.
echo Output: bin\Release\net6.0-windows\win-x64\publish\
echo.
echo You can now distribute the AutoCopy.exe file.
echo.
pause
