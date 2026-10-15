# Lists the signature of every program file in the packages: dist\StillMark (portable), dist\installer\stage
# (installed files) and the installer itself. Run it after tools\publish.ps1 and tools\build-installer.ps1.
# With -RequireSigned (signed builds) it fails unless all of them carry a valid signature.
#   powershell -ExecutionPolicy Bypass -File tools\check-signatures.ps1 [-RequireSigned]
param([switch]$RequireSigned)
$ErrorActionPreference = "Stop"
$root = (Resolve-Path "$PSScriptRoot\..").Path
$version = ([xml](Get-Content "$root\src\StillMark\StillMark.csproj" -Raw)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1

$files = @(Get-ChildItem "$root\dist\StillMark", "$root\dist\installer\stage" -Recurse -File -Include *.exe, *.dll |
    Where-Object { -not $_.FullName.StartsWith("$root\dist\StillMark\data\") })
$files += Get-Item "$root\dist\installer\StillMark-$version-setup.exe"
$notValid = 0
foreach ($f in $files) {
    $sig = Get-AuthenticodeSignature $f.FullName
    $signer = if ($sig.SignerCertificate) { $sig.SignerCertificate.GetNameInfo([Security.Cryptography.X509Certificates.X509NameType]::SimpleName, $false) } else { "-" }
    "{0,-12}  {1,-32}  {2}" -f $sig.Status, $signer, $f.FullName.Substring($root.Length + 1)
    if ($sig.Status -ne "Valid") { $notValid++ }
}
if ($RequireSigned -and $notValid) { throw "$notValid file(s) are not validly signed" }
