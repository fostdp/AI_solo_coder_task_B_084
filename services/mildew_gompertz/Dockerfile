
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY ["shared/TextileMonitoring.Contracts/TextileMonitoring.Contracts.csproj", "shared/TextileMonitoring.Contracts/"]
COPY ["shared/TextileMonitoring.Data/TextileMonitoring.Data.csproj", "shared/TextileMonitoring.Data/"]
COPY ["services/mildew_gompertz/TextileMonitoring.MildewGompertz.csproj", "services/mildew_gompertz/"]

RUN dotnet restore "services/mildew_gompertz/TextileMonitoring.MildewGompertz.csproj"

COPY shared/ shared/
COPY services/mildew_gompertz/ services/mildew_gompertz/

WORKDIR "/src/services/mildew_gompertz"
RUN dotnet publish "TextileMonitoring.MildewGompertz.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/runtime:8.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

HEALTHCHECK --interval=15s --timeout=5s --retries=5 \
    CMD ["sh", "-c", "echo 'healthy' || exit 1"]

ENTRYPOINT ["dotnet", "TextileMonitoring.MildewGompertz.dll"]
