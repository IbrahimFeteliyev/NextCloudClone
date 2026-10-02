param([string]$Api = 'http://localhost:5050/api', [string]$Password = 'Demo123!', [string]$OfficeFixtureDirectory = '')
$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSVersion.Major -lt 7) { throw 'Run this script with PowerShell 7.' }
function Login([string]$Email) { Invoke-RestMethod -Method Post -Uri "$Api/auth/login" -ContentType application/json -Body (@{ email = $Email; password = $Password } | ConvertTo-Json) }
function Json([string]$Method, [string]$Path, [string]$Token, $Body = $null) {
    $requestArgs = @{ Method = $Method; Uri = "$Api$Path"; Headers = @{ Authorization = "Bearer $Token" } }
    if ($null -ne $Body) { $requestArgs.ContentType = 'application/json'; $requestArgs.Body = $Body | ConvertTo-Json }
    Invoke-RestMethod @requestArgs
}
function Assert([bool]$Condition, [string]$Message) { if (!$Condition) { throw "FAILED: $Message" }; Write-Host "PASS: $Message" }
function Denied([int]$Status, [scriptblock]$Action, [string]$Message) {
    try { & $Action | Out-Null; throw "FAILED: $Message (allowed)" }
    catch { if ([int]$_.Exception.Response.StatusCode -ne $Status) { throw }; Write-Host "PASS: $Message" }
}
$finance = Login 'finance@demo.local'; $manager = Login 'manager@demo.local'; $employee = Login 'employee@demo.local'
$root = Json Get '/explorer?view=files' $finance.token
$name = 'ONLYOFFICE demo ' + [DateTime]::Now.ToString('yyyyMMdd-HHmmss')
$folder = Json Post '/folders' $finance.token @{ name = $name; parentFolderId = $root.folderId }
$nested = Json Post '/folders' $finance.token @{ name = 'Budget'; parentFolderId = $folder.id }
$xlsx = Join-Path $PSScriptRoot 'fixtures/Budget_2027.xlsx'
$file = Invoke-RestMethod -Method Post -Uri "$Api/files/upload" -Headers @{ Authorization = "Bearer $($finance.token)" } -Form @{ parentFolderId = $nested.id; file = Get-Item -LiteralPath $xlsx }
Json Put "/resources/folder/$($folder.id)/shares" $finance.token @{ userId = $manager.user.id; permissions = 1 } | Out-Null
$edit = Json Get "/onlyoffice/files/$($file.id)/config" $finance.token
$view = Json Get "/onlyoffice/files/$($file.id)/config" $manager.token
Assert ($edit.mode -eq 'edit' -and $edit.config.document.permissions.edit) 'Owner receives edit mode'
Assert ($view.mode -eq 'view' -and !$view.config.document.permissions.edit) 'Nested folder READ grant produces view mode'
Assert ($view.config.document.permissions.download -and $view.config.document.permissions.print) 'READ maps to download and print'
Assert ($edit.config.document.key -eq $view.config.document.key) 'Authorized users share the same document key'
Assert ($edit.config.token.Length -gt 0 -and $view.config.token.Length -gt 0) 'Configurations include JWT signatures'
Denied 403 { Json Get "/onlyoffice/files/$($file.id)/config?mode=edit" $manager.token } 'READ user cannot request edit mode'
Denied 403 { Json Get "/onlyoffice/files/$($file.id)/config" $employee.token } 'Unshared user cannot open the file'
Denied 404 { Json Get "/onlyoffice/files/$([Guid]::NewGuid())/config" $finance.token } 'Missing file returns 404'
$uri = [Uri]$view.config.document.url
$localContent = "$Api/onlyoffice/files/$($file.id)/content$($uri.Query)"
$download = Invoke-WebRequest $localContent
Assert ($download.RawContentLength -eq (Get-Item -LiteralPath $xlsx).Length) 'Scoped document URL retrieves the MinIO binary'
Denied 401 { Invoke-RestMethod "$Api/onlyoffice/files/$($file.id)/content" } 'Document URL requires a scoped token'
$callbackQuery = ([Uri]$edit.config.editorConfig.callbackUrl).Query
Denied 401 { Invoke-RestMethod -Method Post "$Api/onlyoffice/files/$($file.id)/callback$callbackQuery" -ContentType application/json -Body '{"token":"invalid","status":2}' } 'Invalid callback JWT is rejected'
$history = @(Json Get "/files/$($file.id)/versions" $finance.token)
Assert ($history.Count -eq 1 -and $history[0].number -eq 1) 'Initial immutable version exists'
Json Put "/resources/folder/$($folder.id)/shares" $finance.token @{ userId = $manager.user.id; permissions = 3 } | Out-Null
$collaborator = Json Get "/onlyoffice/files/$($file.id)/config" $manager.token
Assert ($collaborator.mode -eq 'edit' -and $collaborator.config.document.key -eq $edit.config.document.key) 'WRITE upgrade joins the same collaboration session'
# Check unsupported files without changing the Office document.
$textPath = Join-Path (Split-Path $PSScriptRoot -Parent) '.cache/office-unsupported.txt'
New-Item -ItemType Directory -Force -Path (Split-Path $textPath -Parent) | Out-Null
Set-Content -LiteralPath $textPath -Value 'ONLYOFFICE unsupported-type smoke test'
$text = Invoke-RestMethod -Method Post -Uri "$Api/files/upload" -Headers @{ Authorization = "Bearer $($finance.token)" } -Form @{ parentFolderId = $folder.id; file = Get-Item -LiteralPath $textPath }
Denied 415 { Json Get "/onlyoffice/files/$($text.id)/config" $finance.token } 'Unsupported type returns 415'
if ($OfficeFixtureDirectory) {
    foreach ($fixture in Get-ChildItem -LiteralPath $OfficeFixtureDirectory -File | Where-Object Extension -in '.docx', '.pptx') {
        $uploaded = Invoke-RestMethod -Method Post -Uri "$Api/files/upload" -Headers @{ Authorization = "Bearer $($finance.token)" } -Form @{ parentFolderId = $folder.id; file = $fixture }
        $configuration = Json Get "/onlyoffice/files/$($uploaded.id)/config" $finance.token
        Assert ($configuration.mode -eq 'edit') "$($fixture.Name) receives a signed editor configuration"
    }
}
Write-Host "Demo ready: '$name' > Budget > Budget_2027.xlsx. Finance and Manager can collaborate."
Write-Host 'Edit in the browser, press Save, close all editors, then check GET /api/files/{id}/versions.'
Write-Host "File ID: $($file.id)"
