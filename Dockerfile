FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /src
COPY src/Bookwell/Bookwell.csproj src/Bookwell/
RUN dotnet restore src/Bookwell/Bookwell.csproj
COPY src/ src/
RUN dotnet publish src/Bookwell -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=3000 DATA_DIR=/app/data
COPY --from=build /app .
RUN mkdir -p /app/data && chown $APP_UID /app/data
USER $APP_UID
EXPOSE 3000
ENTRYPOINT ["dotnet", "Bookwell.dll"]
