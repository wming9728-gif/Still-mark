# Re-downloads the offline web assets bundled in src\StillMark\web\vendor (only needed to update them).
# highlight.js (BSD-3-Clause) and JetBrains Mono (OFL-1.1) come from the public npm registry.
$ErrorActionPreference = "Stop"
$root = (Resolve-Path "$PSScriptRoot\..").Path
$cache = Join-Path $root "tools\vendor-cache"
$web = Join-Path $root "src\StillMark\web\vendor"
New-Item -ItemType Directory -Force $cache, "$web\hljs", "$web\fonts" | Out-Null

function Get-Package([string]$name, [string]$version) {
    $meta = Invoke-RestMethod "https://registry.npmjs.org/$name/$version"
    $dir = Join-Path $cache (($name -replace '[@/]', '_') + "-" + $meta.version)
    if (-not (Test-Path $dir)) {
        New-Item -ItemType Directory -Force $dir | Out-Null
        Invoke-WebRequest -UseBasicParsing $meta.dist.tarball -OutFile "$dir.tgz"
        tar -xzf "$dir.tgz" -C $dir
    }
    return Join-Path $dir "package"
}

$hl = Get-Package "@highlightjs/cdn-assets" "11.12.0"
Copy-Item "$hl\highlight.min.js", "$hl\LICENSE" "$web\hljs\" -Force
foreach ($l in "powershell", "dos", "cmake", "dockerfile", "nginx") { Copy-Item "$hl\languages\$l.min.js" "$web\hljs\" -Force }

$jb = Get-Package "@fontsource/jetbrains-mono" "5.3.0"
Copy-Item "$jb\files\jetbrains-mono-latin-400-normal.woff2", "$jb\files\jetbrains-mono-latin-500-normal.woff2" "$web\fonts\" -Force
Copy-Item "$jb\LICENSE" "$web\fonts\OFL-JetBrainsMono.txt" -Force
"vendor assets updated in $web"
