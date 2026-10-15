# Collects the release files into dist\release: StillMark-<version>-setup.exe, StillMark-<version>-portable.zip
# (the dist\StillMark folder as StillMark\..., without data\) and SHA256SUMS.txt.
# Run tools\publish.ps1 and tools\build-installer.ps1 first.
#   powershell -ExecutionPolicy Bypass -File tools\package-release.ps1
$ErrorActionPreference = "Stop"
$root = (Resolve-Path "$PSScriptRoot\..").Path
$version = ([xml](Get-Content "$root\src\StillMark\StillMark.csproj" -Raw)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
$portable = Join-Path $root "dist\StillMark"
$setup = Join-Path $root "dist\installer\StillMark-$version-setup.exe"
foreach ($p in @("$portable\StillMark.exe", "$portable\portable.txt", $setup)) { if (-not (Test-Path $p)) { throw "$p is missing" } }

$out = Join-Path $root "dist\release"
if (Test-Path $out) { Remove-Item -LiteralPath $out -Recurse -Force }
New-Item -ItemType Directory $out | Out-Null
Copy-Item $setup $out

# Entry names use "/" so every unzip tool sees the StillMark folder.
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
$zipPath = Join-Path $out "StillMark-$version-portable.zip"
$zip = [IO.Compression.ZipFile]::Open($zipPath, [IO.Compression.ZipArchiveMode]::Create)
try {
    Get-ChildItem $portable -Recurse -File | Where-Object { -not $_.FullName.StartsWith("$portable\data\") } | ForEach-Object {
        $name = "StillMark/" + $_.FullName.Substring($portable.Length + 1).Replace('\', '/')
        [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $_.FullName, $name, [IO.Compression.CompressionLevel]::Optimal)
    }
} finally { $zip.Dispose() }

$sums = foreach ($f in @("StillMark-$version-setup.exe", "StillMark-$version-portable.zip")) {
    "{0}  {1}" -f (Get-FileHash (Join-Path $out $f) -Algorithm SHA256).Hash.ToLowerInvariant(), $f
}
[IO.File]::WriteAllText((Join-Path $out "SHA256SUMS.txt"), ($sums -join "`n") + "`n")
Get-ChildItem $out | ForEach-Object { "{0,12:N0}  {1}" -f $_.Length, $_.Name }
