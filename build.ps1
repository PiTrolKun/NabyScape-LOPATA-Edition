[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $LopataRepositoryRoot,
    [string] $OutputDirectory = (Join-Path $PSScriptRoot 'artifacts-edition'),
    [switch] $RunSelfTests
)

$ErrorActionPreference = 'Stop'
$checkout = (Resolve-Path -LiteralPath $LopataRepositoryRoot).Path
$releaseTools = Join-Path $checkout 'Инструменты/IslandReleasePayload.ps1'
if (!(Test-Path -LiteralPath $releaseTools -PathType Leaf)) {
    throw 'Choose a LOPATA checkout containing the island packaging tools.'
}

New-Item -Path $OutputDirectory -ItemType Directory -Force | Out-Null
$output = (Resolve-Path -LiteralPath $OutputDirectory).Path
$commit = git -C $PSScriptRoot rev-parse --verify HEAD
if ($LASTEXITCODE -ne 0 -or $commit -cnotmatch '^[a-f0-9]{40}$') {
    throw 'The edition commit could not be identified.'
}

. $releaseTools
Add-IslandReleasePayload -PublishDirectory $output -Commit $commit -IslandForkDirectory $PSScriptRoot
Write-Output "Verified package at $output/NabyScape"

if ($RunSelfTests) {
    $exe = Join-Path $output 'NabyScape/LopataIsland.exe'
    $report = Join-Path $output 'unit-report.txt'
    $test = Start-Process -FilePath $exe -ArgumentList @('--self-test', '--report', ('"' + $report + '"')) -WindowStyle Hidden -PassThru
    if (!$test.WaitForExit(180000)) {
        $test.Kill()
        throw 'Island self-tests timed out.'
    }

    Get-Content -LiteralPath $report | Where-Object { $_ -like 'FAIL  *' -or $_ -like 'RESULT:*' }
    if ($test.ExitCode -ne 0) {
        throw "Island self-tests failed. Report is at $report"
    }
}
