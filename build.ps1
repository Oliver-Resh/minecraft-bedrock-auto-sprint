$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) {
    throw 'The x64 .NET Framework C# compiler was not found on this Windows installation.'
}
$target = Join-Path $PSScriptRoot 'BedrockAutoSprint.exe'
$source = Join-Path $PSScriptRoot 'BedrockAutoSprint.cs'
$icon = Join-Path $PSScriptRoot 'BedrockAutoSprint.ico'
& $compiler /nologo /target:winexe /platform:x64 /optimize+ /r:System.Windows.Forms.dll /r:System.Drawing.dll "/win32icon:$icon" "/out:$target" $source
if ($LASTEXITCODE -ne 0) { throw 'Compilation failed.' }
Write-Output "Built: $target"
