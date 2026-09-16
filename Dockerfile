# Runtime image
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
USER $APP_UID

FROM node:24-alpine AS frontend
WORKDIR /src/MiniApp
COPY ["MiniApp/package.json", "MiniApp/package-lock.json", "./"]
RUN npm ci
COPY MiniApp .
COPY test-data /src/test-data
RUN npm run build

# Build image
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["oyinQ.Bot.csproj", "."]
RUN dotnet restore "./oyinQ.Bot.csproj"
COPY . .
RUN dotnet publish "./oyinQ.Bot.csproj" -c $BUILD_CONFIGURATION --no-restore -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
COPY --from=frontend /src/wwwroot/app ./wwwroot/app
ENTRYPOINT ["sh", "-c", "ASPNETCORE_URLS=http://+:${PORT:-8080} exec dotnet oyinQ.Bot.dll"]
