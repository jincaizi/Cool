function Wait-WebGlHealth {
    param(
        [string]$Server,
        [string]$Release,
        [int]$WebPort = 8080,
        [int]$GamePort = 8000
    )

    $deadline = [Diagnostics.Stopwatch]::StartNew()
    do {
        try {
            $web = 'http://{0}:{1}' -f $Server, $WebPort
            $version = Invoke-WebRequest -Uri ($web + '/cool-release.txt') -UseBasicParsing -TimeoutSec 3 -ErrorAction Stop
            $body = [Text.Encoding]::ASCII.GetString($version.RawContentStream.ToArray()).Trim()
            if ($body -cne $Release) { throw "The web server returned release '$body', expected '$Release'." }
            $page = Invoke-WebRequest -Uri ($web + '/') -UseBasicParsing -TimeoutSec 3 -ErrorAction Stop
            if ($page.StatusCode -ne 200) { throw "The WebGL entry page returned HTTP $($page.StatusCode)." }

            $socket = [Net.WebSockets.ClientWebSocket]::new()
            $cancel = [Threading.CancellationTokenSource]::new(3000)
            try {
                $socket.ConnectAsync([Uri]('ws://{0}:{1}/' -f $Server, $GamePort), $cancel.Token).GetAwaiter().GetResult() | Out-Null
            }
            finally {
                $socket.Dispose()
                $cancel.Dispose()
            }
            return
        }
        catch {
            $lastError = $_.Exception.Message
            Start-Sleep -Seconds 1
        }
    } while ($deadline.Elapsed.TotalSeconds -lt 30)
    throw "WebGL health check on $Server failed: $lastError"
}

function Stop-WebGlTask {
    param([string]$Name)

    Stop-ScheduledTask -TaskName $Name -TaskPath '\'
    $deadline = [Diagnostics.Stopwatch]::StartNew()
    while ((Get-ScheduledTask -TaskName $Name -TaskPath '\').State -eq 'Running') {
        if ($deadline.Elapsed.TotalSeconds -ge 8) { throw "Timed out stopping scheduled task $Name." }
        Start-Sleep -Milliseconds 250
    }
}

function Start-WebGlRelease {
    param([string]$Directory, [string]$Release)

    $ErrorActionPreference = 'Stop'
    $root = Split-Path -Parent (Split-Path -Parent $Directory)
    $lock = [IO.File]::Open((Join-Path $root 'activation.lock'), 'OpenOrCreate', 'ReadWrite', 'None')
    try {
        foreach ($port in @(8080, 8000)) {
            $prefix = 'http://+:' + $port + '/'
            $reservation = & netsh.exe http show urlacl "url=$prefix"
            if (($reservation -join "`n") -notmatch [regex]::Escape($prefix)) {
                & netsh.exe http add urlacl "url=$prefix" 'sddl=D:(A;;GX;;;LS)(A;;GX;;;BA)' | Out-Host
                if ($LASTEXITCODE -ne 0) { throw "Could not reserve $prefix for Local Service." }
            }

            $rule = "CoolDemo-WebGL-TCP-$port"
            if (-not (Get-NetFirewallRule -Name $rule -ErrorAction SilentlyContinue)) {
                New-NetFirewallRule -Name $rule -DisplayName $rule -Direction Inbound -Protocol TCP -LocalPort $port -Action Allow | Out-Null
            }
        }

        & icacls.exe $Directory /grant '*S-1-5-19:(OI)(CI)RX' | Out-Host
        if ($LASTEXITCODE -ne 0) { throw 'Could not grant Local Service access to the release.' }

        $serverDir = Join-Path $Directory 'dist\server'
        $siteDir = Join-Path $Directory 'dist\site'
        $tasks = [ordered]@{
            'CoolDemo-WebGL-Site' = New-ScheduledTaskAction -Execute (Join-Path $serverDir 'StaticServer.exe') -WorkingDirectory $serverDir -Argument ('--root "' + $siteDir + '" --port 8080 --prefix http://+:8080/')
            'CoolDemo-WebGL-Game' = New-ScheduledTaskAction -Execute (Join-Path $serverDir 'Server3C.exe') -WorkingDirectory $serverDir -Argument '8000 --monsters 15 --bots 100 --ws-prefix http://+:8000/'
        }
        $account = [Security.Principal.SecurityIdentifier]::new('S-1-5-19').Translate([Security.Principal.NTAccount]).Value
        $principal = New-ScheduledTaskPrincipal -UserId $account -LogonType ServiceAccount
        $trigger = New-ScheduledTaskTrigger -AtStartup
        $settings = New-ScheduledTaskSettingsSet -ExecutionTimeLimit ([TimeSpan]::Zero) -MultipleInstances IgnoreNew
        $previous = @{}
        foreach ($name in $tasks.Keys) {
            $task = Get-ScheduledTask -TaskName $name -TaskPath '\' -ErrorAction SilentlyContinue
            if ($task) {
                $previous[$name] = @{
                    Xml = Export-ScheduledTask -TaskName $name -TaskPath '\'
                    Running = $task.State -eq 'Running'
                }
            }
        }

        $changed = @()
        try {
            foreach ($name in $tasks.Keys) {
                $changed += $name
                if ($previous[$name].Running) { Stop-WebGlTask -Name $name }
                Register-ScheduledTask -TaskName $name -TaskPath '\' -Action $tasks[$name] -Principal $principal -Trigger $trigger -Settings $settings -Force | Out-Null
                Start-ScheduledTask -TaskName $name -TaskPath '\'
            }
            Wait-WebGlHealth -Server '127.0.0.1' -Release $Release
            foreach ($name in $tasks.Keys) {
                if ((Get-ScheduledTask -TaskName $name -TaskPath '\').State -ne 'Running') {
                    throw "Scheduled task $name did not stay running. Close any old manually launched demo before deploying."
                }
            }
        }
        catch {
            $failure = $_
            foreach ($name in $changed) {
                try {
                    $task = Get-ScheduledTask -TaskName $name -TaskPath '\' -ErrorAction SilentlyContinue
                    if ($task -and $task.State -eq 'Running') { Stop-WebGlTask -Name $name }
                    if ($previous.ContainsKey($name)) {
                        Register-ScheduledTask -TaskName $name -TaskPath '\' -Xml $previous[$name].Xml -Force | Out-Null
                        if ($previous[$name].Running) { Start-ScheduledTask -TaskName $name -TaskPath '\' }
                    }
                    elseif ($task) {
                        Unregister-ScheduledTask -TaskName $name -TaskPath '\' -Confirm:$false
                    }
                }
                catch {
                    Write-Warning "Could not restore $name`: $($_.Exception.Message)"
                }
            }
            throw $failure
        }
    }
    finally {
        $lock.Dispose()
    }
}
