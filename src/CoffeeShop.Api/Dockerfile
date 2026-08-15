# syntax=docker/dockerfile:1

FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /source

COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/CoffeeShop.Api/CoffeeShop.Api.csproj src/CoffeeShop.Api/
COPY src/CoffeeShop.Contracts/CoffeeShop.Contracts.csproj src/CoffeeShop.Contracts/
COPY src/CoffeeShop.Modules.Barista/CoffeeShop.Modules.Barista.csproj src/CoffeeShop.Modules.Barista/
COPY src/CoffeeShop.Modules.Counter/CoffeeShop.Modules.Counter.csproj src/CoffeeShop.Modules.Counter/
COPY src/CoffeeShop.Modules.Kitchen/CoffeeShop.Modules.Kitchen.csproj src/CoffeeShop.Modules.Kitchen/
COPY src/CoffeeShop.SharedKernel/CoffeeShop.SharedKernel.csproj src/CoffeeShop.SharedKernel/
RUN dotnet restore src/CoffeeShop.Api/CoffeeShop.Api.csproj

COPY src/CoffeeShop.Api/ src/CoffeeShop.Api/
COPY src/CoffeeShop.Contracts/ src/CoffeeShop.Contracts/
COPY src/CoffeeShop.Modules.Barista/ src/CoffeeShop.Modules.Barista/
COPY src/CoffeeShop.Modules.Counter/ src/CoffeeShop.Modules.Counter/
COPY src/CoffeeShop.Modules.Kitchen/ src/CoffeeShop.Modules.Kitchen/
COPY src/CoffeeShop.SharedKernel/ src/CoffeeShop.SharedKernel/
RUN dotnet publish src/CoffeeShop.Api/CoffeeShop.Api.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS final
RUN apk add --no-cache curl krb5-libs
WORKDIR /app
COPY --from=build /app/publish .
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "CoffeeShop.Api.dll"]
