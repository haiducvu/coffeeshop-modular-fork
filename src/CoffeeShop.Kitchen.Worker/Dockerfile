# syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /source
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/CoffeeShop.Kitchen.Worker/CoffeeShop.Kitchen.Worker.csproj src/CoffeeShop.Kitchen.Worker/
COPY src/CoffeeShop.Contracts/CoffeeShop.Contracts.csproj src/CoffeeShop.Contracts/
COPY src/CoffeeShop.IntegrationContracts/CoffeeShop.IntegrationContracts.csproj src/CoffeeShop.IntegrationContracts/
COPY src/CoffeeShop.Messaging.Abstractions/CoffeeShop.Messaging.Abstractions.csproj src/CoffeeShop.Messaging.Abstractions/
COPY src/CoffeeShop.Messaging.Kafka/CoffeeShop.Messaging.Kafka.csproj src/CoffeeShop.Messaging.Kafka/
COPY src/CoffeeShop.Modules.Kitchen/CoffeeShop.Modules.Kitchen.csproj src/CoffeeShop.Modules.Kitchen/
COPY src/CoffeeShop.SharedKernel/CoffeeShop.SharedKernel.csproj src/CoffeeShop.SharedKernel/
RUN dotnet restore src/CoffeeShop.Kitchen.Worker/CoffeeShop.Kitchen.Worker.csproj
COPY src/CoffeeShop.Kitchen.Worker/ src/CoffeeShop.Kitchen.Worker/
COPY src/CoffeeShop.Contracts/ src/CoffeeShop.Contracts/
COPY src/CoffeeShop.IntegrationContracts/ src/CoffeeShop.IntegrationContracts/
COPY src/CoffeeShop.Messaging.Abstractions/ src/CoffeeShop.Messaging.Abstractions/
COPY src/CoffeeShop.Messaging.Kafka/ src/CoffeeShop.Messaging.Kafka/
COPY src/CoffeeShop.Modules.Kitchen/ src/CoffeeShop.Modules.Kitchen/
COPY src/CoffeeShop.SharedKernel/ src/CoffeeShop.SharedKernel/
RUN dotnet publish src/CoffeeShop.Kitchen.Worker/CoffeeShop.Kitchen.Worker.csproj \
    --configuration Release --no-restore --output /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/runtime:10.0-alpine AS final
RUN apk add --no-cache krb5-libs
WORKDIR /app
COPY --from=build /app/publish .
USER $APP_UID
ENTRYPOINT ["dotnet", "CoffeeShop.Kitchen.Worker.dll"]
