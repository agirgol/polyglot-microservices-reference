# Built from the repository root so the shared Directory.*.props and NuGet.config
# are in scope:
#     docker build -f services/orders/Dockerfile .
#
# Restore is its own layer, keyed on the project and version files alone. A code
# change then rebuilds without re-downloading the world, which is the whole
# reason not to copy the source in first.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Directory.Build.props Directory.Packages.props NuGet.config ./
COPY services/orders/src/Orders/Orders.csproj services/orders/src/Orders/
RUN dotnet restore services/orders/src/Orders/Orders.csproj

COPY services/orders/src/Orders/ services/orders/src/Orders/
RUN dotnet publish services/orders/src/Orders/Orders.csproj \
        -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS runtime
WORKDIR /app

# Not root. The service reads configuration and writes nothing; there is no
# reason for it to be able to.
RUN adduser --disabled-password --no-create-home --uid 64198 orders
USER orders

COPY --from=build /app .

EXPOSE 8080
ENTRYPOINT ["dotnet", "Orders.dll"]
