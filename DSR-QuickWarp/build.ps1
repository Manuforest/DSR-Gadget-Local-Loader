$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$propertyHookProject = Join-Path $repoRoot 'PropertyHook\PropertyHook\PropertyHook.csproj'

if (!(Test-Path $propertyHookProject)) {
    Write-Host 'Fetching PropertyHook...'
    git clone --depth 1 https://github.com/Meikk99/PropertyHook.git (Join-Path $repoRoot 'PropertyHook')
}

Push-Location $repoRoot
try {
    msbuild 'PropertyHook\PropertyHook\PropertyHook.csproj' /m /p:Configuration=Release /p:Platform=AnyCPU /p:TargetFrameworkVersion=v4.7.2
    msbuild 'DSR-QuickWarp\DSR-QuickWarp.csproj' /m /p:Configuration=Release /p:Platform=x64
}
finally {
    Pop-Location
}
