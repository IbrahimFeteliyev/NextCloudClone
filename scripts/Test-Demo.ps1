param([string]$Api = 'http://localhost:5050/api', [string]$Password = 'Demo123!')
$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSVersion.Major -lt 7) { throw 'Run this script with PowerShell 7 (pwsh).' }
function Login-Demo([string]$Email) {
    Invoke-RestMethod -Method Post -Uri "$Api/auth/login" -ContentType 'application/json' -Body (@{ email = $Email; password = $Password } | ConvertTo-Json)
}
function Api-Json([string]$Method, [string]$Path, [string]$Token, $Body = $null) {
    $taskArgs = @{ Method = $Method; Uri = "$Api$Path"; Headers = @{ Authorization = "Bearer $Token" } }
    if ($null -ne $Body) { $taskArgs.ContentType = 'application/json'; $taskArgs.Body = $Body | ConvertTo-Json }
    Invoke-RestMethod @taskArgs
}
function Assert-Demo([bool]$Condition, [string]$Message) { if (!$Condition) { throw "FAILED: $Message" }; Write-Output "PASS: $Message" }
function Assert-Forbidden([scriptblock]$Request, [string]$Message) {
    try { & $Request | Out-Null; throw "FAILED: $Message (request was allowed)" }
    catch { if ([int]$_.Exception.Response.StatusCode -ne 403) { throw }; Write-Output "PASS: $Message" }
}

$finance = Login-Demo 'finance@demo.local'
$manager = Login-Demo 'manager@demo.local'
Assert-Demo ($finance.token.Length -gt 0 -and $manager.token.Length -gt 0) 'Both users authenticate with JWT'
$root = Api-Json Get '/explorer?view=files' $finance.token
$folderName = 'Finance demo ' + [DateTime]::Now.ToString('yyyyMMdd-HHmmss')
$folder = Api-Json Post '/folders' $finance.token @{ name = $folderName; parentFolderId = $root.folderId }
$budget = Api-Json Post '/folders' $finance.token @{ name = 'Budget'; parentFolderId = $folder.id }
Assert-Demo ($budget.parentFolderId -eq $folder.id) 'Nested Budget folder is created'
$fixture = Join-Path $PSScriptRoot 'fixtures/Budget_2027.xlsx'
$uploaded = Invoke-RestMethod -Method Post -Uri "$Api/files/upload" -Headers @{ Authorization = "Bearer $($finance.token)" } -Form @{ parentFolderId = $budget.id; file = Get-Item -LiteralPath $fixture }
Assert-Demo ($uploaded.name -eq 'Budget_2027.xlsx') 'Spreadsheet binary is uploaded to MinIO'
Api-Json Put "/resources/folder/$($folder.id)/shares" $finance.token @{ userId = $manager.user.id; permissions = 1 } | Out-Null
$manager = Login-Demo 'manager@demo.local'
$shared = Api-Json Get '/explorer?view=shared' $manager.token
Assert-Demo ([bool]($shared.items | Where-Object id -EQ $folder.id)) 'Manager sees Finance in Shared With Me'
$visible = Api-Json Get "/explorer?view=shared&folderId=$($budget.id)" $manager.token
Assert-Demo ($visible.permissions -eq 1 -and [bool]($visible.items | Where-Object id -EQ $uploaded.id)) 'READ access inherits through nested folders to the file'
$download = Invoke-WebRequest -Uri "$Api/files/$($uploaded.id)/download" -Headers @{ Authorization = "Bearer $($manager.token)" }
Assert-Demo ($download.RawContentLength -eq (Get-Item -LiteralPath $fixture).Length) 'Manager downloads the stored spreadsheet'
Assert-Forbidden { Api-Json Patch "/resources/file/$($uploaded.id)/name" $manager.token @{ name = 'Changed.xlsx' } } 'READ manager cannot rename'
Assert-Forbidden { Api-Json Delete "/resources/file/$($uploaded.id)" $manager.token } 'READ manager cannot delete'
Assert-Forbidden { Invoke-RestMethod -Method Post -Uri "$Api/files/upload" -Headers @{ Authorization = "Bearer $($manager.token)" } -Form @{ parentFolderId = $folder.id; file = Get-Item -LiteralPath $fixture } } 'READ manager cannot upload'
$finance = Login-Demo 'finance@demo.local'
Api-Json Put "/resources/folder/$($folder.id)/shares" $finance.token @{ userId = $manager.user.id; permissions = 3 } | Out-Null
$manager = Login-Demo 'manager@demo.local'
$managerUpload = Invoke-RestMethod -Method Post -Uri "$Api/files/upload" -Headers @{ Authorization = "Bearer $($manager.token)" } -Form @{ parentFolderId = $folder.id; file = Get-Item -LiteralPath $fixture }
Assert-Demo ($managerUpload.ownerId -eq $finance.user.id -and $managerUpload.permissions -eq 3) 'READ + WRITE manager can upload; workspace ownership remains consistent'
$logs = Api-Json Get '/audit' $finance.token
foreach ($taskAction in @('CREATE_FOLDER','UPLOAD_FILE','DOWNLOAD_FILE','SHARE_FOLDER','CHANGE_PERMISSION')) {
    Assert-Demo ([bool]($logs | Where-Object { $_.action -eq $taskAction -and $_.resourceId -in @($folder.id, $budget.id, $uploaded.id, $managerUpload.id) })) "Audit contains $taskAction"
}
Write-Output "Demo scenario passed. Test documents remain in '$folderName' for inspection."
