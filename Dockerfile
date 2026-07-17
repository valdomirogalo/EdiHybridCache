# --- Stage 1: Build (AMD64) ---
FROM --platform=linux/amd64 mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /src

# Copy all .csproj files while preserving the folder structure
# COPY src/EdiHybridCache/EdiHybridCache.csproj src/EdiHybridCache/
# COPY src/EdiHybridCache.AppHost/EdiHybridCache.AppHost.csproj src/EdiHybridCache.AppHost/
# COPY playground/EdiHybridCache.Playground/EdiHybridCache.Playground.csproj playground/EdiHybridCache.Playground/

# Install Git to clone the repository
RUN apk add --no-cache git

# Clone the source code from the public repository
WORKDIR /src
RUN git clone https://github.com/valdomirogalo/EdiHybridCache.git .

# Restore ALL projects (essential to resolve references)
RUN dotnet restore "src/EdiHybridCache/EdiHybridCache.csproj" \
    && dotnet restore "src/EdiHybridCache.AppHost/EdiHybridCache.AppHost.csproj" \
    && dotnet restore "playground/EdiHybridCache.Playground/EdiHybridCache.Playground.csproj"

# Copy all source code (src/ and playground/)
COPY src/ src/
COPY playground/ playground/

# Publish the Playground with analysis suppression
RUN dotnet publish "playground/EdiHybridCache.Playground/EdiHybridCache.Playground.csproj" \
    -c Release -o /app/publish --no-restore \
    -p:TreatWarningsAsErrors=false \
    -p:EnforceCodeStyleInBuild=false \
    -p:AnalysisLevel=none

# --- Stage 2: Runtime (AMD64) ---
FROM --platform=linux/amd64 mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS runtime
WORKDIR /app

# Copy the published artifacts
COPY --from=build /app/publish .

EXPOSE 5000
ENV ASPNETCORE_URLS=http://+:5000

ENTRYPOINT ["dotnet", "EdiHybridCache.Playground.dll"]