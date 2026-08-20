# Built from the repository root:
#     docker build -f services/gateway/Dockerfile .

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Directory.Build.props Directory.Packages.props NuGet.config ./
COPY services/gateway/src/Gateway/Gateway.csproj services/gateway/src/Gateway/
RUN dotnet restore services/gateway/src/Gateway/Gateway.csproj

COPY services/gateway/src/Gateway/ services/gateway/src/Gateway/
RUN dotnet publish services/gateway/src/Gateway/Gateway.csproj \
        -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS runtime
WORKDIR /app

RUN adduser --disabled-password --no-create-home --uid 64198 gateway
USER gateway

COPY --from=build /app .

EXPOSE 8080
ENTRYPOINT ["dotnet", "Gateway.dll"]
