FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ShiftPlanner.sln ./
COPY src/ShiftPlanner.Domain/*.csproj src/ShiftPlanner.Domain/
COPY src/ShiftPlanner.Application/*.csproj src/ShiftPlanner.Application/
COPY src/ShiftPlanner.Infrastructure/*.csproj src/ShiftPlanner.Infrastructure/
COPY src/ShiftPlanner.Api/*.csproj src/ShiftPlanner.Api/
RUN dotnet restore src/ShiftPlanner.Api/ShiftPlanner.Api.csproj

COPY src/ src/
RUN dotnet publish src/ShiftPlanner.Api/ShiftPlanner.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENTRYPOINT ["dotnet", "ShiftPlanner.Api.dll"]
