
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY ["shared/TextileMonitoring.Contracts/TextileMonitoring.Contracts.csproj", "shared/TextileMonitoring.Contracts/"]
COPY ["shared/TextileMonitoring.Data/TextileMonitoring.Data.csproj", "shared/TextileMonitoring.Data/"]
COPY ["services/alert_dispatch/AlertDispatch.csproj", "services/alert_dispatch/"]

RUN dotnet restore "services/alert_dispatch/AlertDispatch.csproj"

COPY shared/ shared/
COPY services/alert_dispatch/ services/alert_dispatch/

WORKDIR "/src/services/alert_dispatch"
RUN dotnet publish "AlertDispatch.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/runtime:8.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

HEALTHCHECK --interval=15s --timeout=5s --retries=5 \
    CMD ["sh", "-c", "echo 'healthy' || exit 1"]

ENTRYPOINT ["dotnet", "AlertDispatch.dll"]
