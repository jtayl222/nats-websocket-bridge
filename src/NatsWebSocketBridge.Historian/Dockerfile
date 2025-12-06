# Build stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy csproj and restore dependencies
COPY ["NatsWebSocketBridge.Historian.csproj", "./"]
RUN dotnet restore

# Copy source and build
COPY . .
RUN dotnet publish -c Release -o /app/publish

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

# Create non-root user for security
RUN adduser --disabled-password --gecos '' appuser

COPY --from=build /app/publish .

# Switch to non-root user
USER appuser

# Expose metrics port
EXPOSE 9091

ENTRYPOINT ["dotnet", "NatsWebSocketBridge.Historian.dll"]
