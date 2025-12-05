# Build stage
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /app

# Copy csproj and restore dependencies
COPY *.csproj ./
RUN dotnet restore

# Copy everything else and build
COPY . ./
RUN dotnet publish -c Release -o /app/publish

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:9.0
WORKDIR /app
COPY --from=build /app/publish .

# Railway sẽ tự động set PORT environment variable
ENV ASPNETCORE_URLS=http://+:${PORT:-8080}
EXPOSE $PORT

ENTRYPOINT ["dotnet", "MaiAmTinhThuong.dll"]

