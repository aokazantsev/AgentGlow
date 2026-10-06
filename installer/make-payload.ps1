param(
    [Parameter(Mandatory = $true)][string]$Root,
    [Parameter(Mandatory = $true)][string]$Output
)

$ErrorActionPreference = 'Stop'
$Root = (Resolve-Path -LiteralPath $Root).Path.TrimEnd([char]92)
$staging = Join-Path ([IO.Path]::GetTempPath()) ("ClaudeGlowPayload-" + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $staging | Out-Null

foreach ($name in @('ClaudeGlow.exe', 'ClaudeGlow.exe.config', 'Uninstall.exe', 'README.md', 'LICENSE')) {
    $source = Join-Path $Root $name
    if (-not (Test-Path -LiteralPath $source)) { throw "payload file missing: $name" }
    Copy-Item -LiteralPath $source -Destination $staging
}

New-Item -ItemType Directory -Force -Path (Split-Path $Output) | Out-Null
if (Test-Path $Output) { Remove-Item $Output -Force }
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::CreateFromDirectory($staging, $Output, [IO.Compression.CompressionLevel]::Optimal, $false)
Remove-Item $staging -Recurse -Force
"payload: {0:N1} KB" -f ((Get-Item $Output).Length / 1KB)
