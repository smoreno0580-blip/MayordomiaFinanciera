# Fase de compilación
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copiar el archivo de solución y proyectos principales
COPY ["MayordomiaFinanciera.sln", "./"]
COPY ["MayordomiaFinanciera.Core/MayordomiaFinanciera.Core.csproj", "MayordomiaFinanciera.Core/"]
COPY ["MayordomiaFinanciera.Infrastructure/MayordomiaFinanciera.Infrastructure.csproj", "MayordomiaFinanciera.Infrastructure/"]
COPY ["MayordomiaFinanciera.Web/MayordomiaFinanciera.Web.csproj", "MayordomiaFinanciera.Web/"]

RUN dotnet restore "MayordomiaFinanciera.Web/MayordomiaFinanciera.Web.csproj"

# Copiar todo el código restante
COPY . .
WORKDIR "/src/MayordomiaFinanciera.Web"
RUN dotnet publish -c Release -o /app/publish /p:UseAppHost=false

# Fase de ejecución final
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080

COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "MayordomiaFinanciera.Web.dll"]
