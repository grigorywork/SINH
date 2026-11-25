param([string]$SigningCertificateThumbprint = '')
$ErrorActionPreference = 'Stop'
$SinhRoot = Split-Path -Parent $PSScriptRoot
Push-Location $SinhRoot
try {
    [xml]$SinhProps = Get-Content 'Directory.Build.props' -Raw
    $SinhVersion = $SinhProps.Project.PropertyGroup.Version
    $SinhPublish = Join-Path $SinhRoot 'artifacts/publish'
    & dotnet restore SINH.sln --locked-mode
    if ($LASTEXITCODE -ne 0) { throw 'Restore failed.' }
    & dotnet publish src/Sinh.App/Sinh.App.csproj -c Release -r win-x64 --self-contained true --no-restore -o $SinhPublish -p:ContinuousIntegrationBuild=true
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
    if ($SigningCertificateThumbprint) { & "$PSScriptRoot/Sign-Windows.ps1" -Executable "$SinhPublish/SINH.exe" -CertificateThumbprint $SigningCertificateThumbprint }
    $SinhPackage = Join-Path $SinhRoot ("artifacts/SINH-$SinhVersion-win-x64-portable")
    New-Item -ItemType Directory -Force $SinhPackage | Out-Null
    Copy-Item "$SinhPublish/SINH.exe" $SinhPackage -Force
    if (Test-Path "$SinhPublish/Assets") { Copy-Item "$SinhPublish/Assets" $SinhPackage -Recurse -Force }
    Copy-Item README.md $SinhPackage -Force
    Copy-Item docs/WINDOWS-VALIDATION.md $SinhPackage -Force
    $SinhSigning = if ($SigningCertificateThumbprint) { 'Signed; verify trust in Windows file properties.' } else { 'UNSIGNED BUILD. No certificate was provided.' }
    $SinhSigning | Set-Content "$SinhPackage/SIGNATURE.txt" -Encoding utf8
    Get-FileHash "$SinhPackage/SINH.exe" -Algorithm SHA256 | Format-List | Out-String | Set-Content "$SinhPackage/SHA256.txt" -Encoding utf8
    Compress-Archive -Path "$SinhPackage/*" -DestinationPath ($SinhPackage + '.zip') -Force
    Get-FileHash ($SinhPackage + '.zip') -Algorithm SHA256
} finally { Pop-Location }
