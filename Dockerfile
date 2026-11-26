FROM node:24-bookworm-slim AS web
WORKDIR /build/client
COPY client/package.json client/package-lock.json ./
RUN npm ci
COPY client/ ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS publish
WORKDIR /build
COPY src/MangaOrganizer.Server/MangaOrganizer.Server.csproj src/MangaOrganizer.Server/packages.lock.json src/MangaOrganizer.Server/
RUN dotnet restore src/MangaOrganizer.Server --locked-mode
COPY src/MangaOrganizer.Server/ src/MangaOrganizer.Server/
COPY LICENSE THIRD-PARTY-NOTICES.txt ./
COPY --from=web /build/client/dist client/dist
ARG VERSION=1.0.0
ARG TARGETARCH
RUN case "${TARGETARCH:-$(dpkg --print-architecture)}" in \
      amd64) rid=linux-x64 ;; \
      arm64) rid=linux-arm64 ;; \
      *) exit 1 ;; \
    esac && dotnet publish src/MangaOrganizer.Server --no-restore --no-self-contained -r "$rid" -c Release -p:Version="$VERSION" -o /publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS= \
    ASPNETCORE_URLS=http://0.0.0.0:8787 \
    MANGA_ORGANIZER_DATA_DIR=/config
RUN mkdir -p /config && chown app:app /config
COPY --from=publish /publish .
USER app
EXPOSE 8787
ENTRYPOINT ["dotnet", "MangaOrganizer.Server.dll"]
