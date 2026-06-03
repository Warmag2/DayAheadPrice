FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS base
RUN apk add --no-cache tzdata
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /build
COPY ["src/", "src/"]
WORKDIR "/build/src/DayAheadPrice"
RUN dotnet publish "DayAheadPrice.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --chown=app:app --from=build /app/publish .
RUN mkdir -p /app/dpkeys && chown app:app /app/dpkeys
USER app
ENTRYPOINT ["dotnet", "DayAheadPrice.dll"]
