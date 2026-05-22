function Assert-MelezhHandlerJson {
    param(
        [Parameter(Mandatory = $true)][string]$Uri,
        [string]$Label = $Uri,
        [int]$TimeoutSec = 120,
        [string]$Method = "GET",
        [string]$Body = ""
    )

    if ($Method -eq "POST") {
        $response = Invoke-WebRequest -Uri $Uri -Method POST -Body $(if ($Body) { $Body } else { "{}" }) `
            -ContentType "application/json" -UseBasicParsing -TimeoutSec $TimeoutSec
    }
    else {
        $response = Invoke-WebRequest -Uri $Uri -UseBasicParsing -TimeoutSec $TimeoutSec
    }
    if ($response.StatusCode -lt 200 -or $response.StatusCode -ge 300) {
        throw "$Label HTTP $($response.StatusCode)"
    }

    $body = $response.Content.Trim()
    if ([string]::IsNullOrWhiteSpace($body)) {
        throw "$Label returned empty body"
    }

    try {
        $json = $body | ConvertFrom-Json
    }
    catch {
        throw "$Label body is not JSON: $($body.Substring(0, [Math]::Min(200, $body.Length)))"
    }

    if ($null -ne $json.PSObject.Properties['result'] -and $json.result -eq $false) {
        $err = if ($json.PSObject.Properties['error']) { $json.error } else { $body }
        throw "$Label result=false error=$err"
    }

    return $json
}
