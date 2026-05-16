# syntax=docker/dockerfile:1.6
ARG DOTNET_VERSION=10.0

########################
# Build stage
########################
FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_VERSION} AS build
WORKDIR /src

# 1) Copy only the csproj files first for better layer caching
#    Copy the UI csproj into /src/rx-ui/Rx.Ui/
COPY src/rx-ui/Rx.Ui/Rx.Ui.csproj /src/rx-ui/Rx.Ui/Rx.Ui.csproj
#    Copy the shared aggregator csproj into /src/shared/
COPY src/shared/shared.csproj       /src/shared/shared.csproj

# 2) Restore (the ProjectReference ../../shared/shared.csproj now resolves to /src/shared/shared.csproj)
RUN dotnet restore /src/rx-ui/Rx.Ui/Rx.Ui.csproj

# 3) Copy the remaining sources
COPY src/rx-ui/Rx.Ui/ /src/rx-ui/Rx.Ui/
COPY src/shared/      /src/shared/

# 4) Publish the UI
WORKDIR /src/rx-ui/Rx.Ui
RUN dotnet publish -c Release -o /app

########################
# Runtime stage
########################
FROM mcr.microsoft.com/dotnet/aspnet:${DOTNET_VERSION} AS runtime
WORKDIR /app
COPY --from=build /app ./

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "Rx.Ui.dll"]