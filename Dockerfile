FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY renegotiation-service.csproj .
RUN dotnet restore renegotiation-service.csproj

COPY . .
RUN dotnet publish renegotiation-service.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

EXPOSE 8080
ENTRYPOINT ["dotnet", "renegotiation-service.dll"]
