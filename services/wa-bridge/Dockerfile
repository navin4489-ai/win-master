FROM node:20-alpine
WORKDIR /app

# Install dependencies first for better layer caching.
COPY package*.json ./
RUN npm ci --omit=dev || npm install --omit=dev

COPY src/ ./src/

# Persisted Baileys auth state. On Render, mount a disk here.
ENV SESSIONS_DIR=/data/sessions
RUN mkdir -p /data/sessions

ENV NODE_ENV=production
EXPOSE 3001
CMD ["node", "src/server.js"]
