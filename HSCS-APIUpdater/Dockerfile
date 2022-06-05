FROM mcr.microsoft.com/dotnet/runtime:6.0 AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:6.0 AS build
WORKDIR /src
COPY ["HSCS-APIUpdater.csproj", "./"]
RUN dotnet restore "HSCS-APIUpdater.csproj"
COPY . .
WORKDIR "/src/"
RUN dotnet build "HSCS-APIUpdater.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "HSCS-APIUpdater.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "HSCS-APIUpdater.dll"]
