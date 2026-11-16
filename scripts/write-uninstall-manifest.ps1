param([Parameter(Mandatory=$true)][string]$AppDirectory)
$ErrorActionPreference='Stop'
$payloadRoot=(Resolve-Path -LiteralPath $AppDirectory).Path
if(-not (Test-Path -LiteralPath (Join-Path $payloadRoot 'magnetic-desk.exe'))){throw 'Application executable is missing'}
if(-not (Test-Path -LiteralPath (Join-Path $payloadRoot 'magnetic-desk.Uninstaller.exe'))){throw 'Uninstaller is missing'}
$files=@(Get-ChildItem -LiteralPath $payloadRoot -Recurse -File | Where-Object { $_.Name -ne 'magnetic-desk.UninstallManifest.json' } | ForEach-Object {
    if(($_.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0){throw 'Published payload contains a link'}
    $sha=[Security.Cryptography.SHA256]::Create()
    $stream=[IO.File]::OpenRead($_.FullName)
    try { $digest=[BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-','') }
    finally { $stream.Dispose(); $sha.Dispose() }
    [ordered]@{Path=$_.FullName.Substring($payloadRoot.Length+1);Sha256=$digest}
})
$document=[ordered]@{Product='magnetic-desk';Files=$files}|ConvertTo-Json -Depth 4
[IO.File]::WriteAllText((Join-Path $payloadRoot 'magnetic-desk.UninstallManifest.json'),$document,[Text.UTF8Encoding]::new($false))
