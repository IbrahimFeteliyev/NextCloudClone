param([string]$ApiUrl = 'http://localhost:5050/api')
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot -Parent
$fixtureDir = Join-Path $taskRoot '.cache/text-upload-fixtures'
New-Item -ItemType Directory -Force -Path $fixtureDir | Out-Null
$checks = 0
function Assert-Demo($condition, $message) { if (!$condition) { throw $message }; $script:checks++; Write-Host "PASS $message" }
function Login-Demo($email) { Invoke-RestMethod "$ApiUrl/auth/login" -Method Post -ContentType 'application/json' -Body (@{email=$email;password='Demo123!'} | ConvertTo-Json) }
function Call-Demo($headers, $path, $method = 'GET', $body = $null) {
    $args = @{Uri="$ApiUrl$path";Headers=$headers;Method=$method}
    if ($null -ne $body) { $args.ContentType='application/json'; $args.Body=$body | ConvertTo-Json -Depth 8 -Compress }
    Invoke-RestMethod @args
}
function Status-Demo($headers, $path, $method, $body, $expected) {
    try { Call-Demo $headers $path $method $body | Out-Null; throw "Expected HTTP $expected" }
    catch { Assert-Demo ($_.Exception.Response.StatusCode.value__ -eq $expected) "HTTP $expected on $method $path" }
}
$finance = Login-Demo 'finance@demo.local'; $manager = Login-Demo 'manager@demo.local'; $employee = Login-Demo 'employee@demo.local'
$ownerHeaders=@{Authorization="Bearer $($finance.token)"}; $readHeaders=@{Authorization="Bearer $($manager.token)"}; $noneHeaders=@{Authorization="Bearer $($employee.token)"}
$rootExplorer = Call-Demo $ownerHeaders '/explorer'
$folder = Call-Demo $ownerHeaders '/folders' 'POST' @{name="Text and uploads $(Get-Date -Format yyyyMMdd-HHmmss)";parentFolderId=$rootExplorer.folderId}
Call-Demo $ownerHeaders "/resources/folder/$($folder.id)/shares" 'PUT' @{userId=$manager.user.id;permissions=1} | Out-Null
$welcome = "Welcome to Atlas`r`n`r`nYour files. Your team. Your control.`r`n`r`nClick Edit to update this UTF-8 document.`r`nCtrl+F: Find | Ctrl+H: Replace | Ctrl+G: Go to line`r`nAzərbaycan dili dəstəklənir.`r`n"
[IO.File]::WriteAllText((Join-Path $fixtureDir 'Welcome to Atlas.txt'), $welcome, [Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllText((Join-Path $fixtureDir 'save-test.log'), 'original', [Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllText((Join-Path $fixtureDir 'large.log'), ('large log line' + "`n") * 40000, [Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllText((Join-Path $fixtureDir 'unsupported.rtf'), 'Unsupported RTF fixture', [Text.UTF8Encoding]::new($false))
$files=@{}
foreach ($name in @('Welcome to Atlas.txt','save-test.log','large.log','unsupported.rtf')) {
    $files[$name] = Invoke-RestMethod "$ApiUrl/files/upload" -Method Post -Headers $ownerHeaders -Form @{parentFolderId=$folder.id;file=Get-Item -LiteralPath (Join-Path $fixtureDir $name)}
}
$file=$files['save-test.log']; $path="/files/$($file.id)/content"
$ticket=Call-Demo $readHeaders "/files/$($file.id)/preview" 'POST'
Assert-Demo ($ticket.permissions -eq 1 -and $ticket.textEditLimit -eq 524288) 'Preview exposes backend READ permission and text byte limit'
Status-Demo $readHeaders $path 'PUT' @{content='forbidden';expectedVersion=1} 403
Status-Demo $noneHeaders $path 'PUT' @{content='forbidden';expectedVersion=1} 403
Status-Demo @{} $path 'PUT' @{content='forbidden';expectedVersion=1} 401
Status-Demo $ownerHeaders '/files/00000000-0000-0000-0000-000000000001/content' 'PUT' @{content='missing';expectedVersion=1} 404
Status-Demo $ownerHeaders "/files/$($files['unsupported.rtf'].id)/content" 'PUT' @{content='unsupported';expectedVersion=1} 415
Status-Demo $ownerHeaders "/files/$($files['large.log'].id)/content" 'PUT' @{content='small';expectedVersion=1} 413
Status-Demo $ownerHeaders $path 'PUT' @{content=('ə' * 262145);expectedVersion=1} 413
$updated="Azərbaycan`r`nSaved to MinIO`r`n"
$saved=Call-Demo $ownerHeaders $path 'PUT' @{content=$updated;expectedVersion=1}
Assert-Demo ($saved.version -eq 2) 'TXT/LOG save advances document version'
$content=Invoke-WebRequest "$ApiUrl$path" -Headers $readHeaders
Assert-Demo ([Text.Encoding]::UTF8.GetString($content.RawContentStream.ToArray()) -eq $updated) 'READ user receives updated Unicode content with CRLF preserved'
$versions=Call-Demo $readHeaders "/files/$($file.id)/versions"
Assert-Demo ($versions.Count -eq 2) 'Both immutable file versions exist'
$old=Invoke-WebRequest "$ApiUrl/files/$($file.id)/versions/1/download" -Headers $readHeaders
Assert-Demo ([Text.Encoding]::UTF8.GetString($old.RawContentStream.ToArray()) -eq 'original') 'Original version remains downloadable'
Status-Demo $ownerHeaders $path 'PUT' @{content='stale';expectedVersion=1} 409
$empty=Call-Demo $ownerHeaders $path 'PUT' @{content='';expectedVersion=2}
Assert-Demo ($empty.version -eq 3 -and $empty.size -eq 0) 'Empty text is saved through MinIO and versioning'
$logs=Call-Demo $ownerHeaders '/audit'
Assert-Demo (@($logs | Where-Object { $_.action -eq 'EDIT_FILE' -and $_.resourceId -eq $file.id }).Count -eq 2) 'Successful edits create EDIT_FILE audit entries'
$explorer=Call-Demo $ownerHeaders "/explorer?folderId=$($folder.id)"
$current=$explorer.items | Where-Object id -eq $file.id
Assert-Demo ($current.name -eq $file.name -and $current.ownerId -eq $file.ownerId -and $current.parentFolderId -eq $file.parentFolderId) 'Saving preserves name, owner, folder and permissions'
$curlCommand=Get-Command curl -CommandType Application -ErrorAction Stop
$cancelName='cancelled-upload.log'
$filePart='file=@' + (Join-Path $fixtureDir 'large.log') + ';filename=' + $cancelName
& $curlCommand.Source --silent --max-time 1 --limit-rate 128k -H "Authorization: Bearer $($finance.token)" -F "parentFolderId=$($folder.id)" -F $filePart "$ApiUrl/files/upload" | Out-Null
Assert-Demo ($LASTEXITCODE -eq 28) 'Real throttled HTTP upload is cancelled mid-transfer'
$afterCancel=Call-Demo $ownerHeaders "/explorer?folderId=$($folder.id)"
Assert-Demo (@($afterCancel.items | Where-Object name -eq $cancelName).Count -eq 0) 'Cancelled upload publishes no file metadata'
$afterLogs=Call-Demo $ownerHeaders '/audit'
Assert-Demo (@($afterLogs | Where-Object { $_.action -eq 'UPLOAD_FILE' -and $_.details -like "*$cancelName*" }).Count -eq 0) 'Cancelled upload creates no UPLOAD_FILE audit entry'
$manifest=@{folderId=$folder.id;folderName=$folder.name;files=$files;checks=$checks}
$manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $taskRoot '.cache/text-upload-live.json') -Encoding utf8
Write-Host "$checks live checks passed. Demo folder: $($folder.name)"
