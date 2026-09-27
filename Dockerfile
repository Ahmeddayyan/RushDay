# Multi-stage build: Node builds the SPA, the .NET SDK compiles the API with the SPA in its wwwroot,
# and the slim ASP.NET runtime image serves both from one process on one port.

# ---- Front end -------------------------------------------------------------------------------------
FROM node:24-alpine AS web
# Mirror the repo layout so vite.config.ts's relative outDir (../RushDay.Api/wwwroot) lands where expected.
WORKDIR /src/src/RushDay.Web

# Install first so the layer is cached until the lockfile changes.
COPY src/RushDay.Web/package.json src/RushDay.Web/package-lock.json ./
RUN npm ci --no-audit --no-fund

COPY src/RushDay.Web/ ./
RUN npm run build

# ---- API -------------------------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first so the layer is cached until a csproj changes.
COPY global.json Directory.Build.props ./
COPY src/RushDay.Domain/RushDay.Domain.csproj src/RushDay.Domain/
COPY src/RushDay.Infrastructure/RushDay.Infrastructure.csproj src/RushDay.Infrastructure/
COPY src/RushDay.Api/RushDay.Api.csproj src/RushDay.Api/
RUN dotnet restore src/RushDay.Api/RushDay.Api.csproj

COPY src/RushDay.Domain/ src/RushDay.Domain/
COPY src/RushDay.Infrastructure/ src/RushDay.Infrastructure/
COPY src/RushDay.Api/ src/RushDay.Api/
# The built SPA ships inside the API as static content.
COPY --from=web /src/src/RushDay.Api/wwwroot/ src/RushDay.Api/wwwroot/
RUN dotnet publish src/RushDay.Api/RushDay.Api.csproj --configuration Release --no-restore --output /app

# ---- Runtime ---------------------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "RushDay.Api.dll"]
