# Builds the Windows installer dist\installer\StillMark-<version>-setup.exe (requires Inno Setup 6).
#   powershell -ExecutionPolicy Bypass -File tools\build-installer.ps1 [-Iscc <path to ISCC.exe>]
# The program is published self-contained into dist\installer\stage as a single StillMark.exe (uncompressed, so the
# installer compresses it well and the program starts without unpacking anything; the Microsoft runtime DLLs sit next
# to it). Every library is inside StillMark.exe, so signing that one file covers all the code we ship.
# No portable.txt is included, so the installed program keeps its data in %LOCALAPPDATA%\StillMark, and no test
# automation channel is compiled in.
#
# Signed builds (used by .github\workflows\build.yml) sign the uninstaller outside Inno Setup:
#   -SignedUninstallerDir <dir> -ExportUninstaller   publish the stage and write the unsigned uninstaller
#                                                    (<dir>\uninst-*.e32) for signing
#   -SignedUninstallerDir <dir> -NoPublish           after StillMark.exe in the stage and the .e32 file are signed,
#                                                    build the installer from them
param([string]$Iscc = "", [switch]$NoPublish, [string]$SignedUninstallerDir = "", [switch]$ExportUninstaller)
$ErrorActionPreference = "Stop"
$root = (Resolve-Path "$PSScriptRoot\..").Path
if ($ExportUninstaller -and -not $SignedUninstallerDir) { throw "-ExportUninstaller needs -SignedUninstallerDir" }

if (-not $Iscc) {
    $Iscc = @("$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe", "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "$env:ProgramFiles\Inno Setup 6\ISCC.exe") |
        Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
}
if (-not $Iscc -or -not (Test-Path $Iscc)) { throw "Inno Setup 6 (ISCC.exe) not found. Install it (e.g. winget install JRSoftware.InnoSetup) or pass -Iscc." }

$version = ([xml](Get-Content "$root\src\StillMark\StillMark.csproj" -Raw)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
$stage = Join-Path $root "dist\installer\stage"

if (-not $NoPublish) {
    if (Test-Path $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }
    dotnet publish "$root\src\StillMark\StillMark.csproj" -c Release -r win-x64 --self-contained true `
        -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=false -p:EnableCompressionInSingleFile=false `
        -p:DebugType=none -p:StillMarkAutomation=false -o $stage -nologo
    if ($LASTEXITCODE -ne 0) { throw "publish failed" }
}
if (-not (Test-Path (Join-Path $stage "StillMark.exe"))) { throw "dist\installer\stage\StillMark.exe is missing: run without -NoPublish first" }
if (Test-Path (Join-Path $stage "portable.txt")) { throw "portable.txt must not be in the installer's files" }

$isccArgs = @("/Qp", "/DSourceDir=$stage", "/DAppVersion=$version")
if ($SignedUninstallerDir) {
    $SignedUninstallerDir = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($SignedUninstallerDir)
    New-Item -ItemType Directory -Force $SignedUninstallerDir | Out-Null
    $isccArgs += "/DSignedUninstallerDir=$SignedUninstallerDir"
}

if ($ExportUninstaller) {
    # Without a signed uninstaller in the folder, Inno Setup writes an unsigned one there and stops on purpose
    # (its "please attach your digital signature" message below is expected).
    & $Iscc @isccArgs "$root\installer\StillMark.iss" | Out-Null
    $unsigned = @(Get-ChildItem $SignedUninstallerDir -Filter "uninst-*.e32" | Where-Object { (Get-AuthenticodeSignature $_.FullName).Status -eq "NotSigned" })
    if ($unsigned.Count -ne 1) { throw "expected one unsigned uninstaller in $SignedUninstallerDir, found $($unsigned.Count) (ISCC exit code $LASTEXITCODE)" }
    "Uninstaller to sign: $($unsigned[0].FullName)"
    return
}

& $Iscc @isccArgs "$root\installer\StillMark.iss"
if ($LASTEXITCODE -ne 0) { throw "ISCC failed" }
Get-Item "$root\dist\installer\StillMark-$version-setup.exe" | ForEach-Object { "{0}  {1:N1} MB" -f $_.FullName, ($_.Length / 1MB) }
