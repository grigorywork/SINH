$ErrorActionPreference = 'Stop'
$SinhRoot = Split-Path -Parent $PSScriptRoot
Push-Location $SinhRoot
try {
    $SinhReports = Join-Path $SinhRoot 'artifacts/windows-validation'
    New-Item -ItemType Directory -Force $SinhReports | Out-Null
    $SinhOs = Get-CimInstance Win32_OperatingSystem
    @{ OperatingSystem = $SinhOs.Caption; Version = $SinhOs.Version; Build = $SinhOs.BuildNumber; Architecture = $SinhOs.OSArchitecture; At = [DateTime]::UtcNow.ToString('o') } | ConvertTo-Json | Set-Content (Join-Path $SinhReports 'environment.json') -Encoding utf8
    function Invoke-SinhDotnet([string]$Name, [string[]]$Arguments) {
        & dotnet @Arguments 2>&1 | Tee-Object -FilePath (Join-Path $SinhReports ($Name + '.log'))
        if ($LASTEXITCODE -ne 0) { throw "$Name failed with exit code $LASTEXITCODE" }
    }
    Invoke-SinhDotnet 'restore' @('restore', 'SINH.sln', '--locked-mode')
    Invoke-SinhDotnet 'build' @('build', 'SINH.sln', '-c', 'Release', '--no-restore', '--nologo', '-warnaserror', '-m:1', '-nr:false')
    Invoke-SinhDotnet 'engine' @('run', '--no-build', '--project', 'tests/Sinh.Checks', '-c', 'Release')
    Invoke-SinhDotnet 'wpf' @('run', '--no-build', '--project', 'tests/Sinh.WindowsChecks', '-c', 'Release')
    Write-Host 'Automated Windows checks passed. Physical USB/SMB, client OS and DPI checks remain separate; see docs/WINDOWS-VALIDATION.md.'
} finally { Pop-Location }
