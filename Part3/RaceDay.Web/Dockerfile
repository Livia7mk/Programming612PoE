# ---- Stage 1: build ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy the project file first so package restore is cached between builds
COPY RaceDay.Web.csproj ./
RUN dotnet restore

# Copy the rest of the source and publish
COPY . ./
RUN dotnet publish -c Release -o /app/publish --no-restore

# ---- Stage 2: run ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish ./

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

# Run as a non-root user (the aspnet image provides one)
USER $APP_UID

ENTRYPOINT ["dotnet", "RaceDay.Web.dll"]# ---- Stage 1: build ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy the project file first so package restore is cached between builds
COPY RaceDay.Web.csproj ./
RUN dotnet restore

# Copy the rest of the source and publish
COPY . ./
RUN dotnet publish -c Release -o /app/publish --no-restore

# ---- Stage 2: run ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish ./

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

# Run as a non-root user (the aspnet image provides one)
USER $APP_UID

ENTRYPOINT ["dotnet", "RaceDay.Web.dll"]