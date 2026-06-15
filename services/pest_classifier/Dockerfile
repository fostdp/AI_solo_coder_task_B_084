
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY ["shared/TextileMonitoring.Contracts/TextileMonitoring.Contracts.csproj", "shared/TextileMonitoring.Contracts/"]
COPY ["shared/TextileMonitoring.Data/TextileMonitoring.Data.csproj", "shared/TextileMonitoring.Data/"]
COPY ["services/pest_classifier/PestClassifier.csproj", "services/pest_classifier/"]

RUN dotnet restore "services/pest_classifier/PestClassifier.csproj"

COPY shared/ shared/
COPY services/pest_classifier/ services/pest_classifier/

WORKDIR "/src/services/pest_classifier"
RUN dotnet publish "PestClassifier.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/runtime:8.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

HEALTHCHECK --interval=15s --timeout=5s --retries=5 \
    CMD ["sh", "-c", "echo 'healthy' || exit 1"]

EXPOSE 9105

ENTRYPOINT ["dotnet", "PestClassifier.Service.dll"]
