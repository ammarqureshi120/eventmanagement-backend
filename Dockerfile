# EventHub API image (AD-21): one OCI image, stateless, provider-neutral.
# Future hosting only: local dev and CI never build or run containers.
# Runtime = aspnet:10.0 (non-chiseled) with tzdata + ICU so IANA zones and culture data work (AD-12).

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY global.json Directory.Build.props Directory.Packages.props EventHub.slnx ./
COPY src/EventHub.Domain/EventHub.Domain.csproj src/EventHub.Domain/
COPY src/EventHub.Contracts/EventHub.Contracts.csproj src/EventHub.Contracts/
COPY src/EventHub.Application/EventHub.Application.csproj src/EventHub.Application/
COPY src/EventHub.Infrastructure/EventHub.Infrastructure.csproj src/EventHub.Infrastructure/
COPY src/EventHub.ServiceDefaults/EventHub.ServiceDefaults.csproj src/EventHub.ServiceDefaults/
COPY src/EventHub.Api/EventHub.Api.csproj src/EventHub.Api/
RUN dotnet restore src/EventHub.Api/EventHub.Api.csproj

COPY src/ src/
# The committed openapi.json is the contract; the image build does not regenerate it.
RUN dotnet publish src/EventHub.Api/EventHub.Api.csproj -c Release -o /app/publish --no-restore \
    -p:OpenApiGenerateDocuments=false -p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime

RUN apt-get update \
    && apt-get install -y --no-install-recommends tzdata \
        "$(apt-cache search --names-only '^libicu[0-9]+$' | cut -d' ' -f1 | sort -V | tail -n1)" \
    && rm -rf /var/lib/apt/lists/*

ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false \
    ASPNETCORE_HTTP_PORTS=8080 \
    TZ=Etc/UTC

WORKDIR /app
COPY --from=build /app/publish .

# Built-in non-root user of the official .NET images.
USER $APP_UID
EXPOSE 8080

ENTRYPOINT ["dotnet", "EventHub.Api.dll"]
