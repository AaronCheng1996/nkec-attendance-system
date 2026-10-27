# syntax=docker/dockerfile:1

# 1. Build the React PWA
FROM node:22-alpine AS web
WORKDIR /web
COPY web/package.json web/package-lock.json ./
RUN npm ci
COPY web/ ./
RUN npm run build

# 2. Publish the ASP.NET Core API
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS api
WORKDIR /src
COPY src/Attendance.Api/Attendance.Api.csproj src/Attendance.Api/
RUN dotnet restore src/Attendance.Api/Attendance.Api.csproj
COPY src/ src/
RUN dotnet publish src/Attendance.Api/Attendance.Api.csproj -c Release -o /app --no-restore

# 3. Runtime: the API also serves the PWA from wwwroot
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=api /app ./
COPY --from=web /web/dist ./wwwroot
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "Attendance.Api.dll"]
