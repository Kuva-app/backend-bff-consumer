FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Source/Kuva.ConsumerBff.slnx ./Source/
COPY Source/Kuva.ConsumerBff.Entities/*.csproj ./Source/Kuva.ConsumerBff.Entities/
COPY Source/Kuva.ConsumerBff.Business/*.csproj ./Source/Kuva.ConsumerBff.Business/
COPY Source/Kuva.ConsumerBff.Repository/*.csproj ./Source/Kuva.ConsumerBff.Repository/
COPY Source/Kuva.ConsumerBff.EFMigrations/*.csproj ./Source/Kuva.ConsumerBff.EFMigrations/
COPY Source/Kuva.ConsumerBff.Service/*.csproj ./Source/Kuva.ConsumerBff.Service/

RUN dotnet restore ./Source/Kuva.ConsumerBff.slnx

COPY . .
RUN dotnet publish ./Source/Kuva.ConsumerBff.Service/Kuva.ConsumerBff.Service.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "Kuva.ConsumerBff.Service.dll"]
