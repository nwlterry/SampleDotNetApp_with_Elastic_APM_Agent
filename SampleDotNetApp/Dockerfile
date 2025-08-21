FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 80
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:8.0.100 AS build
WORKDIR /src
COPY ["SampleDotNetApp.csproj", "."]
RUN dotnet restore "SampleDotNetApp.csproj"
COPY . .
WORKDIR "/src"
RUN dotnet build "SampleDotNetApp.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "SampleDotNetApp.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .

# Add CA certificates and custom OpenSSL config
RUN apt-get update && apt-get install -y ca-certificates openssl
COPY openssl.cnf /etc/ssl/openssl.cnf
RUN update-ca-certificates

ENV OPENSSL_CONF=/etc/ssl/openssl.cnf

ENTRYPOINT ["dotnet", "SampleDotNetApp.dll"]
