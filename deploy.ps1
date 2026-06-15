param(
    [string]$Command = "help",
    [string]$ServiceName = ""
)

function Show-Help {
    Write-Host "Textile Monitoring Deployment Script"
    Write-Host ""
    Write-Host "Usage: .\deploy.ps1 <command> [service-name]"
    Write-Host ""
    Write-Host "Available commands:"
    Write-Host "  build          Build all Docker images"
    Write-Host "  up             Start all services (daemon mode)"
    Write-Host "  down           Stop and remove all services"
    Write-Host "  logs           View service logs (optional: service name)"
    Write-Host "  test-outbreak  Enable outbreak simulation mode"
    Write-Host "  help           Show this help message"
    Write-Host ""
    Write-Host "Examples:"
    Write-Host "  .\deploy.ps1 build"
    Write-Host "  .\deploy.ps1 up"
    Write-Host "  .\deploy.ps1 logs api-gateway"
    Write-Host "  .\deploy.ps1 test-outbreak"
}

function Invoke-Build {
    Write-Host "Building Docker images..."
    docker compose build
}

function Invoke-Up {
    Write-Host "Starting all services..."
    docker compose up -d
}

function Invoke-Down {
    Write-Host "Stopping and removing all services..."
    docker compose down
}

function Invoke-Logs {
    param([string]$svc)
    if ($svc) {
        Write-Host "Viewing logs for service: $svc"
        docker compose logs -f $svc
    } else {
        Write-Host "Viewing logs for all services..."
        docker compose logs -f
    }
}

function Invoke-TestOutbreak {
    Write-Host "Enabling outbreak simulation mode..."
    $env:SIM_OUTBREAK = "true"
    Write-Host "SIM_OUTBREAK set to true"
    Write-Host "Restarting simulator service..."
    $env:SIM_OUTBREAK = "true"
    docker compose up -d --force-recreate zigbee-simulator
}

switch ($Command) {
    "build" { Invoke-Build }
    "up" { Invoke-Up }
    "down" { Invoke-Down }
    "logs" { Invoke-Logs -svc $ServiceName }
    "test-outbreak" { Invoke-TestOutbreak }
    "help" { Show-Help }
    default { Show-Help }
}
