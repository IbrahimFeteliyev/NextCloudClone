param([switch]$InfrastructureOnly)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot -Parent
$envPath = Join-Path $taskRoot 'infrastructure/.env'
if (!(Test-Path -LiteralPath $envPath)) {
    Copy-Item -LiteralPath (Join-Path $taskRoot 'infrastructure/.env.example') -Destination $envPath
}
# Import data, never execute contents of the env file. Keep secrets out of console output.
$settings = @{}
foreach ($line in Get-Content -LiteralPath $envPath) {
    if ($line -match '^([A-Za-z_][A-Za-z0-9_]*)=(.*)$') { $settings[$Matches[1]] = $Matches[2].Trim() }
}
if (!$settings['OnlyOffice__JwtSecret']) {
    $secretBytes = New-Object byte[] 32
    $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    try { $rng.GetBytes($secretBytes) } finally { $rng.Dispose() }
    $settings['OnlyOffice__JwtSecret'] = [Convert]::ToBase64String($secretBytes)
    $envLines = @(Get-Content -LiteralPath $envPath | Where-Object { $_ -notmatch '^OnlyOffice__JwtSecret=' })
    $envLines += 'OnlyOffice__JwtSecret=' + $settings['OnlyOffice__JwtSecret']
    Set-Content -LiteralPath $envPath -Value $envLines -Encoding utf8
}
# Older local .env files need the new non-secret settings from the maintained example.
foreach ($line in Get-Content -LiteralPath (Join-Path $taskRoot 'infrastructure/.env.example')) {
    if ($line -match '^(OnlyOffice__[A-Za-z0-9_]+)=(.*)$' -and !$settings[$Matches[1]]) {
        $settings[$Matches[1]] = $Matches[2].Trim()
    }
}
foreach ($entry in $settings.GetEnumerator()) {
    if ($entry.Key -like 'OnlyOffice__*') { [Environment]::SetEnvironmentVariable($entry.Key, $entry.Value, 'Process') }
}
$dockerCommand = Get-Command docker -ErrorAction SilentlyContinue
$dockerExe = if ($dockerCommand) { $dockerCommand.Source } else { Join-Path $env:LOCALAPPDATA 'Programs/DockerDesktop/resources/bin/docker.exe' }
if (!(Test-Path -LiteralPath $dockerExe)) { throw 'Docker Desktop is required. Add docker.exe to PATH.' }
Push-Location (Join-Path $taskRoot 'infrastructure')
try {
    & $dockerExe compose --profile office up -d --build
    if ($LASTEXITCODE -ne 0) { throw 'Unable to start the Docker services.' }
} finally { Pop-Location }
if ($InfrastructureOnly) { Write-Host 'Infrastructure started. Run this script without -InfrastructureOnly to start the API with ONLYOFFICE settings.'; return }
if (Get-NetTCPConnection -LocalPort 5050 -State Listen -ErrorAction SilentlyContinue) {
    throw 'An API is already listening on port 5050. Stop that API terminal, then run this script again so it receives the ONLYOFFICE settings.'
}
$env:ASPNETCORE_ENVIRONMENT = 'Development'
Push-Location $taskRoot
try {
    # Bind to the host interface so the Docker Document Server can retrieve documents and post callbacks.
    & dotnet run --project backend --no-launch-profile --urls http://0.0.0.0:5050
    if ($LASTEXITCODE -ne 0) { throw 'The Atlas API exited with an error.' }
} finally { Pop-Location }
