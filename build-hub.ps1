param([switch]$Tests)
$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$frameworkDir = Split-Path $compiler
$outDir = Join-Path $PSScriptRoot 'build\gear-hub'
New-Item -ItemType Directory -Path $outDir -Force | Out-Null
$target = Join-Path $outDir 'GM-GearHub.exe'
$argsList = @('/nologo','/optimize+','/platform:x64','/target:winexe',"/out:$target",'/codepage:65001',
    '/r:System.dll','/r:System.Core.dll','/r:System.Drawing.dll','/r:System.Windows.Forms.dll',
    "/r:$frameworkDir\WPF\PresentationFramework.dll","/r:$frameworkDir\WPF\PresentationCore.dll",
    "/r:$frameworkDir\WPF\WindowsBase.dll","/r:$frameworkDir\System.Xaml.dll",
    "/resource:$PSScriptRoot\hub\MainWindow.xaml,GearHub.MainWindow.xaml",
    "/resource:$PSScriptRoot\hub\resources\mouse-product.png,GearHub.MouseProduct",
    "/resource:$PSScriptRoot\hub\resources\gear.ico,GearHub.AppIcon",
    "/win32icon:$PSScriptRoot\hub\resources\gear.ico","/win32manifest:$PSScriptRoot\hub\app.manifest")
$sources = @(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'hub') -Filter '*.cs' | ForEach-Object {$_.FullName})
& $compiler @argsList @sources
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
if ($Tests) {
    $testTarget=Join-Path $outDir 'GearHub.Tests.exe'
    $testArgs = @($argsList | Where-Object { $_ -notlike '/out:*' -and $_ -notlike '/target:*' })
    $testArgs += @('/target:exe',"/out:$testTarget",'/main:GearHub.Tests')
    & $compiler @testArgs @sources (Join-Path $PSScriptRoot 'hub\tests\Tests.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Test build failed' }
}
Write-Output $target
