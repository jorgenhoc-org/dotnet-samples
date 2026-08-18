# Debian-based runtime — the broadest-compatibility default.
# Build:  docker build -t jorgenhoc-sample:debian .

# ── Stage 1: restore ──────────────────────────────────────────────────────────
# Separate restore so Docker caches it until the .csproj changes, rather than
# re-downloading every NuGet package whenever a .cs file changes.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS restore
WORKDIR /src

COPY ["JorgenHoc.DotnetDockerContainer.csproj", "./"]
RUN dotnet restore "JorgenHoc.DotnetDockerContainer.csproj"

# ── Stage 2: publish ──────────────────────────────────────────────────────────
FROM restore AS publish
COPY . .
RUN dotnet publish "JorgenHoc.DotnetDockerContainer.csproj" \
    -c Release \
    --no-restore \
    -o /app/publish \
    -p:UseAppHost=false

# ── Stage 3: runtime ──────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final

WORKDIR /app
COPY --from=publish /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production
EXPOSE 8080

# Non-root via the `app` user (UID 1654) baked into every .NET 8+ image. The old
# `RUN adduser` pattern fails with exit 127 on the .NET 10 Debian image — the slim base
# no longer ships adduser/addgroup at all.
USER app

# NOTE: no HEALTHCHECK here on purpose. The aspnet images ship without wget or curl,
# so the usual `HEALTHCHECK CMD wget -qO- .../health` fails with "not found" and marks a
# perfectly healthy container unhealthy. See this folder's README.
ENTRYPOINT ["dotnet", "JorgenHoc.DotnetDockerContainer.dll"]
