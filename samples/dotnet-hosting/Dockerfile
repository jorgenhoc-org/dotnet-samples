# Multi-stage build for .NET 10 — the same image deploys to Railway, Fly.io, Render,
# and DigitalOcean App Platform with the configs under deploy/.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["JorgenHoc.DotnetHosting.csproj", "."]
RUN dotnet restore
COPY . .
RUN dotnet publish -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
# No ASPNETCORE_URLS=${PORT:-8080} trick here: Docker ENV resolves ${...} at BUILD time,
# so a runtime-injected PORT would be silently ignored. The app reads PORT in Program.cs
# instead; without PORT it uses the base image default (8080).
EXPOSE 8080
ENTRYPOINT ["dotnet", "JorgenHoc.DotnetHosting.dll"]
