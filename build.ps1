[CmdletBinding(PositionalBinding=$false)]
param(
    [bool] $CreatePackages,
    [switch] $StartServers,
    [bool] $RunTests = $true,
    [string] $PullRequestNumber,
    [switch] $NetCoreOnlyTests
)

Write-Host "Run Parameters:" -ForegroundColor Cyan
Write-Host "  CreatePackages: $CreatePackages"
Write-Host "  RunTests: $RunTests"
Write-Host "  dotnet --version:" (dotnet --version)

$packageOutputFolder = "$PSScriptRoot\.nupkgs"

if ($PullRequestNumber) {
    Write-Host "Building for a pull request (#$PullRequestNumber), skipping packaging." -ForegroundColor Yellow
    $CreatePackages = $false
}

Write-Host "Building all projects (Build.csproj traversal)..." -ForegroundColor "Magenta"
dotnet build ".\Build.csproj" -c Release /p:CI=true
Write-Host "Done building." -ForegroundColor "Green"

if ($RunTests) {
    if ($StartServers) {
        Write-Host "Starting all servers for testing: $project (all frameworks)" -ForegroundColor "Magenta"
        & .\RedisConfigs\start-all.cmd
        Write-Host "Servers Started." -ForegroundColor "Green"
    }
    Write-Host "Running tests: Build.csproj traversal (all frameworks)" -ForegroundColor "Magenta"
    if ($NetCoreOnlyTests) {
        dotnet test ".\Build.csproj" -c Release -f net8.0 --no-build --logger trx
    } else {
        dotnet test ".\Build.csproj" -c Release --no-build --logger trx
    }
    if ($LastExitCode -ne 0) {
        Write-Host "Error with tests, aborting build." -Foreground "Red"
        Exit 1
    }
    Write-Host "Tests passed!" -ForegroundColor "Green"
}

if ($CreatePackages) {
    New-Item -ItemType Directory -Path $packageOutputFolder -Force | Out-Null
    Write-Host "Clearing existing $packageOutputFolder..." -NoNewline
    Get-ChildItem $packageOutputFolder | Remove-Item
    Write-Host "done." -ForegroundColor "Green"

    Write-Host "Building all packages" -ForegroundColor "Green"
    dotnet pack ".\Build.csproj" --no-build -c Release /p:PackageOutputPath=$packageOutputFolder /p:CI=true
}

Write-Host "Done."