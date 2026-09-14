# Use the official .NET SDK image to build and publish the application
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy the rest of the application files and publish the application
COPY ./AcceptanceTestsWebAPI ./AcceptanceTestsWebAPI
COPY ./AcceptanceTestsShared ./AcceptanceTestsShared
RUN dotnet publish AcceptanceTestsWebAPI/AcceptanceTestsWebAPI.csproj -c Release -o /out

# Use the official .NET runtime image to run the application
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /out .

# Change the application URL to run on port 80 for easy exposure in Docker
ENV ASPNETCORE_HTTP_PORTS=80

# Expose the port and run the application
EXPOSE 80
ENTRYPOINT ["dotnet", "AcceptanceTestsWebAPI.dll"]