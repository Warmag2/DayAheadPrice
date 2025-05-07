FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
ENV TZ="Europe/Helsinki"
WORKDIR /app
RUN mkdir dpkeys

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /build
COPY ["src/", "src/"]
WORKDIR "/build/src/DayAheadPrice"
RUN dotnet publish "DayAheadPrice.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "DayAheadPrice.dll"]
