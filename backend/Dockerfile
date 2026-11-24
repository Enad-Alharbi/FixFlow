# FixFlow backend — DEVELOPMENT image (dotnet watch, hot reload).
# A separate multi-stage production image (small runtime base, non-root user) is added in Phase 18.

# The SDK image contains the compiler and `dotnet watch`; it runs as root, which is acceptable for local dev only.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS dev
WORKDIR /src

# 1) Copy only the files restore needs, then restore. Docker caches this layer, so packages are
#    downloaded again only when a .csproj / global.json / Directory.Build.props changes.
COPY global.json Directory.Build.props ./
COPY FixFlow.Api/FixFlow.Api.csproj FixFlow.Api/
RUN dotnet restore FixFlow.Api/FixFlow.Api.csproj

# 2) Copy the source. In `docker compose watch`, later edits are synced into this folder.
COPY FixFlow.Api/ FixFlow.Api/
WORKDIR /src/FixFlow.Api

# The base image already sets ASPNETCORE_HTTP_PORTS=8080 (listen on all interfaces, port 8080).
# Restart automatically when an edit cannot be hot-reloaded.
ENV DOTNET_WATCH_RESTART_ON_RUDE_EDIT=true
EXPOSE 8080

# --no-launch-profile: ignore Properties/launchSettings.json (its ports are for running on the host).
CMD ["dotnet", "watch", "run", "--no-launch-profile", "--non-interactive"]
