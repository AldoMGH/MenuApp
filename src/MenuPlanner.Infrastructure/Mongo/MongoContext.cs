using MenuPlanner.Domain;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Conventions;
using MongoDB.Driver;

namespace MenuPlanner.Infrastructure.Mongo;

public sealed class MongoOptions
{
    public const string Section = "Mongo";

    public string ConnectionString { get; set; } = "mongodb://localhost:27017";

    public string Database { get; set; } = "menuplanner";
}

/// <summary>Acceso a las colecciones. Documentos en camelCase y enums como texto, igual que el JSON de la API.</summary>
public sealed class MongoContext
{
    private static int _conventionsRegistered;

    public MongoContext(IOptions<MongoOptions> options)
        : this(options.Value.ConnectionString, options.Value.Database)
    {
    }

    public MongoContext(string connectionString, string database)
    {
        RegisterConventions();
        Client = new MongoClient(connectionString);
        Database = Client.GetDatabase(database);
    }

    public IMongoClient Client { get; }

    public IMongoDatabase Database { get; }

    public IMongoCollection<NutritionPlan> Plans => Database.GetCollection<NutritionPlan>("nutritionPlans");

    public IMongoCollection<Ingredient> Ingredients => Database.GetCollection<Ingredient>("ingredients");

    public IMongoCollection<Recipe> Recipes => Database.GetCollection<Recipe>("recipes");

    public IMongoCollection<WeeklyMenu> Menus => Database.GetCollection<WeeklyMenu>("weeklyMenus");

    public IMongoCollection<Pantry> Pantry => Database.GetCollection<Pantry>("pantry");

    public IMongoCollection<BasicsConfig> Basics => Database.GetCollection<BasicsConfig>("basics");

    public async Task EnsureIndexesAsync(CancellationToken ct = default)
    {
        await Plans.Indexes.CreateOneAsync(
            new CreateIndexModel<NutritionPlan>(Builders<NutritionPlan>.IndexKeys.Ascending(p => p.Active)),
            cancellationToken: ct);

        await Ingredients.Indexes.CreateManyAsync(
            [
                new CreateIndexModel<Ingredient>(Builders<Ingredient>.IndexKeys.Ascending(i => i.Group)),
                new CreateIndexModel<Ingredient>(Builders<Ingredient>.IndexKeys.Ascending(i => i.Aliases)),
            ],
            ct);

        await Recipes.Indexes.CreateManyAsync(
            [
                new CreateIndexModel<Recipe>(Builders<Recipe>.IndexKeys.Ascending(r => r.Meal)),
                new CreateIndexModel<Recipe>(Builders<Recipe>.IndexKeys.Ascending(r => r.Tags)),
                new CreateIndexModel<Recipe>(Builders<Recipe>.IndexKeys.Ascending(r => r.Stats.LastCooked)),
            ],
            ct);

        await Menus.Indexes.CreateManyAsync(
            [
                new CreateIndexModel<WeeklyMenu>(Builders<WeeklyMenu>.IndexKeys.Descending(m => m.WeekOf)),
                new CreateIndexModel<WeeklyMenu>(Builders<WeeklyMenu>.IndexKeys.Ascending(m => m.Status)),
            ],
            ct);
    }

    public async Task<bool> PingAsync(CancellationToken ct = default)
    {
        try
        {
            await Database.RunCommandAsync((Command<BsonDocument>)"{ ping: 1 }", cancellationToken: ct);
            return true;
        }
        catch (Exception ex) when (ex is TimeoutException or MongoException or OperationCanceledException)
        {
            return false;
        }
    }

    private static void RegisterConventions()
    {
        if (Interlocked.Exchange(ref _conventionsRegistered, 1) == 1)
        {
            return;
        }

        var pack = new ConventionPack
        {
            new CamelCaseElementNameConvention(),
            new EnumRepresentationConvention(BsonType.String),
            new IgnoreExtraElementsConvention(true),
            new IgnoreIfNullConvention(true),
        };
        ConventionRegistry.Register(
            "menuplanner",
            pack,
            type => type.Namespace?.StartsWith("MenuPlanner", StringComparison.Ordinal) == true);
    }
}
