# Run MirrorStudio.App project
param([string[]]$Args)
dotnet run --project "MirrorStudio.App/MirrorStudio.App.csproj" @Args
