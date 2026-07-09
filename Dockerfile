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
COPY --from=build /app ./

# The JSON database lives here — mount a Dokploy volume at /data to persist it.
ENV PETUS_DB_PATH=/data
ENV PORT=8080
VOLUME ["/data"]
EXPOSE 8080

# Seed the database folder inside the image (empty; created on first boot).
RUN mkdir -p /data

HEALTHCHECK --interval=30s --timeout=5s --start-period=15s --retries=3 \
  CMD wget -qO- http://localhost:8080/health || exit 1

ENTRYPOINT ["dotnet", "PetusCore.dll"]
