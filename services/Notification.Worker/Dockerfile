FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY contracts/Workflow.Contracts.csproj contracts/
COPY services/Notification.Worker/Notification.Worker.csproj services/Notification.Worker/
RUN dotnet restore services/Notification.Worker/Notification.Worker.csproj
COPY contracts/ contracts/
COPY services/Notification.Worker/ services/Notification.Worker/
RUN dotnet publish services/Notification.Worker/Notification.Worker.csproj -c Release --no-restore -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS final
WORKDIR /app
USER $APP_UID
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Notification.Worker.dll"]
