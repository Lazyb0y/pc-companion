<#
.SYNOPSIS
Stands in for GitHub's releases API in Windows Sandbox, for the Updates group: answers HTTPS
requests on port 443, which the hosts file points api.github.com to, with the JSON in $Answer, and
adds each request's method, path and headers to $RequestLog as a line of JSON. While the file
$Status exists, it answers with the status code it holds instead, such as 429 for a rate limit.
Runs until stopped.
#>
param(
    [Parameter(Mandatory)]
    [string] $RequestLog,
    [Parameter(Mandatory)]
    [string] $Answer,
    [string] $Status
)

$listener = New-Object System.Net.HttpListener
$listener.Prefixes.Add('https://+:443/')
$listener.Start()
while ($listener.IsListening) {
    $context = $listener.GetContext()
    $request = $context.Request
    $headers = [ordered] @{}
    foreach ($name in $request.Headers.AllKeys) { $headers[$name] = $request.Headers[$name] }
    $entry = [ordered] @{ Method = $request.HttpMethod; Path = $request.Url.AbsolutePath; Headers = $headers }
    Add-Content -Path $RequestLog -Value ($entry | ConvertTo-Json -Compress) -Encoding utf8

    $code = 200
    $json = Get-Content -Path $Answer -Raw -Encoding utf8
    if ($Status -and (Test-Path $Status)) {
        $code = [int] (Get-Content -Path $Status -Raw).Trim()
        $json = '{ "message": "Stand-in error" }'
    }
    $body = [Text.Encoding]::UTF8.GetBytes($json)
    $context.Response.StatusCode = $code
    $context.Response.ContentType = 'application/json; charset=utf-8'
    $context.Response.ContentLength64 = $body.Length
    $context.Response.OutputStream.Write($body, 0, $body.Length)
    $context.Response.Close()
}
