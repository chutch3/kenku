# syntax=docker/dockerfile:1
# Single image: builds the Nuxt frontend to static assets, builds the .NET API,
# then ships one runtime that serves the SPA (wwwroot) and the API on one origin.
ARG DOTNET=10.0

# ---- Stage 1: build the frontend (static prerender) ----
# Pinned to the build platform: the output is static/platform-agnostic, so build it once
# natively rather than once per target arch (node:alpine lacks some target variants anyway).
FROM --platform=$BUILDPLATFORM node:24-alpine AS web
WORKDIR /src/web/website
# The OpenAPI spec and app sources must exist BEFORE `npm ci`: the postinstall hook
# (nuxt prepare) generates the typed API client from the spec via ../../api/..., and the
# components extend those generated types. Copy spec + sources first, then install.
COPY web/website/ ./
COPY api/API/openapi/API_v2.json /src/api/API/openapi/API_v2.json
RUN npm ci
RUN npm run generate   # -> /src/web/website/.output/public

# ---- Stage 2: runtime base (aspnet + chromium for PuppeteerSharp) ----
FROM mcr.microsoft.com/dotnet/aspnet:$DOTNET-alpine AS base
USER root
RUN apk add --no-cache chromium krb5-libs

# ---- Stage 3: build the API ----
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:$DOTNET-alpine AS build-env
WORKDIR /src
COPY api/Kenku.sln /src
COPY api/API/API.csproj /src/API/API.csproj
RUN dotnet restore /src/API/API.csproj
COPY api/ /src/
# Release version stamped into the assembly. Defaults to 0.0.0 for local/main builds;
# the release pipeline passes the semantic-release version (e.g. 0.0.1).
ARG VERSION=0.0.0
# GitVersion=false: no .git in the build context, so let GitInfo skip deriving the
# package version from git (otherwise it emits an invalid "0.0.0+main." and fails);
# the version comes from the VERSION build-arg instead.
RUN --mount=type=cache,id=nuget,target=/root/.nuget/packages \
    VERSION="${VERSION:-0.0.0}" && \
    dotnet publish /src/API/API.csproj -c Release --property:OutputPath=/publish -p:GitVersion=false -p:Version="$VERSION" -maxcpucount:1 --no-cache

# ---- Stage 4: final runtime ----
FROM base AS runtime
WORKDIR /publish

EXPOSE 6531

ARG UNAME=kenku
ARG UID=1000
ARG GID=1000
RUN addgroup -g $GID $UNAME \
  && adduser -D -u $UID -G $UNAME -s /bin/sh $UNAME \
  && mkdir /usr/share/kenku-api \
  && mkdir /Manga \
  && chown 1000:1000 /usr/share/kenku-api \
  && chown 1000:1000 /Manga

# PuppeteerSharp (Chromium path + no-sandbox args)
ENV PUPPETEER_EXECUTABLE_PATH=/usr/bin/chromium-browser
ENV CHROME_BIN=/usr/bin/chromium-browser
ENV PUPPETEER_ARGS="--no-sandbox --disable-setuid-sandbox --disable-dev-shm-usage --disable-gpu --no-zygote --single-process"

# API publish output + the prerendered frontend served from wwwroot.
COPY --chown=1000:1000 --from=build-env /publish .
COPY --chown=1000:1000 --from=web /src/web/website/.output/public ./wwwroot

USER 0
ENTRYPOINT ["dotnet", "/publish/API.dll"]
CMD [""]
