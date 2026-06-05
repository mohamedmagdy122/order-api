FROM mcr.microsoft.com/dotnet/sdk:10.0-preview AS build
WORKDIR /src
COPY Order.API/*.csproj Order.API/
RUN dotnet restore Order.API/Order.API.csproj
COPY Order.API/. Order.API/
RUN dotnet publish Order.API/Order.API.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0-preview
RUN useradd -m appuser
WORKDIR /app
COPY --from=build /app/publish .
USER appuser
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENTRYPOINT ["dotnet","Order.API.dll"]
