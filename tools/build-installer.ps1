# Builds the Windows installer dist\installer\StillMark-<version>-setup.exe (requires Inno Setup 6).
#   powershell -ExecutionPolicy Bypass -File tools\build-installer.ps1 [-Iscc <path to ISCC.exe>]
# The program is published self-contained into dist\installer\stage (a normal folder, not a single file: the
# installer compresses it better and the program starts without unpacking). No portable.txt is included, so the
# installed program keeps its data in %LOCALAPPDATA%\StillMark, and no test automation channel is compiled in.
param([string]$Iscc = "")
$ErrorActionPreference = "Stop"
$root = (Resolve-Path "$PSScriptRoot\..").Path

if (-not $Iscc) {
    $Iscc = @("$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe", "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "$env:ProgramFiles\Inno Setup 6\ISCC.exe") |
        Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
}
if (-not $Iscc -or -not (Test-Path $Iscc)) { throw "Inno Setup 6 (ISCC.exe) not found. Install it (e.g. winget install JRSoftware.InnoSetup) or pass -Iscc." }

$version = ([xml](Get-Content "$root\src\StillMark\StillMark.csproj" -Raw)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
$stage = Join-Path $root "dist\installer\stage"
if (Test-Path $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }

dotnet publish "$root\src\StillMark\StillMark.csproj" -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=false -p:DebugType=none -p:StillMarkAutomation=false -o $stage -nologo
if ($LASTEXITCODE -ne 0) { throw "publish failed" }
if (Test-Path (Join-Path $stage "portable.txt")) { throw "portable.txt must not be in the installer's files" }

& $Iscc /Qp "/DSourceDir=$stage" "/DAppVersion=$version" "$root\installer\StillMark.iss"
if ($LASTEXITCODE -ne 0) { throw "ISCC failed" }
Get-Item "$root\dist\installer\StillMark-$version-setup.exe" | ForEach-Object { "{0}  {1:N1} MB" -f $_.FullName, ($_.Length / 1MB) }
