#requires -Version 5.1
param([string]$PackageRoot = (Split-Path -Parent $PSScriptRoot))
$ErrorActionPreference = 'Stop'
$source = Join-Path $PackageRoot 'Companion~/wdmcp-companion.ps1'
$tokens = $null
$errors = $null
$ast = [System.Management.Automation.Language.Parser]::ParseFile($source, [ref]$tokens, [ref]$errors)
if ($errors.Count -gt 0) { throw ($errors | Out-String) }
$functions = $ast.FindAll({ param($node) $node -is [System.Management.Automation.Language.FunctionDefinitionAst] }, $false)
foreach ($function in $functions) { . ([ScriptBlock]::Create($function.Extent.Text)) }
$Utf8NoBom = [System.Text.UTF8Encoding]::new($false)
$folder = Join-Path ([System.IO.Path]::GetTempPath()) ('wasim-mcp-tests-' + [Guid]::NewGuid().ToString('N'))
[System.IO.Directory]::CreateDirectory($folder) | Out-Null
$Script:Config = [pscustomobject]@{
    requestsPath = (Join-Path $folder 'Requests')
    responsesPath = (Join-Path $folder 'Responses')
    unityStatusPath = (Join-Path $folder 'unity-status.json')
    companionStatusPath = (Join-Path $folder 'companion-status.json')
    catalogPath = (Join-Path $folder 'catalog.json')
    companionLogPath = (Join-Path $folder 'companion.log')
    requestTimeoutSeconds = 2
    companionVersion = '0.5.2'
    capabilityToken = 'local-test'
    port = 38421
}
[System.IO.Directory]::CreateDirectory($Config.requestsPath) | Out-Null
[System.IO.Directory]::CreateDirectory($Config.responsesPath) | Out-Null
$Script:NgrokProcess = $null
$Script:OwnsNgrok = $false
$Script:PublicUrl = ''
$Script:NgrokState = 'Disabled'
$Script:LastError = ''
$Script:LastRequestUtc = ''
$Script:StartedUtc = [DateTime]::UtcNow.ToString('O')
$originalWriter = (Get-Item Function:Write-JsonAtomic).ScriptBlock
# Simulated Unity worker; real Invoke-UnityTool response serialization is exercised.
function Write-JsonAtomic([string]$Path, $Value) {
    & $originalWriter $Path $Value
    if ([System.IO.Path]::GetDirectoryName($Path) -eq $Config.requestsPath) {
        $response = [ordered]@{ id=$Value.id; success=$true; result=$Script:NextResult }
        if ($null -ne $Script:NextContent) { $response.mcpContent = $Script:NextContent }
        Write-TextAtomic (Join-Path $Config.responsesPath ($Value.id + '.json')) (ConvertTo-Json -InputObject $response -Depth 100 -Compress)
    }
}
function Assert-True([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
    Write-Host ('PASS ' + $Message)
}
try {
    Write-JsonAtomic $Config.unityStatusPath ([ordered]@{timestampUtc=[DateTime]::UtcNow.ToString('O')})
    Assert-True (Get-UnityAvailability) 'Fresh Unity heartbeat'
    $cases = @(
        [pscustomobject]@{value=@();expected='[]'},
        [pscustomobject]@{value=@([pscustomobject]@{id='one'});expected='[{"id":"one"}]'},
        [pscustomobject]@{value=@(1,2);expected='[1,2]'},
        [pscustomobject]@{value=$null;expected='null'}
    )
    foreach ($case in $cases) {
        $Script:NextResult = $case.value
        $result = Invoke-UnityTool 'unity_read_console' ([ordered]@{})
        Assert-True ($result.isError -eq $false -and $result.content[0].text -is [string]) 'MCP text is a string'
        Assert-True ($result.content[0].text -eq $case.expected) ('Preserve JSON shape ' + $case.expected)
    }
    $Script:NextContent = @([ordered]@{ type='image'; mimeType='image/jpeg'; data='ZGF0YQ==' })
    $imageResult = Invoke-UnityTool 'unity_capture_view' ([ordered]@{})
    Assert-True ($imageResult.isError -eq $false -and $imageResult.content[0].type -eq 'image') 'Native MCP image content is forwarded'
    $Script:NextContent = $null
    $path = Join-Path $folder 'rewrite.json'
    Write-TextAtomic $path '{"version":1}'
    Write-TextAtomic $path '{"version":2}'
    Assert-True ((Read-JsonFile $path).version -eq 2) 'Baseline file overwrite works without held reader'
    Write-Host 'Companion function tests passed. Use Documentation~/test-mcp.ps1 for a real Unity round trip.'
} finally { Remove-Item -LiteralPath $folder -Recurse -Force -ErrorAction SilentlyContinue }
