# Builds the Astra Finance release: tests → self-contained publish → Inno Setup installer.
# Usage (from the repository root):  powershell -ExecutionPolicy Bypass -File installer\build-release.ps1
# Requires: .NET 10 SDK and Inno Setup 6 (ISCC.exe; per-user install in %LOCALAPPDATA%\Programs\Inno Setup 6 is found automatically).
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

Write-Host '1/4 Running tests...'
dotnet test AstraFinance.sln -c Release -nologo -v q
if ($LASTEXITCODE -ne 0) { throw 'Tests failed; no release was built.' }

Write-Host '2/4 Publishing (self-contained, win-x64)...'
if (Test-Path 'artifacts\publish') { Remove-Item 'artifacts\publish' -Recurse -Force }
dotnet publish src\AstraFinance.App\AstraFinance.App.csproj -c Release -r win-x64 --self-contained true -o artifacts\publish -nologo -v q
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }

Write-Host '3/4 Locating Inno Setup...'
$iscc = @(
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw 'Inno Setup 6 (ISCC.exe) was not found.' }

Write-Host '4/4 Building the installer...'
& $iscc /Q installer\AstraFinance.iss
if ($LASTEXITCODE -ne 0) { throw 'Installer build failed.' }

$setup = Get-ChildItem artifacts\installer\AstraFinance-Setup-*.exe | Sort-Object LastWriteTime -Descending | Select-Object -First 1
$hash = (Get-FileHash $setup.FullName -Algorithm SHA256).Hash
Write-Host ("Done: {0} ({1:N1} MB)  SHA-256 {2}" -f $setup.FullName, ($setup.Length / 1MB), $hash)
