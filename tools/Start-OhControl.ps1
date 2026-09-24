param(
    [switch]$Rebuild
)

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root "src\OhControl\OhControl.csproj"
$toolsRoot = Join-Path $root ".ohcontrol-tools"
$localDotnetRoot = Join-Path $toolsRoot "dotnet"
$localDotnet = Join-Path $localDotnetRoot "dotnet.exe"
$dotnetInstallScript = Join-Path $toolsRoot "dotnet-install.ps1"

Write-Host ""
Write-Host "OhControl" -ForegroundColor Cyan
Write-Host "---------" -ForegroundColor DarkGray

function Find-MsfsSdk {
    $candidates = @(
        $env:MSFS2024_SDK,
        "$env:SystemDrive\MSFS 2024 SDK",
        "C:\MSFS 2024 SDK",
        "D:\MSFS 2024 SDK"
    ) | Where-Object { $_ -and $_.Trim() -ne "" } | Select-Object -Unique

    foreach ($candidate in $candidates) {
        $dll = Join-Path $candidate "SimConnect SDK\lib\managed\Microsoft.FlightSimulator.SimConnect.dll"
        if (Test-Path $dll) {
            return $candidate
        }
    }

    return $null
}

function Ensure-SimConnectBesideExe {
    param(
        [string]$Executable,
        [string]$SdkRoot
    )

    if (-not $Executable -or -not (Test-Path $Executable)) {
        return
    }

    $managedSource = Join-Path $SdkRoot "SimConnect SDK\lib\managed\Microsoft.FlightSimulator.SimConnect.dll"
    $nativeSource = Join-Path $SdkRoot "SimConnect SDK\lib\SimConnect.dll"
    $outputDir = Split-Path -Parent $Executable

    if (Test-Path $managedSource) {
        Copy-Item -Path $managedSource -Destination (Join-Path $outputDir "Microsoft.FlightSimulator.SimConnect.dll") -Force
    }

    if (Test-Path $nativeSource) {
        Copy-Item -Path $nativeSource -Destination (Join-Path $outputDir "SimConnect.dll") -Force
    }
}

function Get-UsableDotnet {
    if (Test-Path $localDotnet) {
        return $localDotnet
    }

    $systemDotnet = Get-Command dotnet -ErrorAction SilentlyContinue

    if ($systemDotnet) {
        try {
            $sdks = & $systemDotnet.Source --list-sdks 2>$null
            if ($LASTEXITCODE -eq 0 -and $sdks) {
                return $systemDotnet.Source
            }
        }
        catch {
        }
    }

    return $null
}

function Install-LocalDotnetSdk {
    New-Item -ItemType Directory -Force -Path $toolsRoot | Out-Null
    New-Item -ItemType Directory -Force -Path $localDotnetRoot | Out-Null

    Write-Host ""
    Write-Host "Installation automatique du composant .NET necessaire..." -ForegroundColor Cyan
    Write-Host "Une seule fois, uniquement dans le dossier OhControl." -ForegroundColor DarkGray

    if (-not (Test-Path $dotnetInstallScript)) {
        Invoke-WebRequest -Uri "https://dot.net/v1/dotnet-install.ps1" -OutFile $dotnetInstallScript -UseBasicParsing
    }

    $installOutput = & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $dotnetInstallScript -Channel "8.0" -InstallDir $localDotnetRoot -NoPath 2>&1
    $installExitCode = $LASTEXITCODE

    if ($installOutput) {
        $installOutput | ForEach-Object { Write-Host $_ }
    }

    if ($installExitCode -ne 0 -or -not (Test-Path $localDotnet)) {
        throw "Impossible d'installer automatiquement le SDK .NET local."
    }

    return $localDotnet
}

$sdk = Find-MsfsSdk

if (-not $sdk) {
    Write-Host ""
    Write-Host "SimConnect MSFS 2024 n'est pas encore installe." -ForegroundColor Yellow
    Write-Host ""
    Write-Host "Une seule chose a faire :"
    Write-Host "  1. Ouvre Microsoft Flight Simulator 2024"
    Write-Host "  2. Active Developer Mode"
    Write-Host "  3. Barre Developer -> Help -> SDK Installer"
    Write-Host "  4. Installe le SDK, puis relance ce fichier"
    Write-Host ""
    Read-Host "Appuie sur Entree pour fermer"
    exit 2
}

$env:MSFS2024_SDK = $sdk
Write-Host "SimConnect : OK" -ForegroundColor Green

$outputCandidates = @(
    (Join-Path $root "src\OhControl\bin\Release\net48\OhControl.exe"),
    (Join-Path $root "src\OhControl\bin\x64\Release\net48\OhControl.exe"),
    (Join-Path $root "src\OhControl\bin\Release\OhControl.exe")
)

$exe = $outputCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1

$sourceChanged = $false

if ($exe) {
    $exeTime = (Get-Item $exe).LastWriteTimeUtc

    $newerSource = Get-ChildItem -Path (Join-Path $root "src\OhControl") -Recurse -File |
        Where-Object {
            ($_.Extension -eq ".cs" -or $_.Extension -eq ".csproj") -and
            $_.LastWriteTimeUtc -gt $exeTime
        } |
        Select-Object -First 1

    $sourceChanged = $null -ne $newerSource
}

if ($Rebuild -or -not $exe -or $sourceChanged) {
    Write-Host "Preparation d'OhControl..." -ForegroundColor Cyan

    $dotnet = Get-UsableDotnet

    if (-not $dotnet) {
        $dotnet = Install-LocalDotnetSdk
    }

    Write-Host ".NET SDK : OK" -ForegroundColor Green

    & $dotnet build $project --configuration Release --nologo -p:PlatformTarget=x64 -p:MSFS2024SdkPath="$sdk"

    if ($LASTEXITCODE -ne 0) {
        Write-Host ""
        Write-Host "La compilation OhControl a echoue." -ForegroundColor Red
        Write-Host "Fais une capture de cette fenetre et envoie-la dans le chat." -ForegroundColor Yellow
        Read-Host "Appuie sur Entree pour fermer"
        exit 4
    }

    $exe = $outputCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
}

if (-not $exe) {
    Write-Host ""
    Write-Host "OhControl.exe n'a pas ete trouve apres compilation." -ForegroundColor Red
    Read-Host "Appuie sur Entree pour fermer"
    exit 5
}

Ensure-SimConnectBesideExe -Executable $exe -SdkRoot $sdk

Write-Host "SimConnect local : OK" -ForegroundColor Green
Write-Host "OhControl : pret" -ForegroundColor Green
Write-Host "Lancement..." -ForegroundColor Green

Start-Process -FilePath $exe -WorkingDirectory (Split-Path -Parent $exe)
