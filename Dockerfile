FROM mcr.microsoft.com/dotnet/sdk:10.0 AS api-build
WORKDIR /src
COPY HeroChess.Api/ ./HeroChess.Api/
COPY HeroChess.Contracts/ ./HeroChess.Contracts/
COPY HeroChess.Rules/ ./HeroChess.Rules/
COPY Hero_Chess_DB_Prototype_v0_1/ ./Hero_Chess_DB_Prototype_v0_1/
RUN dotnet publish HeroChess.Api/HeroChess.Api.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=api-build /app/publish .
RUN mkdir -p keys && chown -R $APP_UID:$APP_UID keys
USER $APP_UID
EXPOSE 10000
CMD ["sh", "-c", "exec dotnet HeroChess.Api.dll --urls \"http://0.0.0.0:${PORT:-10000}\""]
