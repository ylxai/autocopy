Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  AutoCopy - Automated Test Script" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Setup test environment
Write-Host "[1/5] Creating test folders..." -ForegroundColor Yellow
$testRoot = "test_autocopy"
if (Test-Path $testRoot) {
    Remove-Item $testRoot -Recurse -Force
}

New-Item -ItemType Directory -Path "$testRoot\source\subfolder1" -Force | Out-Null
New-Item -ItemType Directory -Path "$testRoot\source\subfolder2" -Force | Out-Null
New-Item -ItemType Directory -Path "$testRoot\destination" -Force | Out-Null

Write-Host "[2/5] Creating test files..." -ForegroundColor Yellow
"test content" | Out-File "$testRoot\source\photo1.jpg"
"test content" | Out-File "$testRoot\source\photo2.jpg"
"test content" | Out-File "$testRoot\source\document.pdf"
"test content" | Out-File "$testRoot\source\image.png"
"test content" | Out-File "$testRoot\source\subfolder1\photo3.jpg"
"test content" | Out-File "$testRoot\source\subfolder1\video.mp4"
"test content" | Out-File "$testRoot\source\subfolder2\PHOTO4.JPG"
"test content" | Out-File "$testRoot\source\subfolder2\Report.docx"

Write-Host "[3/5] Creating file list..." -ForegroundColor Yellow
@"
photo1.jpg
photo2.jpg
photo3.jpg
photo4.jpg
document.pdf
image.png
video.mp4
Report.docx
notfound.txt
missing_file.jpg
"@ | Out-File "$testRoot\test_list.txt"

Write-Host "[4/5] Test environment ready!" -ForegroundColor Green
Write-Host ""
Write-Host "Test folders created at: $testRoot" -ForegroundColor Green
Write-Host "  - Source: $testRoot\source (8 files in total)" -ForegroundColor White
Write-Host "  - Destination: $testRoot\destination (empty)" -ForegroundColor White
Write-Host "  - File List: $testRoot\test_list.txt (10 files)" -ForegroundColor White
Write-Host ""

Write-Host "[5/5] Opening test folder..." -ForegroundColor Yellow
Start-Process explorer.exe (Resolve-Path $testRoot).Path

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  Test Environment Ready!" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Next steps:" -ForegroundColor Yellow
Write-Host "  1. Run AutoCopy application" -ForegroundColor White
Write-Host "  2. Use folders in test_autocopy\" -ForegroundColor White
Write-Host "  3. Follow test cases in TEST_GUIDE.md" -ForegroundColor White
Write-Host ""
Write-Host "Press any key to continue..."
$null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
