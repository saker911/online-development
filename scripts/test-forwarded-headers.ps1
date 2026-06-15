param(
    [string]$BaseUrl = "http://127.0.0.1:5001",
    [string]$ProbePath = "/Account/Login",
    [string]$ForwardedFor = "198.51.100.24"
)

$targetUrl = '{0}{1}' -f $BaseUrl.TrimEnd('/'), $ProbePath

function Get-SetCookieHeader {
    param(
        [string]$Uri,
        [hashtable]$Headers = @{}
    )

    $response = Invoke-WebRequest -UseBasicParsing -Uri $Uri -Headers $Headers
    return [string]$response.Headers['Set-Cookie']
}

function Has-SecureFlag {
    param([string]$SetCookieHeader)

    return $SetCookieHeader -match '(^|[;,]\s*)secure([;,]|$)'
}

try {
    $normalSetCookie = Get-SetCookieHeader -Uri $targetUrl
    $forwardedSetCookie = Get-SetCookieHeader -Uri $targetUrl -Headers @{
        'X-Forwarded-Proto' = 'https'
        'X-Forwarded-For'   = $ForwardedFor
    }

    $normalSecure = Has-SecureFlag -SetCookieHeader $normalSetCookie
    $forwardedSecure = Has-SecureFlag -SetCookieHeader $forwardedSetCookie

    [pscustomobject]@{
        TargetUrl                  = $targetUrl
        TrustedProxyScenario       = 'Loopback request to app with forwarded https headers'
        NormalSetCookie            = $normalSetCookie
        ForwardedSetCookie         = $forwardedSetCookie
        NormalHasSecureFlag        = $normalSecure
        ForwardedHasSecureFlag     = $forwardedSecure
        ForwardedHeadersRecognized = (-not $normalSecure) -and $forwardedSecure
    } | Format-List

    if ((-not $normalSecure) -and $forwardedSecure) {
        Write-Host ''
        Write-Host 'PASS: التطبيق قرأ X-Forwarded-Proto=https وتعامل مع الطلب كأنه HTTPS.' -ForegroundColor Green
        exit 0
    }

    Write-Host ''
    Write-Host 'FAIL: النتيجة لا تثبت أن التطبيق قرأ forwarded headers كما هو متوقع.' -ForegroundColor Red
    exit 1
}
catch {
    Write-Error "فشل اختبار forwarded headers على $targetUrl. تأكد أن التطبيق يعمل أولاً. $($_.Exception.Message)"
    exit 1
}
