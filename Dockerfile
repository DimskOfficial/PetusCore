# PetusCore — Geometry Dash 2.2 server core (VB.NET / ASP.NET Core)
# Multi-stage build: restore+publish with the SDK, run on the slim ASP.NET runtime.

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY src/PetusCore/PetusCore.vbproj ./PetusCore/
RUN dotnet restore ./PetusCore/PetusCore.vbproj
COPY src/PetusCore/ ./PetusCore/
RUN dotnet publish ./PetusCore/PetusCore.vbproj -c Release -o /app /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

# curl is needed for the Docker HEALTHCHECK below (not in the base image).
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app ./

# Storage is PostgreSQL — set PETUS_DB_URL to your Postgres connection string.
ENV PORT=8080
EXPOSE 8080

HEALTHCHECK --interval=30s --timeout=5s --start-period=15s --retries=3 \
  CMD curl -fsS http://localhost:8080/health || exit 1

ENTRYPOINT ["dotnet", "PetusCore.dll"]
