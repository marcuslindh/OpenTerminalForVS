<#
.SYNOPSIS
    Publicerar OpenTerminalForVS som en fristående NativeAOT-exe.

.DESCRIPTION
    Kör 'dotnet publish' med PublishAot och lägger resultatet i .\publish\.
    Resultatet är en enda .exe utan beroende på installerad .NET-runtime.

    Länkningssteget i ILCompiler anropar vswhere.exe för att hitta MSVC-länkaren.
    Ligger inte vswhere.exe på PATH kraschar publiceringen med MSB3073
    ("'vswhere.exe' is not recognized"), så skriptet lägger till standardsökvägen
    till PATH för den här processen innan publiceringen startar.

.PARAMETER Runtime
    Runtime identifier, t.ex. win-x64 (standard) eller win-arm64.

.PARAMETER OutputDir
    Katalog som exe-filen hamnar i. Standard: .\publish

.PARAMETER Run
    Starta den publicerade exe-filen när publiceringen är klar.

.EXAMPLE
    .\publish.ps1

.EXAMPLE
    .\publish.ps1 -Runtime win-arm64 -Run
#>
[CmdletBinding()]
param(
    [string]$Runtime = 'win-x64',
    [string]$OutputDir = 'publish',
    [switch]$Run
)

$ErrorActionPreference = 'Stop'

$projectDir = $PSScriptRoot
$project = Join-Path $projectDir 'OpenTerminalForVS.csproj'
$outputPath = if ([System.IO.Path]::IsPathRooted($OutputDir)) { $OutputDir } else { Join-Path $projectDir $OutputDir }

if (-not (Test-Path $project)) {
    throw "Hittar inte projektfilen: $project"
}

# --- förutsättningar --------------------------------------------------------

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw 'dotnet hittades inte på PATH. Installera .NET 10 SDK.'
}

# ILCompiler anropar vswhere.exe via cmd; se till att den går att hitta
if (-not (Get-Command vswhere.exe -ErrorAction SilentlyContinue)) {
    $installerDir = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer'
    if (Test-Path (Join-Path $installerDir 'vswhere.exe')) {
        $env:PATH = "$installerDir;$env:PATH"
        Write-Host "vswhere.exe lades till på PATH: $installerDir" -ForegroundColor DarkGray
    }
    else {
        Write-Warning 'vswhere.exe hittades inte. Saknas MSVC-länkaren (C++ build tools) misslyckas länkningen med MSB3073.'
    }
}

# --- publicera --------------------------------------------------------------

Write-Host "Publicerar NativeAOT ($Runtime) till $outputPath ..." -ForegroundColor Cyan

if (Test-Path $outputPath) {
    Remove-Item -Path (Join-Path $outputPath '*') -Recurse -Force -Confirm:$false
}

& dotnet publish $project `
    --configuration Release `
    --runtime $Runtime `
    --output $outputPath `
    -p:PublishAot=true `
    --nologo

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish misslyckades med felkod $LASTEXITCODE."
}

$exe = Join-Path $outputPath 'OpenTerminalForVS.exe'
if (-not (Test-Path $exe)) {
    throw "Publiceringen gav ingen exe-fil på $exe."
}

$sizeMb = [math]::Round((Get-Item $exe).Length / 1MB, 1)
Write-Host ''
Write-Host "Klart: $exe ($sizeMb MB)" -ForegroundColor Green

if ($Run) {
    Write-Host 'Startar...' -ForegroundColor Cyan
    & $exe
}
