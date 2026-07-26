FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY ["VehiclePermitSystemWeb.csproj", "./"]
RUN dotnet restore "VehiclePermitSystemWeb.csproj"

COPY . .
RUN dotnet publish "VehiclePermitSystemWeb.csproj" \
    --configuration Release \
    --no-restore \
    --property:UseAppHost=false \
    --output /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app

ENV ASPNETCORE_URLS=http://0.0.0.0:10000 \
    DOTNET_EnableDiagnostics=0 \
    VehiclePermitSystemWeb__StorageRoot=/data

EXPOSE 10000

COPY --from=build /app/publish .
RUN mkdir -p /data && chown -R $APP_UID:$APP_UID /data
USER $APP_UID

ENTRYPOINT ["dotnet", "VehiclePermitSystemWeb.dll"]
