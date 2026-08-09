# syntax=docker/dockerfile:1

FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /source

COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/CoffeeShop.DataGen/CoffeeShop.DataGen.csproj src/CoffeeShop.DataGen/
RUN dotnet restore src/CoffeeShop.DataGen/CoffeeShop.DataGen.csproj

COPY src/CoffeeShop.DataGen/ src/CoffeeShop.DataGen/
RUN dotnet publish src/CoffeeShop.DataGen/CoffeeShop.DataGen.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/runtime:10.0-alpine AS final
WORKDIR /app
COPY --from=build /app/publish .
USER $APP_UID
ENTRYPOINT ["dotnet", "CoffeeShop.DataGen.dll"]
