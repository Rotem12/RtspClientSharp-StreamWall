$ErrorActionPreference = 'Stop'

$root = $PSScriptRoot
$project = Join-Path $root 'src\RtspClientSharp.Web\RtspClientSharp.Web.csproj'
$artifacts = Join-Path $root 'artifacts'
$publishDirectory = Join-Path $artifacts 'portable-win-x64'
$archive = Join-Path $artifacts 'StreamWall-win-x64.zip'
$fullArtifacts = [System.IO.Path]::GetFullPath($artifacts).TrimEnd('\')
$fullPublish = [System.IO.Path]::GetFullPath($publishDirectory).TrimEnd('\')

if (-not (Test-Path -LiteralPath $project -PathType Leaf)) { throw "Project not found: $project" }
New-Item -ItemType Directory -Force -Path $artifacts | Out-Null
if (-not $fullPublish.StartsWith("$fullArtifacts\", [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Refusing to clean a build output outside this repository artifacts folder.'
}
if (Test-Path -LiteralPath $publishDirectory) {
    Remove-Item -LiteralPath $publishDirectory -Recurse -Force
}
if (Test-Path -LiteralPath $archive -PathType Leaf) {
    Remove-Item -LiteralPath $archive -Force
}

Write-Host 'Publishing a self-contained Windows x64 build (the host PC does not need .NET installed)...'
dotnet publish $project --configuration Release --runtime win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true `
    --output $publishDirectory --verbosity minimal
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE." }

foreach ($file in @('README.md', 'Run-Local.bat', 'Run-Local.ps1', 'Run-Public.bat', 'Run-Public.ps1')) {
    Copy-Item -LiteralPath (Join-Path $root $file) -Destination $publishDirectory
}
Copy-Item -LiteralPath (Join-Path $root 'src\RtspClientSharp\LICENSE.md') `
    -Destination (Join-Path $publishDirectory 'THIRD-PARTY-RtspClientSharp-LICENSE.md')
Compress-Archive -Path (Join-Path $publishDirectory '*') -DestinationPath $archive -CompressionLevel Optimal

Write-Host ''
Write-Host "Portable folder: $publishDirectory"
Write-Host "Ready-to-copy package: $archive" -ForegroundColor Green
Write-Host 'Extract it to a writable folder on the host PC and double-click RtspClientSharp.Web.exe.'

