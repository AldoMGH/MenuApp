using System.Text.Json;
using System.Text.Json.Nodes;
using MenuPlanner.Domain;

namespace MenuPlanner.Application.Seeding;

/// <summary>Datos iniciales leídos de <c>seed/</c>: planes (<c>plan-*.json</c>), <c>ingredients.json</c> y <c>recipes.json</c>.</summary>
public sealed class SeedData
{
    public List<NutritionPlan> Plans { get; init; } = [];

    public List<Ingredient> Ingredients { get; init; } = [];

    public List<Recipe> Recipes { get; init; } = [];

    /// <summary>Los mismos documentos como JSON, para validarlos contra los schemas.</summary>
    public List<(string Source, string Schema, JsonNode Node)> RawDocuments { get; init; } = [];

    public static SeedData Load(string directory)
    {
        if (!Directory.Exists(directory))
        {
            throw new DirectoryNotFoundException($"No se encontró el directorio de seed: {directory}");
        }

        var raw = new List<(string, string, JsonNode)>();
        var plans = new List<NutritionPlan>();
        foreach (var file in Directory.EnumerateFiles(directory, "plan-*.json").Order(StringComparer.Ordinal))
        {
            var node = Parse(file);
            raw.Add((Path.GetFileName(file), Validation.SchemaNames.NutritionPlan, node));
            plans.Add(node.Deserialize<NutritionPlan>(AppJson.Options)!);
        }

        var ingredients = ReadArray<Ingredient>(Path.Combine(directory, "ingredients.json"), Validation.SchemaNames.Ingredient, raw);
        var recipes = ReadArray<Recipe>(Path.Combine(directory, "recipes.json"), Validation.SchemaNames.Recipe, raw);

        return new SeedData { Plans = plans, Ingredients = ingredients, Recipes = recipes, RawDocuments = raw };
    }

    private static List<T> ReadArray<T>(string file, string schema, List<(string, string, JsonNode)> raw)
    {
        if (!File.Exists(file))
        {
            return [];
        }

        var array = Parse(file).AsArray();
        var items = new List<T>();
        for (var i = 0; i < array.Count; i++)
        {
            var node = array[i]!;
            raw.Add(($"{Path.GetFileName(file)}[{i}]", schema, node));
            items.Add(node.Deserialize<T>(AppJson.Options)!);
        }

        return items;
    }

    private static JsonNode Parse(string file) =>
        JsonNode.Parse(
            File.ReadAllText(file),
            documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true })
        ?? throw new InvalidDataException($"El archivo {file} está vacío.");
}
