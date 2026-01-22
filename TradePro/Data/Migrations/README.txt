Migrations will be generated using EF Core tools. To create the initial migration and apply it at runtime, the application will check for pending migrations and apply them automatically on startup.

Commands to generate migrations locally:
- dotnet tool install --global dotnet-ef
- dotnet ef migrations add InitialCreate -p TradePro -s TradePro
- dotnet ef database update -p TradePro -s TradePro

The app will use a local SQLite DB file named tradepro.db in the application folder.
