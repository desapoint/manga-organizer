param(
    [Parameter(Mandatory)] [string] $Version,
    [Parameter(Mandatory)] [string] $Payload,
    [Parameter(Mandatory)] [string] $OutputDir
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true
$compilerDir = Join-Path $env:RUNNER_TEMP 'inno-setup-6.7.3'
$compilerSetup = "$compilerDir.exe"
$compilerUrl = 'https://github.com/jrsoftware/issrc/releases/download/is-6_7_3/innosetup-6.7.3.exe'
$expectedHash = '9c73c3bae7ed48d44112a0f48e66742c00090bdb5bef71d9d3c056c66e97b732'
Invoke-WebRequest -Uri $compilerUrl -OutFile $compilerSetup
if ((Get-FileHash -LiteralPath $compilerSetup -Algorithm SHA256).Hash -ne $expectedHash) {
    throw 'The Inno Setup compiler checksum did not match.'
}

function Run-Setup([string] $Path, [string[]] $Flags) {
    $process = Start-Process -FilePath $Path -ArgumentList $Flags -Wait -PassThru
    if ($process.ExitCode -ne 0) {
        throw "Setup exited with code $($process.ExitCode)."
    }
}

$silent = @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART')
Run-Setup $compilerSetup ($silent + '/CURRENTUSER' + "/DIR=`"$compilerDir`"")
$payloadPath = (Resolve-Path -LiteralPath $Payload).Path
$outputPath = (Resolve-Path -LiteralPath $OutputDir).Path
& "$compilerDir\ISCC.exe" "/DReleaseVersion=$Version" "/DPayloadDir=$payloadPath" "/O$outputPath" "$PSScriptRoot\installer.iss"

$installation = Join-Path $env:RUNNER_TEMP 'manga-organizer-installed'
Run-Setup "$outputPath\MangaOrganizer-Setup-win-x64.exe" ($silent + '/SP-' + "/DIR=`"$installation`"")
try {
    python tests/foundation_smoke.py --app "$installation\MangaOrganizer.Server.exe"
} finally {
    Run-Setup "$installation\unins000.exe" $silent
}
if (Test-Path -LiteralPath "$installation\MangaOrganizer.Server.exe") {
    throw 'The uninstaller left the installed server executable in place.'
}
