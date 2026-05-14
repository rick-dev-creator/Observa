# syntax=docker/dockerfile:1.7

# ----- build -----
FROM mcr.microsoft.com/dotnet/sdk:10.0.100 AS build
WORKDIR /src

# Node is required by the Tailwind MSBuild target in Observa.Web.csproj
RUN curl -fsSL https://deb.nodesource.com/setup_20.x | bash - \
    && apt-get install -y --no-install-recommends nodejs \
    && rm -rf /var/lib/apt/lists/*

COPY . .

RUN dotnet publish src/Observa.Web/Observa.Web.csproj \
    -c Release \
    -o /app/publish

# ----- runtime -----
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish ./

ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_RUNNING_IN_CONTAINER=true

EXPOSE 8080

ENTRYPOINT ["dotnet", "Observa.Web.dll"]
