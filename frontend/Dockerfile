# FixFlow frontend — DEVELOPMENT image (Angular dev server with live reload).
# A separate production image (static build served by a non-root web server) is added in Phase 18.

# Same Node major/minor as the developer machine (see docs/decisions/0001-tech-versions.md).
FROM node:24.21.0-alpine AS dev
WORKDIR /app

# 1) Install dependencies first, in their own cached layer. `npm ci` installs exactly what
#    package-lock.json says, with Linux binaries (the Windows node_modules is never copied — see .dockerignore).
COPY package.json package-lock.json ./
RUN npm ci

# 2) Copy the source. In `docker compose watch`, later edits are synced into /app/src.
COPY . .
EXPOSE 4200

# --host 0.0.0.0: accept connections from outside the container (the default, localhost, would be unreachable).
CMD ["npx", "ng", "serve", "--host", "0.0.0.0", "--port", "4200"]
