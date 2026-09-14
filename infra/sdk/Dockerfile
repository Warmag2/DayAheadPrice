# Build container that exposes the prebuilt Faemiyah.RepositoryPrototype package as a local NuGet source. The
# package is dropped into infra/sdk/ on both the dev machine and the server and is intentionally kept out of the
# repository, so the application image restores it from here instead of a project reference.
FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS sdk
WORKDIR /build
COPY infra/sdk/nuget.config ./
COPY infra/sdk/*.nupkg customnugets/
