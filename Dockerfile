# Multi-stage build: Node builds the SPA, the .NET SDK compiles the API with the SPA in its wwwroot,
# and the slim, non-root ASP.NET runtime image serves both from one process on one port.
# Base images are pinned by tag and digest (03-security.md section 9) and kept current by
# Dependabot's `docker` ecosystem (.github/dependabot.yml).

# ---- Front end -------------------------------------------------------------------------------------
FROM node:26-alpine@sha256:0b36e8c136b94cd4fcf02188228e76c31ad5872eef3fec8cbd2eee500cfd9e80 AS web
# Mirror the repo layout so vite.config.ts's relative outDir (../RushDay.Api/wwwroot) lands where expected.
WORKDIR /src/src/RushDay.Web

# Install first so the layer is cached until the lockfile changes.
COPY src/RushDay.Web/package.json src/RushDay.Web/package-lock.json ./
RUN npm ci --no-audit --no-fund

COPY src/RushDay.Web/ ./
RUN npm run build

# ---- API -------------------------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0@sha256:35d40304542c8689331f8cab17c65926cdf48fe711e289321d71924b230a7d29 AS build
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
# Chiseled: no shell, no package manager. Time-zone data is not needed (the server never formats in a
# zone, D26). Runs as the image's built-in non-root user.
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled@sha256:9651fa59abcdf177c30392cb44a820605ca5d618429ab37acbf6e7c644510b02 AS runtime
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "RushDay.Api.dll"]
