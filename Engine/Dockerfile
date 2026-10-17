# Builds and runs the gateway. Context is this folder:  docker build -t stepwise-gateway Engine
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY Directory.Build.props ./
COPY src/ src/
RUN dotnet publish src/Engine.Gateway/Engine.Gateway.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app
COPY --from=build /app .
# The image's built-in non-root user; listens on 8080 (ASPNETCORE_HTTP_PORTS).
USER $APP_UID
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "Engine.Gateway.dll"]
