# syntax=docker/dockerfile:1
# The admin dashboard image (Next.js production server). Build context is apps/admin:
#   docker build -t azwah-admin apps/admin
# Every setting is read at run time on the server (src/lib/server/config.ts); nothing is baked into the image, and no
# NEXT_PUBLIC_ setting exists. infra/docker-compose.yml runs it in development against the local stack.
FROM node:24-slim

ENV NODE_ENV=production \
    NEXT_TELEMETRY_DISABLED=1
WORKDIR /app

# Install first so the dependency layer is cached until the lockfile changes. The build needs the dev dependencies
# (TypeScript, Tailwind), so `npm ci` installs them despite NODE_ENV.
COPY package.json package-lock.json ./
RUN npm ci --include=dev

COPY . .
RUN npm run build && chown -R node:node /app/.next

USER node
EXPOSE 3000
CMD ["node_modules/.bin/next", "start", "-p", "3000", "-H", "0.0.0.0"]
