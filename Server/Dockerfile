# Simple Dockerfile to build and run the ShibaBridge server
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ShibaBridge.Server/ ./ShibaBridge.Server/
RUN dotnet publish ShibaBridge.Server/ShibaBridge.Server.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 8080
ENTRYPOINT ["dotnet", "ShibaBridge.Server.dll"]
