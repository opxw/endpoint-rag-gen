[CmdletBinding()]
param([string]$ReleaseName = "text-rag-gen-win-x64")

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$release = Join-Path $root 'release'
$output = Join-Path $release $ReleaseName

dotnet run --project (Join-Path $root 'tests\TextRag.Verification\TextRag.Verification.csproj') -c Release
if ($LASTEXITCODE -ne 0) { throw 'Verification failed.' }
dotnet publish (Join-Path $root 'src\TextRag.WinForms\TextRag.WinForms.csproj') -c Release -r win-x64 --self-contained true -o $output -p:PublishSingleFile=true -p:DebugType=None -p:DebugSymbols=false
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }

$pdb = @(Get-ChildItem -LiteralPath $output -Recurse -Filter '*.pdb')
if ($pdb.Count -ne 0) { throw "Release contains $($pdb.Count) PDB file(s)." }

$zip = "$output.zip"
if (Test-Path -LiteralPath $zip) { Remove-Item -LiteralPath $zip -Force }
# Only ship application artifacts, never user projects or generated package/ data.
$artifacts = @((Join-Path $output 'TextRagGen.exe'), (Join-Path $output 'THIRD-PARTY-NOTICES.txt'))
Compress-Archive -LiteralPath $artifacts -DestinationPath $zip -CompressionLevel Optimal
Get-FileHash -Algorithm SHA256 -LiteralPath $zip | Format-List
