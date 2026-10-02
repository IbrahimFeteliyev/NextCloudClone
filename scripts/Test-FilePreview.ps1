param([string]$ApiUrl = 'http://localhost:5050/api', [Parameter(Mandatory)][string]$FixtureDirectory, [Guid]$ExistingFolderId = [Guid]::Empty)
$ErrorActionPreference = 'Stop'
function Login([string]$email) { (Invoke-RestMethod "$ApiUrl/auth/login" -Method Post -ContentType 'application/json' -Body (@{ email=$email; password='Demo123!' } | ConvertTo-Json)).token }
$ownerToken = Login 'finance@demo.local'; $readerToken = Login 'manager@demo.local'; $ownerHeaders=@{Authorization="Bearer $ownerToken"}; $readerHeaders=@{Authorization="Bearer $readerToken"}
$unsharedToken = Login 'employee@demo.local'
$root = Invoke-RestMethod "$ApiUrl/explorer" -Headers $ownerHeaders
if ($ExistingFolderId -ne [Guid]::Empty) {
    $folder = $root.items | Where-Object id -eq $ExistingFolderId
    $files = (Invoke-RestMethod "$ApiUrl/explorer?folderId=$ExistingFolderId" -Headers $ownerHeaders).items
} else {
    $folder = Invoke-RestMethod "$ApiUrl/folders" -Headers $ownerHeaders -Method Post -ContentType 'application/json' -Body (@{name='Preview demo '+(Get-Date -Format 'yyyyMMdd-HHmmss');parentFolderId=$root.folderId}|ConvertTo-Json)
    $files = @(); foreach ($fixture in Get-ChildItem -LiteralPath $FixtureDirectory -File) {
        $files += Invoke-RestMethod "$ApiUrl/files/upload" -Headers $ownerHeaders -Method Post -Form @{parentFolderId=$folder.id;file=$fixture}
    }
}
$file = $files | Where-Object name -eq 'demo.mp4' | Select-Object -First 1
if (!$file) { $file = $files | Select-Object -First 1 }
$checks=0
function Check($condition, [string]$label) { if (!$condition) {throw "FAIL: $label"}; $script:checks++; Write-Host "PASS: $label" }
function Http([string]$path, [string]$token='', [string]$range='') {
    $request=[System.Net.Http.HttpRequestMessage]::new([System.Net.Http.HttpMethod]::Get, "$ApiUrl$path")
    if($token){$request.Headers.Authorization=[System.Net.Http.Headers.AuthenticationHeaderValue]::new('Bearer',$token)}
    if($range){[void]$request.Headers.TryAddWithoutValidation('Range',$range)}
    $response=$client.SendAsync($request,[System.Net.Http.HttpCompletionOption]::ResponseHeadersRead).GetAwaiter().GetResult()
    try { $bytes=$response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult(); return @{status=[int]$response.StatusCode;bytes=$bytes;headers=$response.Headers;contentHeaders=$response.Content.Headers} } finally {$request.Dispose();$response.Dispose()}
}
$client=[System.Net.Http.HttpClient]::new()
try {
    Check ((Http "/files/$($file.id)/content").status -eq 401) 'Anonymous preview rejected'
    Check ((Http "/files/$($file.id)/content" $unsharedToken).status -eq 403) 'Unshared user rejected'
    $users=Invoke-RestMethod "$ApiUrl/auth/users" -Headers $ownerHeaders
    $reader=$users | Where-Object email -eq 'manager@demo.local'
    Invoke-RestMethod "$ApiUrl/resources/folder/$($folder.id)/shares" -Headers $ownerHeaders -Method Put -ContentType 'application/json' -Body (@{userId=$reader.id;permissions=1}|ConvertTo-Json) | Out-Null
    $ticket=Invoke-RestMethod "$ApiUrl/files/$($file.id)/preview" -Headers $readerHeaders -Method Post
    $scoped=Http $ticket.contentPath '' 'bytes=64-127'
    Check ($scoped.status -eq 206 -and $scoped.bytes.Length -eq 64) 'Scoped ticket streams requested bytes'
    $full=Http "/files/$($file.id)/content" $readerToken
    Check ($full.status -eq 200 -and $full.contentHeaders.ContentDisposition.DispositionType -eq 'inline') 'Inherited READ previews inline'
    Check ([Convert]::ToBase64String($scoped.bytes) -eq [Convert]::ToBase64String($full.bytes[64..127])) 'MinIO byte range matches original content'
    $suffix=Http "/files/$($file.id)/content" $readerToken 'bytes=-32'
    Check ($suffix.status -eq 206 -and $suffix.bytes.Length -eq 32) 'Suffix byte ranges supported'
    Check ((Http "/files/$($file.id)/content" $readerToken "bytes=$($file.size)-").status -eq 416) 'Out-of-bounds range rejected'
    $download=Http "/files/$($file.id)/download" $readerToken
    Check ($download.status -eq 200 -and $download.contentHeaders.ContentDisposition.DispositionType -eq 'attachment') 'Explicit Download returns attachment'
    foreach ($uploaded in $files) {
        $preview=Invoke-RestMethod "$ApiUrl/files/$($uploaded.id)/preview" -Headers $readerHeaders -Method Post
        Check ($preview.id -eq $uploaded.id -and $preview.size -eq $uploaded.size) "Preview metadata: $($uploaded.name)"
    }
    Invoke-RestMethod "$ApiUrl/resources/file/$($file.id)/shares" -Headers $ownerHeaders -Method Put -ContentType 'application/json' -Body (@{userId=$reader.id;permissions=0}|ConvertTo-Json) | Out-Null
    Check ((Http $ticket.contentPath).status -eq 403) 'Direct deny invalidates previously issued media access'
    Check ((Http "/files/$($file.id)/download" $readerToken).status -eq 403) 'Direct deny prevents explicit download'
    Invoke-RestMethod "$ApiUrl/resources/file/$($file.id)/shares/$($reader.id)" -Headers $ownerHeaders -Method Delete | Out-Null
    Check ((Http $ticket.contentPath '' 'bytes=0-15').status -eq 206) 'Removing override restores inherited READ'
    Check ((Http "/files/$([Guid]::NewGuid())/content" $ownerToken).status -eq 404) 'Missing file handled'
    $output=Join-Path (Split-Path $PSScriptRoot -Parent) '.cache/preview-live.json'
    @{folderId=$folder.id;folderName=$folder.name;files=$files;checks=$checks}|ConvertTo-Json -Depth 8|Set-Content -LiteralPath $output -Encoding utf8
    Write-Host "$checks live preview checks passed. Demo folder: $($folder.name)"
} finally { $client.Dispose() }
