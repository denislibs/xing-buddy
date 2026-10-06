# Builds xing-pixel.exe with the C# compiler that ships with .NET Framework 4.x (nothing to install),
# generates the example character plugins (mini pig, spark) and exports the sprites for the macOS build.
$ErrorActionPreference = 'Stop'
$Dir = Split-Path -Parent $MyInvocation.MyCommand.Path
$fw = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319"
$wpf = "$fw\WPF"
$refs = @("$wpf\PresentationFramework.dll", "$wpf\PresentationCore.dll", "$wpf\WindowsBase.dll", "$fw\System.Xaml.dll", "$fw\System.Web.Extensions.dll",
          "$fw\System.Windows.Forms.dll", "$fw\System.Drawing.dll", "$fw\Microsoft.CSharp.dll", "$fw\System.Core.dll") | ForEach-Object { "/r:$_" }
$src = "Sprites", "Brain", "Plugins", "Extras", "Game", "App" | ForEach-Object { "$Dir\src\$_.cs" }
& "$fw\csc.exe" /nologo /target:winexe /optimize+ /nowarn:649,169,414 "/out:$Dir\xing-pixel.exe" @refs @src
if ($LASTEXITCODE -ne 0) { throw "build failed" }
Write-Host "Built $Dir\xing-pixel.exe"

function Run($a) { $p = Start-Process "$Dir\xing-pixel.exe" -ArgumentList $a -Wait -PassThru; if ($p.ExitCode -ne 0) { throw "xing-pixel $a failed" } }
function Fresh($path) { if ([IO.Directory]::Exists($path)) { [IO.Directory]::Delete($path, $true) } }

foreach ($c in 'pig', 'spark') { Run @('--export-character', $c, "`"$Dir\characters\$c`"") }
$mac = Join-Path (Split-Path -Parent $Dir) 'mac'
Fresh "$mac\sprites"; Run @('--export', "`"$mac\sprites`"")
Fresh "$mac\characters"; Copy-Item -Recurse "$Dir\characters" "$mac\characters"
Write-Host "Characters: $((Get-ChildItem "$Dir\characters" -Directory).Name -join ', '); mac sprites exported"
