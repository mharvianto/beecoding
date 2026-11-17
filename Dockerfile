# One image: the web app with the judge in-process (gcc/g++ + bubblewrap inside). See CONTAINERS.md.
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
