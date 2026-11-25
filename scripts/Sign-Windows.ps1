param(
    [Parameter(Mandatory = $true)][string]$Executable,
    [Parameter(Mandatory = $true)][string]$CertificateThumbprint
)
$ErrorActionPreference = 'Stop'
$SinhCertificate = Get-Item -LiteralPath ('Cert:\CurrentUser\My\' + $CertificateThumbprint)
if (-not $SinhCertificate.HasPrivateKey) { throw 'The selected certificate has no private key.' }
if ([DateTime]::Now -lt $SinhCertificate.NotBefore -or [DateTime]::Now -gt $SinhCertificate.NotAfter) { throw 'The selected certificate is outside its validity period.' }
if (-not ($SinhCertificate.EnhancedKeyUsageList.ObjectId.Value -contains '1.3.6.1.5.5.7.3.3')) { throw 'A code-signing certificate is required.' }
$SinhSignature = Set-AuthenticodeSignature -FilePath $Executable -Certificate $SinhCertificate -HashAlgorithm SHA256
$SinhSignature | Format-List Status, StatusMessage, SignerCertificate
if ($SinhSignature.Status -ne 'Valid') { throw 'Signature is not trusted/valid on this computer. No trust store was modified.' }
Get-FileHash -LiteralPath $Executable -Algorithm SHA256
