# ── Build ─────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy only project files first → docker cache layer dla restore
COPY SmogWawelski.Core/SmogWawelski.Core.csproj  SmogWawelski.Core/
COPY SmogWawelski.Web/SmogWawelski.Web.csproj    SmogWawelski.Web/
RUN dotnet restore SmogWawelski.Web/SmogWawelski.Web.csproj

# Copy source + publish
COPY SmogWawelski.Core/ SmogWawelski.Core/
COPY SmogWawelski.Web/  SmogWawelski.Web/
RUN dotnet publish SmogWawelski.Web/SmogWawelski.Web.csproj \
    -c Release \
    -o /app/publish \
    /p:UseAppHost=false

# ── Runtime ───────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

# Railway przekazuje $PORT
ENV ASPNETCORE_URLS=http://0.0.0.0:${PORT:-8080}
ENV CACHE_DIR=/data
RUN mkdir -p /data

ENTRYPOINT ["dotnet", "SmogWawelski.Web.dll"]
