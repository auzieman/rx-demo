# syntax=docker/dockerfile:1.7
ARG DOTNET_VERSION=10.0

### Build stage
FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_VERSION} AS build
WORKDIR /src

# Copy shared libs
COPY ./src/shared ./src/shared

# Copy service & restore
COPY ./src/read-model-projection ./src/read-model-projection
RUN dotnet restore ./src/read-model-projection

# Publish
RUN dotnet publish ./src/read-model-projection -c Release -o /app/publish --no-restore

### Runtime stage (ASP.NET Core runtime required)
FROM mcr.microsoft.com/dotnet/aspnet:${DOTNET_VERSION} AS final
WORKDIR /app

COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "read-model-projection.dll"]
