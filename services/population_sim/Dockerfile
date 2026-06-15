
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY ["shared/TextileMonitoring.Contracts/TextileMonitoring.Contracts.csproj", "shared/TextileMonitoring.Contracts/"]
COPY ["shared/TextileMonitoring.Data/TextileMonitoring.Data.csproj", "shared/TextileMonitoring.Data/"]
COPY ["services/population_sim/PopulationSim.Service.csproj", "services/population_sim/"]

RUN dotnet restore "services/population_sim/PopulationSim.Service.csproj"

COPY shared/ shared/
COPY services/population_sim/ services/population_sim/

WORKDIR "/src/services/population_sim"
RUN dotnet publish "PopulationSim.Service.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/runtime:8.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

HEALTHCHECK --interval=15s --timeout=5s --retries=5 \
    CMD ["sh", "-c", "echo 'healthy' || exit 1"]

ENTRYPOINT ["dotnet", "PopulationSim.Service.dll"]
