FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Backend/Domain/Domain.csproj Backend/Domain/
COPY Backend/Application/Application.csproj Backend/Application/
COPY Backend/Infrastructure/Infrastructure.csproj Backend/Infrastructure/
COPY Backend/Api/Api.csproj Backend/Api/
RUN dotnet restore Backend/Api/Api.csproj

COPY Backend/. Backend/
RUN dotnet publish Backend/Api/Api.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS mockrunner-build
WORKDIR /src

RUN apt-get update \
    && apt-get install -y --no-install-recommends cmake g++ make pkg-config libpaho-mqttpp-dev libpaho-mqtt-dev libssl-dev \
    && rm -rf /var/lib/apt/lists/*

COPY Native/Abstractions Native/Abstractions
COPY Native/Platform/Pc Native/Platform/Pc

RUN cmake -S Native/Platform/Pc -B /tmp/pc-build -DCMAKE_BUILD_TYPE=Release \
    && cmake --build /tmp/pc-build --config Release

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

RUN apt-get update \
    && apt-get install -y --no-install-recommends libpaho-mqttpp3-1 libpaho-mqtt1.3 \
    && rm -rf /var/lib/apt/lists/*

RUN mkdir -p /app/mock-runtime
COPY --from=mockrunner-build /tmp/pc-build/pc_telemetry_runner /app/mock-runtime/pc_telemetry_runner
RUN chmod +x /app/mock-runtime/pc_telemetry_runner

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "Api.dll"]
