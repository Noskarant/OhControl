param(
    [switch]$Rebuild
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root "src\OhControl\OhControl.csproj"

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

function Find-MsBuild {
    $vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"

    if (Test-Path $vswhere) {
        $found = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -find MSBuild\**\Bin\MSBuild.exe |
            Select-Object -First 1

        if ($found -and (Test-Path $found)) {
            return $found
        }
    }

    $known = @(
        "${env:ProgramFiles}\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe",
        "${env:ProgramFiles}\Microsoft Visual Studio\2022\Professional\MSBuild\Current\Bin\MSBuild.exe",
        "${env:ProgramFiles}\Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\MSBuild.exe",
        "${env:ProgramFiles(x86)}\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe"
    )

    return $known | Where-Object { Test-Path $_ } | Select-Object -First 1
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

if ($Rebuild -or -not $exe) {
    Write-Host "Preparation d'OhControl..." -ForegroundColor Cyan

    $msbuild = Find-MsBuild

    if ($msbuild) {
        & $msbuild $project /restore /m /p:Configuration=Release /p:Platform=x64 /p:MSFS2024SdkPath="$sdk" /verbosity:minimal

        if ($LASTEXITCODE -ne 0) {
            throw "La compilation OhControl a echoue."
        }
    }
    elseif (Get-Command dotnet -ErrorAction SilentlyContinue) {
        & dotnet build $project -c Release -p:PlatformTarget=x64 -p:MSFS2024SdkPath="$sdk"

        if ($LASTEXITCODE -ne 0) {
            throw "La compilation OhControl a echoue. Installe Visual Studio Build Tools / Desktop .NET puis relance."
        }
    }
    else {
        Write-Host ""
        Write-Host "Outil de compilation introuvable." -ForegroundColor Yellow
        Write-Host "Installe Visual Studio 2022 ou Build Tools avec 'Desktop .NET', puis relance."
        Read-Host "Appuie sur Entree pour fermer"
        exit 3
    }

    $exe = $outputCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
}

if (-not $exe) {
    throw "OhControl.exe n'a pas ete trouve apres compilation."
}

Write-Host "Lancement..." -ForegroundColor Green
Start-Process -FilePath $exe -WorkingDirectory (Split-Path -Parent $exe)
