$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$app = Join-Path $root 'artifacts/app-light'
dotnet publish (Join-Path $root 'CaptureZoo.csproj') -c Release -r win-x64 --self-contained false -p:RuntimeFrameworkVersion=8.0.32 -p:DebugType=None -p:DebugSymbols=false -o $app
if ($LASTEXITCODE -ne 0) { throw 'Application build failed' }
Copy-Item (Join-Path $root 'README.md') $app
Copy-Item (Join-Path $root 'LICENSE') $app
Copy-Item (Join-Path $root 'THIRD-PARTY-NOTICES.md') $app
Copy-Item (Join-Path $root 'licenses') $app -Recurse -Force
$compiler = @("$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe", "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe") | Where-Object { Test-Path $_ } | Select-Object -First 1
if (!$compiler) { throw 'Install Inno Setup 6 first' }
& $compiler (Join-Path $root 'installer/CaptureZoo.iss')
if ($LASTEXITCODE -ne 0) { throw 'Installer build failed' }
