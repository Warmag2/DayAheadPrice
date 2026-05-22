FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
ENV TZ="Europe/Helsinki"
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /build
COPY ["src/", "src/"]
WORKDIR "/build/src/DayAheadPrice"
RUN dotnet publish "DayAheadPrice.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --chown=app:app --from=build /app/publish .
USER app
ENTRYPOINT ["dotnet", "DayAheadPrice.dll"]
