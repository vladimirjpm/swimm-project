#:project ../../Swimm.Infrastructure/Swimm.Infrastructure.csproj
#:property NoWarn=EF1001
#:property PublishAot=false
// Слепок EF-модели SwimmDbContext + дифф против снимка миграций (то, что посчитал бы
// `migrations add`). Запуск из этой папки: dotnet run ef_model_dump.cs -- before.txt
// До и после переноса конфигурации — слепки должны совпасть побайтно, дифф — 0 операций.
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Swimm.Infrastructure.Data;

var opts = new DbContextOptionsBuilder<SwimmDbContext>().UseNpgsql("Host=none").Options;
using var ctx = new SwimmDbContext(opts);
var model = ctx.GetService<IDesignTimeModel>().Model;
File.WriteAllText(args[0], model.ToDebugString(MetadataDebugStringOptions.LongDefault));

// То же, что делает `migrations add`: снимок → runtime-модель → дифф с текущей.
var snapshot = ctx.GetService<IMigrationsAssembly>().ModelSnapshot!.Model;
var init = ctx.GetService<IModelRuntimeInitializer>();
var snap = init.Initialize(((IMutableModel)snapshot).FinalizeModel(), designTime: true, validationLogger: null);
var diffs = ctx.GetService<IMigrationsModelDiffer>().GetDifferences(snap.GetRelationalModel(), model.GetRelationalModel());
Console.WriteLine($"Entities: {model.GetEntityTypes().Count()}; diff vs snapshot: {diffs.Count}");
foreach (var d in diffs) Console.WriteLine("  " + d.GetType().Name + " " + d);
