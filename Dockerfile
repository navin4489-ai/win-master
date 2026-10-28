# ---------- Stage 1: build Angular web ----------
FROM node:20-alpine AS web
WORKDIR /web
ENV NODE_OPTIONS=--max-old-space-size=1536
COPY web/winmaster-web/package*.json ./
RUN npm ci || npm install
COPY web/winmaster-web/ ./
RUN npm run build -- --configuration production

# ---------- Stage 2: install Node bridge dependencies ----------
FROM node:20-alpine AS bridge
WORKDIR /bridge
COPY services/wa-bridge/package*.json ./
RUN npm ci --omit=dev || npm install --omit=dev
COPY services/wa-bridge/src ./src

# ---------- Stage 3: build .NET API ----------
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS api
WORKDIR /src
COPY backend/ ./
RUN dotnet restore WinMaster.API/WinMaster.API.csproj
RUN dotnet publish WinMaster.API/WinMaster.API.csproj -c Release -o /app/publish /p:UseAppHost=false

# ---------- Stage 4: runtime (API + co-hosted Node bridge) ----------
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app

# Install Node.js so the WhatsApp bridge can run in the same container.
# On Render's free tier a separate always-on service isn't available, so the
# bridge is co-hosted with the API and reached over localhost.
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl ca-certificates \
    && curl -fsSL https://deb.nodesource.com/setup_20.x | bash - \
    && apt-get install -y --no-install-recommends nodejs \
    && apt-get clean && rm -rf /var/lib/apt/lists/*

# .NET API + Angular static files.
COPY --from=api /app/publish ./
COPY --from=web /web/dist/winmaster-web/browser ./wwwroot

# Node bridge (source + production node_modules).
COPY --from=bridge /bridge /app/bridge

# Combined entrypoint launches the bridge, then the API.
COPY docker-entrypoint.sh /app/docker-entrypoint.sh
# Normalize line endings (in case of CRLF from Windows) and make executable.
RUN sed -i 's/\r$//' /app/docker-entrypoint.sh && chmod +x /app/docker-entrypoint.sh

ENV ASPNETCORE_ENVIRONMENT=Production
ENV ASPNETCORE_URLS=http://+:10000
# The API reaches the co-hosted bridge over localhost; sessions are stored on
# the container's ephemeral filesystem (reset when the free service sleeps).
ENV Bridge__BaseUrl=http://localhost:3001
ENV BRIDGE_PORT=3001
ENV SESSIONS_DIR=/app/bridge/sessions

EXPOSE 10000
ENTRYPOINT ["/app/docker-entrypoint.sh"]
