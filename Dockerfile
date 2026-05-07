FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["src/Grimoire.Core/Grimoire.Core.csproj",           "src/Grimoire.Core/"]
COPY ["src/Grimoire.Infrastructure/Grimoire.Infrastructure.csproj", "src/Grimoire.Infrastructure/"]
COPY ["src/Grimoire.Api/Grimoire.Api.csproj",              "src/Grimoire.Api/"]
RUN dotnet restore "src/Grimoire.Api/Grimoire.Api.csproj"

COPY . .
WORKDIR "/src/src/Grimoire.Api"
RUN dotnet publish "Grimoire.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

RUN mkdir -p /data && chmod 777 /data

EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENV ConnectionStrings__Default="Data Source=/data/grimoire.db"

COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Grimoire.Api.dll"]
