EF Core migrations will be generated here once the model is finalised.

Run from the project root (with the EF Core CLI tool installed):

  dotnet tool install --global dotnet-ef   # first time only
  dotnet ef migrations add InitialCreate
  dotnet ef database update
