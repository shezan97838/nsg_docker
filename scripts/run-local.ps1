<#
.SYNOPSIS
  Runs the NSG Mobile API locally on Windows (dotnet run, Development environment) using .env.local.secrets.

.DESCRIPTION
  Loads the git-ignored .env.local.secrets into this process's environment variables, then starts the API.
  Environment variables override ASP.NET Core User Secrets, so the file is the single source of truth for local secrets.
  Windows only: the WINDOWS_Jwt__Secret value becomes Jwt__Secret; DOCKER_* values are never loaded. Only variable NAMES are printed, never values.
  Issuer/Audience come from appsettings.Development.json. Database:MigrateOnStartup=false comes from appsettings.Development.json.

.EXAMPLE
  .\scripts\run-local.ps1                 # http profile (http://localhost:5062)
  .\scripts\run-local.ps1 -Profile https
#>
[CmdletBinding()]
param(
    [ValidateSet('http', 'https')][string]$Profile = 'http',
    [string]$EnvFile
)
$ErrorActionPreference = 'Stop'

# Resolve the repository root from the script's own location (works in Windows PowerShell 5.1 and PowerShell 7, from any current directory).
# $PSScriptRoot is not reliable inside a param() default in Windows PowerShell 5.1, so it is resolved here instead.
$scriptDir = if ($PSScriptRoot) { $PSScriptRoot } else { Split-Path -Parent $MyInvocation.MyCommand.Path }
$repoRoot = (Resolve-Path -LiteralPath (Join-Path $scriptDir '..')).Path
if ([string]::IsNullOrWhiteSpace($EnvFile)) { $EnvFile = Join-Path $repoRoot '.env.local.secrets' }

if (-not (Test-Path -LiteralPath $EnvFile)) {
    throw "Secrets file not found: $EnvFile. Copy .env.local.secrets.example to .env.local.secrets and fill in real local values."
}

$vars = @{}
foreach ($line in Get-Content -LiteralPath $EnvFile) {
    if ($line -match '^\s*(#|$)') { continue }
    $name, $value = $line -split '=', 2
    if ($null -eq $value) { throw "Malformed line in $EnvFile (expected NAME=value): $name" }
    $vars[$name.Trim()] = $value
}

foreach ($required in 'LOCAL_DB_USER', 'LOCAL_DB_PASSWORD', 'LOCAL_DB_NAME', 'Encryption__Key', 'WINDOWS_Jwt__Secret') {
    if (-not $vars.ContainsKey($required) -or [string]::IsNullOrWhiteSpace($vars[$required]) -or $vars[$required].StartsWith('REPLACE_')) {
        throw "$required is missing or still a placeholder in $EnvFile."
    }
}

$set = [ordered]@{
    ASPNETCORE_ENVIRONMENT    = 'Development'
    ConnectionStrings__MySql  = "Server=127.0.0.1;Port=3306;Database=$($vars['LOCAL_DB_NAME']);Uid=$($vars['LOCAL_DB_USER']);Pwd=$($vars['LOCAL_DB_PASSWORD']);AllowUserVariables=True;SslMode=None;"
    Encryption__Key           = $vars['Encryption__Key']
    Jwt__Secret               = $vars['WINDOWS_Jwt__Secret']
}
foreach ($entry in $set.GetEnumerator()) {
    [Environment]::SetEnvironmentVariable($entry.Key, $entry.Value, 'Process')
}
Write-Host ("Loaded into process environment: " + ($set.Keys -join ', ') + "  (values not shown)")

$project = Join-Path $repoRoot 'src\NSG.Mobile.API\NSG.Mobile.API.csproj'
dotnet run --project $project --launch-profile $Profile
