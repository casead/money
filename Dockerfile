FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy csproj files and restore (layer cache)
COPY src/MoneyRecord.Domain/MoneyRecord.Domain.csproj src/MoneyRecord.Domain/
COPY src/MoneyRecord.Application/MoneyRecord.Application.csproj src/MoneyRecord.Application/
COPY src/MoneyRecord.Infrastructure/MoneyRecord.Infrastructure.csproj src/MoneyRecord.Infrastructure/
COPY src/MoneyRecord.API/MoneyRecord.API.csproj src/MoneyRecord.API/
COPY Directory.Build.props .
COPY nuget.config .
RUN dotnet restore src/MoneyRecord.API/MoneyRecord.API.csproj

# Copy everything and publish
COPY . .
RUN dotnet publish src/MoneyRecord.API/MoneyRecord.API.csproj -c Release -o /app/publish --no-restore

# Runtime
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "MoneyRecord.API.dll"]
