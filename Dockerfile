# ---------- Stage 1: build Angular web ----------
FROM node:20-alpine AS web
WORKDIR /web
ENV NODE_OPTIONS=--max-old-space-size=1536
COPY web/winmaster-web/package*.json ./
RUN npm ci || npm install
COPY web/winmaster-web/ ./
RUN npm run build -- --configuration production

# ---------- Stage 2: build .NET API ----------
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS api
WORKDIR /src
COPY backend/ ./
RUN dotnet restore WinMaster.API/WinMaster.API.csproj
RUN dotnet publish WinMaster.API/WinMaster.API.csproj -c Release -o /app/publish /p:UseAppHost=false

# ---------- Stage 3: runtime ----------
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app
COPY --from=api /app/publish ./
# Angular build output becomes the API's static web root.
COPY --from=web /web/dist/winmaster-web/browser ./wwwroot
ENV ASPNETCORE_ENVIRONMENT=Production
ENV ASPNETCORE_URLS=http://+:10000
EXPOSE 10000
ENTRYPOINT ["dotnet", "WinMaster.API.dll"]
