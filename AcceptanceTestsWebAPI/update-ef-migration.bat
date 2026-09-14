@echo off
REM Run this batch file any time new models are added or existing models are modified in the AcceptanceTestsShared project.
REM This batch file is used to add a new Entity Framework migration and update the database.
REM It prompts the user for a migration name, adds the migration, and then updates the database
setlocal

set /p MIGRATION_NAME=Enter migration name: 

if "%MIGRATION_NAME%"=="" (
	echo Migration name cannot be empty.
	exit /b 1
)

echo.
echo Adding migration "%MIGRATION_NAME%"...
dotnet ef migrations add "%MIGRATION_NAME%"

if errorlevel 1 (
	echo.
	echo Migration add failed. Database update was not run.
	exit /b %errorlevel%
)

echo.
echo Migration added successfully. Updating database...
dotnet ef database update

if errorlevel 1 (
	echo.
	echo Database update failed.
	exit /b %errorlevel%
)

echo.
echo Database updated successfully.
exit /b 0
