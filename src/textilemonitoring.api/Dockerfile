
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY ["shared/TextileMonitoring.Contracts/TextileMonitoring.Contracts.csproj", "shared/TextileMonitoring.Contracts/"]
COPY ["shared/TextileMonitoring.Data/TextileMonitoring.Data.csproj", "shared/TextileMonitoring.Data/"]
COPY ["src/textilemonitoring.api/textilemonitoring.api.csproj", "src/textilemonitoring.api/"]

RUN dotnet restore "src/textilemonitoring.api/textilemonitoring.api.csproj"

COPY shared/ shared/
COPY src/textilemonitoring.api/ src/textilemonitoring.api/

WORKDIR "/src/src/textilemonitoring.api"
RUN dotnet publish "textilemonitoring.api.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

EXPOSE 5000

HEALTHCHECK --interval=15s --timeout=5s --retries=5 \
    CMD ["curl", "-f", "http://localhost:5000/healthz"]

ENTRYPOINT ["dotnet", "textilemonitoring.api.dll"]
