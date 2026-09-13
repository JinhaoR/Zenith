[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$null = Get-Command dotnet -ErrorAction Stop
function Invoke-DotnetCheck {
    param([string[]] $Arguments, [string] $LogPath)
    # Windows PowerShell turns native stderr into errors when redirected. Preserve
    # the failing process result instead of aborting before writing the summary.
    $ErrorActionPreference = 'Continue'
    $PSNativeCommandUseErrorActionPreference = $false
    & dotnet @Arguments *> $LogPath
    return $LASTEXITCODE
}
$repository = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$previousWebView = $env:ZENITH_WEBVIEW_TESTS
$previousStrict = $env:ZENITH_STRICT_CONNECTION_TESTS
$failed = $true
Push-Location $repository
try {
    # Only generated, synthetic-test evidence. Never collect browser profiles,
    # environment dumps, network bodies, real-account screenshots or credentials.
    $runName = [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8)
    $evidence = New-Item -ItemType Directory -Path (Join-Path $repository "bin/security-checks/$runName")
    $buildExitCode = Invoke-DotnetCheck -Arguments @('build', 'Zenith.slnx', '--configuration', $Configuration, '--no-restore') -LogPath (Join-Path $evidence.FullName 'build.log')
    if ($buildExitCode -ne 0) { throw "Build failed. See $($evidence.FullName)/build.log; restore dependencies first if needed." }

    $env:ZENITH_WEBVIEW_TESTS = '1'
    $env:ZENITH_STRICT_CONNECTION_TESTS = '0'
    $regressionExitCode = Invoke-DotnetCheck -Arguments @('test', 'Zenith.slnx', '--configuration', $Configuration, '--no-build', '--no-restore', '--logger', 'console;verbosity=normal') -LogPath (Join-Path $evidence.FullName 'regressions.log')

    # Run separately so a known coverage failure cannot prevent other regressions.
    $env:ZENITH_STRICT_CONNECTION_TESTS = '1'
    $connectionExitCode = Invoke-DotnetCheck -Arguments @('test', 'tests/Zenith.App.Tests/Zenith.App.Tests.csproj', '--configuration', $Configuration, '--no-build', '--no-restore', '--filter', 'FullyQualifiedName~WebViewNavigationTests', '--logger', 'console;verbosity=normal') -LogPath (Join-Path $evidence.FullName 'connection-gate.log')
    $failed = $regressionExitCode -ne 0 -or $connectionExitCode -ne 0
    $assembly = Join-Path $repository "src/Zenith.App/bin/$Configuration/net10.0-windows/Zenith.App.dll"
    [ordered]@{
        RecordedUtc = [DateTime]::UtcNow.ToString('O')
        Commit = (& git rev-parse HEAD)
        DirtyWorktree = [bool](& git status --porcelain)
        Windows = [Environment]::OSVersion.VersionString
        DotnetSdk = (& dotnet --version)
        Configuration = $Configuration
        AppAssemblySha256 = (Get-FileHash -LiteralPath $assembly -Algorithm SHA256).Hash
        RegressionExitCode = $regressionExitCode
        ConnectionGateExitCode = $connectionExitCode
        AutomatedGatePassed = -not $failed
        ProviderMfa = 'Not established by automated tests; see provider-mfa-testing.md'
        IndependentReview = 'Not established by automated tests; separate reviewer required'
        PrimaryAccountApproval = $false
    } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $evidence.FullName 'summary.json') -Encoding UTF8
    Write-Host "Evidence: $($evidence.FullName)"
    Write-Host "Regression exit: $regressionExitCode; strict connection gate exit: $connectionExitCode"
    Write-Host 'Provider MFA and independent review are separate release gates, even if all automated checks pass.'
}
finally {
    $env:ZENITH_WEBVIEW_TESTS = $previousWebView
    $env:ZENITH_STRICT_CONNECTION_TESTS = $previousStrict
    Pop-Location
}
if ($failed) { exit 1 }
