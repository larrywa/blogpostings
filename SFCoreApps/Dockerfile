#See https://aka.ms/containerfastmode to understand how Visual Studio uses this Dockerfile to build your images for faster debugging.

#Depending on the operating system of the host machines(s) that will build or run the containers, the image specified in the FROM statement may need to be changed.
#For more information, please see https://aka.ms/containercompat

FROM mcr.microsoft.com/dotnet/core/aspnet:3.1-nanoserver-1803 AS base
WORKDIR /app


FROM mcr.microsoft.com/dotnet/core/sdk:3.1-nanoserver-1803 AS build
WORKDIR /src

#App one
COPY ["SFCoreAppOne/SFCoreAppOne.csproj", "SFCoreAppOne/"]
RUN dotnet restore "SFCoreAppOne/SFCoreAppOne.csproj"
COPY . .
WORKDIR "/src/SFCoreAppOne"
RUN dotnet build "SFCoreAppOne.csproj" -c Release -o /app/build


FROM build AS publish
RUN dotnet publish "SFCoreAppOne.csproj" -c Release -o /app/publish


FROM base AS final
WORKDIR /app
RUN mkdir ErrorLogs
RUN mkdir WarnLogs
COPY --from=publish /app/publish .
EXPOSE 5000
ENV ASPNETCORE_URLS http://0.0.0.0:5000
ENTRYPOINT ["dotnet", "SFCoreAppOne.dll"]
