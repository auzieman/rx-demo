# syntax=docker/dockerfile:1.7
ARG DOTNET_VERSION=10.0

### Build stage
FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_VERSION} AS build
WORKDIR /src

# Copy shared libs (brings in AspNetCore instr via shared.csproj)
COPY ./src/shared ./src/shared

# Copy service & restore
COPY ./src/legacy-sync-worker ./src/legacy-sync-worker
RUN dotnet restore ./src/legacy-sync-worker

# Publish
RUN dotnet publish ./src/legacy-sync-worker -c Release -o /app/publish --no-restore

### Runtime stage (ASP.NET Core runtime required)
FROM mcr.microsoft.com/dotnet/aspnet:${DOTNET_VERSION} AS final
WORKDIR /app

COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "legacy-sync-worker.dll"]
