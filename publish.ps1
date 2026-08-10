<#
.SYNOPSIS
    Publicerar OpenTerminalForVS som en fristaende Native AOT-exe.

.DESCRIPTION
    Bygger med PublishAot och lagger resultatet i
    bin\Release\net10.0\publish\win-x64 - en enda exe utan .NET-runtimeberoende.

    Skriptet lagger till Visual Studio Installer-katalogen i processens PATH om
    den saknas. Utan den anropar vcvarsall.bat vswhere okvalificerat, och
    felutskriften pa stderr fangas av MSBuild och forstor sokvagen till link.exe
    (yttrar sig som MSB3073 med en trasig kommandorad).

.PARAMETER Clean
    Tar bort obj och bin innan publicering, sa hela ILC- och lankningssteget kors om.

.EXAMPLE
    .\publish.ps1
    .\publish.ps1 -Clean
#>
[CmdletBinding()]
param(
    [switch]$Clean
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$projectDir = $PSScriptRoot
$project    = Join-Path $projectDir 'OpenTerminalForVS.csproj'
$outputDir  = Join-Path $projectDir 'bin\Release\net10.0\publish\win-x64'

if (-not (Test-Path $project)) {
    throw "Hittar inte projektfilen: $project"
}

# vcvarsall.bat forutsatter att vswhere.exe gar att na via PATH
$installerDir = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer'
if (-not (Get-Command vswhere.exe -ErrorAction SilentlyContinue)) {
    if (Test-Path (Join-Path $installerDir 'vswhere.exe')) {
        $env:PATH = "$installerDir;$env:PATH"
        Write-Host "Added to PATH for this run: $installerDir" -ForegroundColor DarkGray
    }
    else {
        Write-Warning "vswhere.exe not found in $installerDir - the native link step will likely fail."
    }
}

if ($Clean) {
    Write-Host 'Cleaning obj and bin...' -ForegroundColor Cyan
    foreach ($dir in @('obj', 'bin')) {
        $path = Join-Path $projectDir $dir
        if (Test-Path $path) {
            Remove-Item -Recurse -Force $path
        }
    }
}

Write-Host "Publishing Native AOT to $outputDir" -ForegroundColor Cyan

dotnet publish $project -c Release -r win-x64 -o $outputDir

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish misslyckades med exitkod $LASTEXITCODE"
}

$exe = Join-Path $outputDir 'OpenTerminalForVS.exe'
if (-not (Test-Path $exe)) {
    throw "Publiceringen rapporterade lyckad men $exe saknas"
}

$sizeMb = [math]::Round((Get-Item $exe).Length / 1MB, 2)
Write-Host ''
Write-Host "Done: $exe ($sizeMb MB)" -ForegroundColor Green
