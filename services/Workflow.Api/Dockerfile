FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY contracts/Workflow.Contracts.csproj contracts/
COPY services/Workflow.Api/Workflow.Api.csproj services/Workflow.Api/
RUN dotnet restore services/Workflow.Api/Workflow.Api.csproj
COPY contracts/ contracts/
COPY services/Workflow.Api/ services/Workflow.Api/
RUN dotnet publish services/Workflow.Api/Workflow.Api.csproj -c Release --no-restore -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
USER $APP_UID
EXPOSE 8080
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Workflow.Api.dll"]
