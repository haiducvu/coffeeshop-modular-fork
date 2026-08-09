# syntax=docker/dockerfile:1

FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /source

COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/CoffeeShop.Api/CoffeeShop.Api.csproj src/CoffeeShop.Api/
COPY src/CoffeeShop.Application/CoffeeShop.Application.csproj src/CoffeeShop.Application/
COPY src/CoffeeShop.Domain/CoffeeShop.Domain.csproj src/CoffeeShop.Domain/
COPY src/CoffeeShop.Infrastructure/CoffeeShop.Infrastructure.csproj src/CoffeeShop.Infrastructure/
RUN dotnet restore src/CoffeeShop.Api/CoffeeShop.Api.csproj

COPY src/CoffeeShop.Api/ src/CoffeeShop.Api/
COPY src/CoffeeShop.Application/ src/CoffeeShop.Application/
COPY src/CoffeeShop.Domain/ src/CoffeeShop.Domain/
COPY src/CoffeeShop.Infrastructure/ src/CoffeeShop.Infrastructure/
RUN dotnet publish src/CoffeeShop.Api/CoffeeShop.Api.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS final
RUN apk add --no-cache curl
WORKDIR /app
COPY --from=build /app/publish .
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "CoffeeShop.Api.dll"]
