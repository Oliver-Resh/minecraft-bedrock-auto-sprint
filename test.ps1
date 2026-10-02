$ErrorActionPreference = 'Stop'
if (Get-Process -Name BedrockAutoSprint -ErrorAction SilentlyContinue) {
    throw 'Exit the running Bedrock Auto Sprint helper before testing.'
}
& (Join-Path $PSScriptRoot 'build.ps1')
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$testDirectory = Join-Path $PSScriptRoot 'test-output'
New-Item -ItemType Directory -Force -Path $testDirectory | Out-Null
$testExe = Join-Path $testDirectory 'AuditTests.exe'
$source = Join-Path $PSScriptRoot 'BedrockAutoSprint.cs'
$tests = Join-Path $PSScriptRoot 'AuditTests.cs'
$icon = Join-Path $PSScriptRoot 'BedrockAutoSprint.ico'
& $compiler /nologo /target:exe /main:AuditTests /platform:x64 /r:System.Windows.Forms.dll /r:System.Drawing.dll "/win32icon:$icon" "/out:$testExe" $source $tests
if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed.' }
& $testExe (Join-Path $PSScriptRoot 'BedrockAutoSprint.exe')
if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
Write-Output 'All tests passed.'
