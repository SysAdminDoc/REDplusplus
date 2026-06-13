$ErrorActionPreference = 'Stop'

$toolsDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
$packageName = 'redplusplus.portable'
$url = 'https://github.com/SysAdminDoc/REDplusplus/releases/download/v1.5.12/RED++_v1.5.12.zip'
$checksum = ''
$checksumType = 'sha256'

$packageArgs = @{
    packageName    = $packageName
    unzipLocation  = $toolsDir
    url            = $url
    checksum       = $checksum
    checksumType   = $checksumType
}

Install-ChocolateyZipPackage @packageArgs
