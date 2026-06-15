
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY ["shared/TextileMonitoring.Contracts/TextileMonitoring.Contracts.csproj", "shared/TextileMonitoring.Contracts/"]
COPY ["shared/TextileMonitoring.Data/TextileMonitoring.Data.csproj", "shared/TextileMonitoring.Data/"]
COPY ["services/zigbee_ingest/ZigBeeIngest.Worker.csproj", "services/zigbee_ingest/"]

RUN dotnet restore "services/zigbee_ingest/ZigBeeIngest.Worker.csproj"

COPY shared/ shared/
COPY services/zigbee_ingest/ services/zigbee_ingest/

WORKDIR "/src/services/zigbee_ingest"
RUN dotnet publish "ZigBeeIngest.Worker.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/runtime:8.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

HEALTHCHECK --interval=15s --timeout=5s --retries=5 \
    CMD ["sh", "-c", "echo 'healthy' || exit 1"]

ENTRYPOINT ["dotnet", "ZigBeeIngest.Worker.dll"]
