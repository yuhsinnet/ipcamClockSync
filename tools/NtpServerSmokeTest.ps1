[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$PublishRoot = (Join-Path $PSScriptRoot "..\artifacts\smoketest"),
    [string]$CliExePath,
    [string]$ServiceExePath,
    [switch]$SkipPublish,
    [switch]$KeepArtifacts,
    [int]$StateWaitSeconds = 20
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Test-IsAdministrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Invoke-Step {
    param(
        [Parameter(Mandatory = $true)][string]$Title,
        [Parameter(Mandatory = $true)][scriptblock]$Action
    )

    Write-Host "`n==> $Title" -ForegroundColor Cyan
    & $Action
}

function Invoke-External {
    param(
        [Parameter(Mandatory = $true)][string]$FilePath,
        [Parameter(Mandatory = $true)][string[]]$Arguments,
        [switch]$AllowNonZeroExit
    )

    Write-Host ("[CMD] {0} {1}" -f $FilePath, ($Arguments -join ' ')) -ForegroundColor DarkGray
    $output = & $FilePath @Arguments 2>&1
    $exitCode = $LASTEXITCODE

    if ($output) {
        $output | ForEach-Object { Write-Host $_ }
    }

    if (-not $AllowNonZeroExit -and $exitCode -ne 0) {
        throw "Command failed with exit code $exitCode."
    }

    return [pscustomobject]@{
        ExitCode = $exitCode
        Output = ($output -join [Environment]::NewLine)
    }
}

function Get-ServiceStateText {
    param(
        [Parameter(Mandatory = $true)][string]$CliPath
    )

    $result = Invoke-External -FilePath $CliPath -Arguments @('/ntpserver', 'status') -AllowNonZeroExit

    if ($result.Output -match 'FAILED 1060' -or $result.Output -match 'does not exist') {
        return 'MISSING'
    }

    if ($result.Output -match 'STATE\s+:\s+\d+\s+RUNNING') {
        return 'RUNNING'
    }

    if ($result.Output -match 'STATE\s+:\s+\d+\s+STOPPED') {
        return 'STOPPED'
    }

    if ($result.Output -match 'STATE\s+:\s+\d+\s+START_PENDING') {
        return 'START_PENDING'
    }

    if ($result.Output -match 'STATE\s+:\s+\d+\s+STOP_PENDING') {
        return 'STOP_PENDING'
    }

    return 'UNKNOWN'
}

function Wait-ForServiceState {
    param(
        [Parameter(Mandatory = $true)][string]$CliPath,
        [Parameter(Mandatory = $true)][string]$ExpectedState,
        [Parameter(Mandatory = $true)][int]$TimeoutSeconds
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    do {
        $state = Get-ServiceStateText -CliPath $CliPath
        if ($state -eq $ExpectedState) {
            Write-Host "Service state reached: $ExpectedState" -ForegroundColor Green
            return
        }

        Start-Sleep -Milliseconds 800
    } while ((Get-Date) -lt $deadline)

    throw "Timed out waiting for service state '$ExpectedState'. Last state: '$state'."
}

function Resolve-PublishLayout {
    param(
        [Parameter(Mandatory = $true)][string]$Root,
        [Parameter(Mandatory = $true)][string]$ConfigurationName,
        [Parameter(Mandatory = $true)][string]$RuntimeName
    )

    $resolvedRoot = [System.IO.Path]::GetFullPath($Root)
    $mainPath = Join-Path $resolvedRoot 'main'
    $ntpPath = Join-Path $resolvedRoot 'ntpserver'

    Invoke-Step -Title 'Publish main CLI/GUI executable' -Action {
        dotnet publish (Join-Path $PSScriptRoot '..\IPCamClockSync\IPCamClockSync.csproj') -c $ConfigurationName -r $RuntimeName --self-contained false -o $mainPath
    }

    Invoke-Step -Title 'Publish standalone NTP Server executable' -Action {
        dotnet publish (Join-Path $PSScriptRoot '..\IPCamClockSync.NtpServer\IPCamClockSync.NtpServer.csproj') -c $ConfigurationName -r $RuntimeName --self-contained false -o $ntpPath
    }

    return [pscustomobject]@{
        CliExePath = Join-Path $mainPath 'IPCamClockSync.exe'
        ServiceExePath = Join-Path $ntpPath 'IPCamClockSync.NtpServer.exe'
        Root = $resolvedRoot
    }
}

if (-not $IsWindows) {
    throw 'This smoke test only supports Windows.'
}

if (-not (Test-IsAdministrator)) {
    throw 'Please run this smoke test in an elevated PowerShell session.'
}

$resolvedPublishRoot = [System.IO.Path]::GetFullPath($PublishRoot)

if (-not $SkipPublish) {
    $layout = Resolve-PublishLayout -Root $resolvedPublishRoot -ConfigurationName $Configuration -RuntimeName $Runtime
    $CliExePath = $layout.CliExePath
    $ServiceExePath = $layout.ServiceExePath
}
else {
    if ([string]::IsNullOrWhiteSpace($CliExePath) -or [string]::IsNullOrWhiteSpace($ServiceExePath)) {
        throw 'When -SkipPublish is used, both -CliExePath and -ServiceExePath are required.'
    }

    $CliExePath = [System.IO.Path]::GetFullPath($CliExePath)
    $ServiceExePath = [System.IO.Path]::GetFullPath($ServiceExePath)
}

if (-not (Test-Path $CliExePath)) {
    throw "CLI executable not found: $CliExePath"
}

if (-not (Test-Path $ServiceExePath)) {
    throw "Service executable not found: $ServiceExePath"
}

Write-Host "CLI executable: $CliExePath" -ForegroundColor Yellow
Write-Host "Service executable: $ServiceExePath" -ForegroundColor Yellow

$cleanupNeeded = -not $KeepArtifacts.IsPresent -and -not $SkipPublish.IsPresent

try {
    Invoke-Step -Title 'Best-effort cleanup of previous service instance' -Action {
        Invoke-External -FilePath $CliExePath -Arguments @('/ntpserver', 'stop') -AllowNonZeroExit | Out-Null
        Invoke-External -FilePath $CliExePath -Arguments @('/ntpserver', 'service', 'uninstall') -AllowNonZeroExit | Out-Null
    }

    Invoke-Step -Title 'Install service' -Action {
        Invoke-External -FilePath $CliExePath -Arguments @('/ntpserver', 'service', 'install', $ServiceExePath) | Out-Null
    }

    Invoke-Step -Title 'Verify service is registered' -Action {
        Wait-ForServiceState -CliPath $CliExePath -ExpectedState 'STOPPED' -TimeoutSeconds $StateWaitSeconds
    }

    Invoke-Step -Title 'Start service' -Action {
        Invoke-External -FilePath $CliExePath -Arguments @('/ntpserver', 'start') | Out-Null
        Wait-ForServiceState -CliPath $CliExePath -ExpectedState 'RUNNING' -TimeoutSeconds $StateWaitSeconds
    }

    Invoke-Step -Title 'Restart service' -Action {
        Invoke-External -FilePath $CliExePath -Arguments @('/ntpserver', 'restart') | Out-Null
        Wait-ForServiceState -CliPath $CliExePath -ExpectedState 'RUNNING' -TimeoutSeconds $StateWaitSeconds
    }

    Invoke-Step -Title 'Stop service' -Action {
        Invoke-External -FilePath $CliExePath -Arguments @('/ntpserver', 'stop') -AllowNonZeroExit | Out-Null
        Wait-ForServiceState -CliPath $CliExePath -ExpectedState 'STOPPED' -TimeoutSeconds $StateWaitSeconds
    }

    Invoke-Step -Title 'Uninstall service' -Action {
        Invoke-External -FilePath $CliExePath -Arguments @('/ntpserver', 'service', 'uninstall') -AllowNonZeroExit | Out-Null
    }

    Invoke-Step -Title 'Verify service is removed' -Action {
        $deadline = (Get-Date).AddSeconds($StateWaitSeconds)
        do {
            $state = Get-ServiceStateText -CliPath $CliExePath
            if ($state -eq 'MISSING') {
                Write-Host 'Service removed successfully.' -ForegroundColor Green
                return
            }

            Start-Sleep -Milliseconds 800
        } while ((Get-Date) -lt $deadline)

        throw "Timed out waiting for service removal. Last state: '$state'."
    }

    Write-Host "`nSmoke test completed successfully." -ForegroundColor Green
}
finally {
    if ($cleanupNeeded -and (Test-Path $resolvedPublishRoot)) {
        Write-Host "Cleaning publish artifacts: $resolvedPublishRoot" -ForegroundColor DarkGray
        Remove-Item -Path $resolvedPublishRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}
