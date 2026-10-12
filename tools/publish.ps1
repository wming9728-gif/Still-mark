# Builds the portable Windows release into dist\InkPage (self-contained, no .NET install needed).
#   powershell -ExecutionPolicy Bypass -File tools\publish.ps1
# The official package does not contain the test automation channel. For GUI testing of a release build:
#   powershell -ExecutionPolicy Bypass -File tools\publish.ps1 -WithAutomation     (-> dist\InkPage-test)
param([switch]$WithAutomation)
$ErrorActionPreference = "Stop"
$root = (Resolve-Path "$PSScriptRoot\..").Path
$name = if ($WithAutomation) { "InkPage-test" } else { "InkPage" }
$out = Join-Path $root "dist\$name"
# Never kill a running copy: it may hold the user's unsaved work. Ask for it to be closed instead.
$running = @(Get-Process InkPage -ErrorAction SilentlyContinue | Where-Object { $_.Path -like "$out\*" })
if ($running.Count) { throw "dist\$name\InkPage.exe is running (pid $($running.Id -join ',')). Close it first, then publish again." }
# Clear the old build but keep data\ (the user's settings, recent files and crash-recovery drafts).
if (Test-Path $out) { Get-ChildItem -LiteralPath $out -Force | Where-Object Name -ne "data" | Remove-Item -Recurse -Force }
dotnet publish "$root\src\InkPage\InkPage.csproj" -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true `
    -p:DebugType=none "-p:InkPageAutomation=$(if ($WithAutomation) { 'true' } else { 'false' })" -o $out -nologo
if ($LASTEXITCODE -ne 0) { throw "publish failed" }
# Portable package: the marker keeps settings and drafts in data\ next to the exe (see AppPaths).
Copy-Item "$root\src\InkPage\portable.txt" $out -Force
Get-ChildItem $out -Recurse -File | Where-Object { -not $_.FullName.StartsWith("$out\data\") } | ForEach-Object { "{0,12:N0}  {1}" -f $_.Length, $_.FullName.Substring($out.Length + 1) }
