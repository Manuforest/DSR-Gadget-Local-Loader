$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $root

if (!(Test-Path 'PropertyHook\PropertyHook\PropertyHook.csproj')) {
    git clone https://github.com/Meikk99/PropertyHook.git PropertyHook
}

New-Item -ItemType Directory -Force -Path third_party | Out-Null
if (!(Test-Path 'third_party\imgui\imgui.cpp')) {
    git clone https://github.com/ocornut/imgui.git third_party\imgui
}
git -C third_party\imgui fetch origin 46d39d56febc2a00bdd2270dc88c8a13f2a0441a --depth 1
git -C third_party\imgui checkout 46d39d56febc2a00bdd2270dc88c8a13f2a0441a

if (!(Test-Path 'third_party\minhook\src\hook.c')) {
    git clone https://github.com/TsudaKageyu/minhook.git third_party\minhook
}
git -C third_party\minhook fetch origin d94c64d32ea37bc4f5ee47d580709f70c6fb6080 --depth 1
git -C third_party\minhook checkout d94c64d32ea37bc4f5ee47d580709f70c6fb6080

msbuild PropertyHook\PropertyHook\PropertyHook.csproj /m /p:Configuration=Release /p:Platform=AnyCPU /p:TargetFrameworkVersion=v4.7.2
msbuild DSR-QuickWarp\DSR-QuickWarp.csproj /m /p:Configuration=Release /p:Platform=x64
msbuild DSR-QuickWarp-Native\DSR-QuickWarp-Native.vcxproj /m /p:Configuration=Release /p:Platform=x64

Write-Host 'Built:'
Write-Host '  DSR-QuickWarp\bin\x64\Release\DSR QuickWarp.exe'
Write-Host '  DSR-QuickWarp\bin\x64\Release\PropertyHook.dll'
Write-Host '  DSR-QuickWarp-Native\bin\x64\Release\DSR QuickWarp Overlay.dll'
