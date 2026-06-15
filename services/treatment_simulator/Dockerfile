FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY ["shared/TextileMonitoring.Contracts/TextileMonitoring.Contracts.csproj", "shared/TextileMonitoring.Contracts/"]
COPY ["shared/TextileMonitoring.Data/TextileMonitoring.Data.csproj", "shared/TextileMonitoring.Data/"]
COPY ["services/treatment_simulator/TreatmentSimulator.csproj", "services/treatment_simulator/"]

RUN dotnet restore "services/treatment_simulator/TreatmentSimulator.csproj"

COPY shared/ shared/
COPY services/treatment_simulator/ services/treatment_simulator/

WORKDIR "/src/services/treatment_simulator"
RUN dotnet publish "TreatmentSimulator.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/runtime:8.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

EXPOSE 9107

HEALTHCHECK --interval=15s --timeout=5s --retries=5 \
    CMD ["sh", "-c", "echo 'healthy' || exit 1"]

ENTRYPOINT ["dotnet", "TreatmentSimulator.Service.dll"]
