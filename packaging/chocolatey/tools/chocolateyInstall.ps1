$ErrorActionPreference = 'Stop'

$toolsDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
$packageName = 'redplusplus.portable'
$url = 'https://github.com/SysAdminDoc/REDplusplus/releases/download/v1.5.17/RED++_v1.5.17.zip'
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
