# https://hub.docker.com/_/microsoft-dotnet
FROM mcr.microsoft.com/dotnet/sdk:5.0 AS build
WORKDIR /source

# copy everything and build app
COPY P2PQuakeClient.Sandbox/. ./P2PQuakeClient.Sandbox/
COPY src/. ./src/
WORKDIR /source/P2PQuakeClient.Sandbox
RUN dotnet publish -c release -o /app

# final stage/image
FROM mcr.microsoft.com/dotnet/runtime:5.0
WORKDIR /app
COPY --from=build /app ./
ENTRYPOINT ["dotnet", "P2PQuakeClient.dll"]
