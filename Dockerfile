FROM mcr.microsoft.com/playwright/dotnet:v1.58.0-noble AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["UT-Utah-ParceliD/UT-Utah-ParceliD.csproj", "UT-Utah-ParceliD/"]
RUN dotnet restore "UT-Utah-ParceliD/UT-Utah-ParceliD.csproj"
COPY . .
WORKDIR "/src/UT-Utah-ParceliD"
RUN dotnet build "UT-Utah-ParceliD.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "UT-Utah-ParceliD.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "UT-Utah-ParceliD.dll"]
