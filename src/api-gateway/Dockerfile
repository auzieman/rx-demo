# syntax=docker/dockerfile:1.7
ARG DOTNET_VERSION=10.0

### Build stage
FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_VERSION} AS build
WORKDIR /src

# Copy shared libraries first (for caching)
COPY ./src/shared ./src/shared

# Copy service project files & restore
COPY ./src/api-gateway ./src/api-gateway
RUN dotnet restore ./src/api-gateway

# Publish
RUN dotnet publish ./src/api-gateway -c Release -o /app/publish --no-restore

### Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:${DOTNET_VERSION} AS final
WORKDIR /app

COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://0.0.0.0:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "api-gateway.dll"]