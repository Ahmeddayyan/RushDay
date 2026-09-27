# Multi-stage build: SDK image compiles, slim ASP.NET runtime image serves.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first so the layer is cached until a csproj changes.
COPY global.json Directory.Build.props ./
COPY src/RushDay.Domain/RushDay.Domain.csproj src/RushDay.Domain/
COPY src/RushDay.Infrastructure/RushDay.Infrastructure.csproj src/RushDay.Infrastructure/
COPY src/RushDay.Api/RushDay.Api.csproj src/RushDay.Api/
RUN dotnet restore src/RushDay.Api/RushDay.Api.csproj

COPY src/ src/
RUN dotnet publish src/RushDay.Api/RushDay.Api.csproj --configuration Release --no-restore --output /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "RushDay.Api.dll"]
