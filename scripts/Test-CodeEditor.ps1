param([string]$ApiUrl = 'http://127.0.0.1:5050/api')
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot -Parent
$fixtureDir = Join-Path $taskRoot '.cache/code-editor-fixtures'
New-Item -ItemType Directory -Force -Path $fixtureDir | Out-Null
$checks=0
function Check-Code($condition, $message) { if (!$condition) { throw $message }; $script:checks++; Write-Host "PASS $message" }
function Login-Code($email) { Invoke-RestMethod "$ApiUrl/auth/login" -Method Post -ContentType application/json -Body (@{email=$email;password='Demo123!'} | ConvertTo-Json) }
function Call-Code($headers,$path,$method='GET',$body=$null) {
    $args=@{Uri="$ApiUrl$path";Headers=$headers;Method=$method}
    if ($null -ne $body) { $args.ContentType='application/json';$args.Body=$body | ConvertTo-Json -Compress -Depth 8 }
    Invoke-RestMethod @args
}
function Denied-Code($headers,$id,$status,$version=1) {
    try { Call-Code $headers "/files/$id/content" 'PUT' @{content='attempted overwrite';expectedVersion=$version} | Out-Null; throw 'Expected denial' }
    catch { Check-Code ($_.Exception.Response.StatusCode.value__ -eq $status) "HTTP $status on text save" }
}
$owner=Login-Code 'finance@demo.local';$reader=Login-Code 'manager@demo.local'
$ownerHeaders=@{Authorization="Bearer $($owner.token)"};$readerHeaders=@{Authorization="Bearer $($reader.token)"}
$rootExplorer=Call-Code $ownerHeaders '/explorer'
$folder=Call-Code $ownerHeaders '/folders' 'POST' @{parentFolderId=$rootExplorer.folderId;name="Code editor demo $(Get-Date -Format yyyyMMdd-HHmmss)"}
Call-Code $ownerHeaders "/resources/folder/$($folder.id)/shares" 'PUT' @{userId=$reader.user.id;permissions=1} | Out-Null
$sources=[ordered]@{
    'Welcome to Atlas.txt'="Welcome to Atlas`r`nAtlas documents. Atlas permissions. Atlas teamwork.`r`nFind Atlas to test the search panel.`r`n"
    'application.log'="INFO Atlas started`nINFO Atlas ready`nWARN retry requested`n"
    'config.json'='{"workspace":"Atlas","enabled":true,"roles":["reader","editor"],"retries":3}'
    'settings.xml'='<?xml version="1.0"?><settings><workspace>Atlas</workspace><upload enabled="true"/></settings>'
    'README.md'="# Atlas workspace`n`n**One place** for your team.`n`n| Action | Permission |`n| --- | --- |`n| Search | READ |`n| Save | WRITE |`n`n<script>ignored raw HTML</script>`n"
    'report.sql'="SELECT Name, UpdatedAt FROM Documents`nWHERE OwnerId = @owner ORDER BY UpdatedAt DESC;"
    'app.js'='export function welcome(user) { return `Hello ${user.name}`; }'
    'app.ts'='type User = { name: string }; const welcome = (user: User): string => `Hello ${user.name}`;'
    'styles.css'='.card { display: flex; color: #256f50; gap: 12px; }'
    'index.html'='<!doctype html><html><body><h1>Uploaded source only</h1><script>alert("must never execute")</script></body></html>'
    'Program.cs'='public static class Program { public static void Main() { System.Console.WriteLine("Atlas"); } }'
    'script.py'="def welcome(name):`n    return f'Hello {name}'`n"
}
$registry=Get-Content (Join-Path $taskRoot 'shared/text-file-types.json') -Raw | ConvertFrom-Json
$files=@{}
foreach ($entry in $sources.GetEnumerator()) {
    $name=$entry.Key;$source=$entry.Value;$path=Join-Path $fixtureDir $name
    [IO.File]::WriteAllText($path,$source,[Text.UTF8Encoding]::new($false))
    $file=Invoke-RestMethod "$ApiUrl/files/upload" -Headers $ownerHeaders -Method Post -Form @{parentFolderId=$folder.id;file=Get-Item -LiteralPath $path};$files[$name]=$file
    $extension=[IO.Path]::GetExtension($name).TrimStart('.');$type=$registry | Where-Object {$_.extensions -contains $extension}
    $ticket=Call-Code $readerHeaders "/files/$($file.id)/preview" 'POST'
    Check-Code ($ticket.contentType -eq $type.mime -and $ticket.permissions -eq 1) "${name}: canonical MIME and inherited READ"
    Denied-Code $readerHeaders $file.id 403
    $updated=$source+"`n";$saved=Call-Code $ownerHeaders "/files/$($file.id)/content" 'PUT' @{content=$updated;expectedVersion=1}
    Check-Code ($saved.version -eq 2 -and $saved.id -eq $file.id) "${name}: WRITE save preserves identity and advances version"
    $content=Invoke-WebRequest "$ApiUrl/files/$($file.id)/content" -Headers $readerHeaders
    Check-Code ([Text.Encoding]::UTF8.GetString($content.RawContentStream.ToArray()) -eq $updated) "${name}: saved bytes retrieved from MinIO"
    if ($extension -in @('html','xml')) { Check-Code ($content.Headers['Content-Security-Policy'] -like '*sandbox*') "${name}: inline response remains sandboxed" }
}
[IO.File]::WriteAllText((Join-Path $fixtureDir 'invalid.json'),'{"workspace":"Atlas","enabled":tru}',[Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllText((Join-Path $fixtureDir 'utf16.py'),'print("Read-only UTF-16")',[Text.Encoding]::Unicode)
[IO.File]::WriteAllText((Join-Path $fixtureDir 'unsupported.rtf'),'Unsupported RTF',[Text.UTF8Encoding]::new($false))
foreach ($name in @('invalid.json','utf16.py','unsupported.rtf')) { $files[$name]=Invoke-RestMethod "$ApiUrl/files/upload" -Headers $ownerHeaders -Method Post -Form @{parentFolderId=$folder.id;file=Get-Item -LiteralPath (Join-Path $fixtureDir $name)} }
Denied-Code $ownerHeaders $files['utf16.py'].id 415
Denied-Code $ownerHeaders $files['unsupported.rtf'].id 415
$jsonId=$files['config.json'].id
Denied-Code $ownerHeaders $jsonId 409 1
$old=Invoke-WebRequest "$ApiUrl/files/$jsonId/versions/1/download" -Headers $readerHeaders
Check-Code ([Text.Encoding]::UTF8.GetString($old.RawContentStream.ToArray()) -eq $sources['config.json']) 'Original JSON version preserved'
$logs=Call-Code $ownerHeaders '/audit'
$fileIds=@($files.Values | ForEach-Object id)
Check-Code (@($logs | Where-Object { $_.action -eq 'EDIT_FILE' -and $fileIds -contains $_.resourceId }).Count -eq $sources.Count) 'Each successful code edit is audited'
@{folderId=$folder.id;folderName=$folder.name;files=$files;checks=$checks} | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $taskRoot '.cache/code-editor-live.json') -Encoding utf8
Write-Host "$checks live code-editor checks passed. Demo folder: $($folder.name)"
