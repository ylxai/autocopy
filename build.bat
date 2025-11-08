@echo off
echo ========================================
echo   AutoCopy - Build Script
echo ========================================
echo.

echo [1/3] Restoring dependencies...
dotnet restore
if %errorlevel% neq 0 (
    echo ERROR: Failed to restore dependencies
    pause
    exit /b 1
)

echo.
echo [2/3] Building project...
dotnet build -c Release
if %errorlevel% neq 0 (
    echo ERROR: Build failed
    pause
    exit /b 1
)

echo.
echo [3/3] Build successful!
echo.
echo Output: bin\Release\net6.0-windows\
echo.
pause
