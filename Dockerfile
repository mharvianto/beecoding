# Default (last stage, `docker build .`): ONE image, the web app with the judge in-process (gcc/g++ + bubblewrap inside).
# Split setup (docker-compose.split.yml, CONTAINERS.md §4): `--target web-slim` (no compiler) + `--target judge` + Redis.
#   docker build -t beecoding .          (add --build-arg BASE_PATH=/beecoding/ behind a sub-path proxy)
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
RUN curl -fsSL https://deb.nodesource.com/setup_24.x | bash - && apt-get install -y --no-install-recommends nodejs \
    && rm -rf /var/lib/apt/lists/*
ARG BASE_PATH=/
ENV VITE_BASE_PATH=$BASE_PATH
WORKDIR /src
COPY . .
# publish also runs `npm ci && npm run build` into wwwroot
RUN dotnet publish BeeCoding/BeeCoding.csproj -c Release -o /app
RUN dotnet publish BeeCoding.Judge/BeeCoding.Judge.csproj -c Release -o /app-judge

# --- judge only: pulls jobs from Redis, compiles + runs untrusted code, sends the verdict back. No database, no keys. ---
FROM mcr.microsoft.com/dotnet/runtime:10.0 AS judge
RUN apt-get update && apt-get install -y --no-install-recommends gcc g++ bubblewrap util-linux \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app-judge .
USER app
ENTRYPOINT ["dotnet", "BeeCoding.Judge.dll"]

# --- web without a compiler: use with Judge__Queue__Backend=redis and the judge image ---
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS web-slim
RUN mkdir /data && chown app:app /data
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://+:8080 \
    ConnectionStrings__Default="Data Source=/data/beecoding.db" \
    DataProtection__KeysPath=/data/keys
VOLUME /data
USER app
EXPOSE 8080
ENTRYPOINT ["dotnet", "BeeCoding.dll"]

# --- default: web + judge in one process (must stay the LAST stage) ---
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS web
# util-linux gives prlimit, which the judge puts around the compiler
RUN apt-get update && apt-get install -y --no-install-recommends gcc g++ bubblewrap util-linux \
    && rm -rf /var/lib/apt/lists/* \
    && mkdir /data && chown app:app /data
WORKDIR /app
COPY --from=build /app .
# /data = SQLite file + Data Protection keys: mount a volume there, the keys must survive restarts
ENV ASPNETCORE_URLS=http://+:8080 \
    ConnectionStrings__Default="Data Source=/data/beecoding.db" \
    DataProtection__KeysPath=/data/keys
VOLUME /data
USER app
EXPOSE 8080
ENTRYPOINT ["dotnet", "BeeCoding.dll"]
