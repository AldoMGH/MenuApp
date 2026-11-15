using System.Text.Json.Nodes;
using Json.Schema;

namespace MenuPlanner.Application.Validation;

public static class SchemaNames
{
    public const string NutritionPlan = "nutrition-plan";
    public const string Recipe = "recipe";
    public const string Ingredient = "ingredient";
    public const string Dish = "dish";
}

/// <summary>
/// Valida documentos contra los JSON Schema de <c>schemas/</c>. Es la misma fuente que usarán los formularios
/// dinámicos de la UI, así que API, seed y formularios aplican exactamente las mismas reglas.
/// </summary>
public sealed class SchemaValidator
{
    private readonly Dictionary<string, (JsonSchema Schema, string Raw)> _schemas;

    private SchemaValidator(Dictionary<string, (JsonSchema, string)> schemas) => _schemas = schemas;

    public IReadOnlyCollection<string> Names => _schemas.Keys;

    public static SchemaValidator FromDirectory(string directory)
    {
        if (!Directory.Exists(directory))
        {
            throw new DirectoryNotFoundException($"No se encontró el directorio de schemas: {directory}");
        }

        var schemas = new Dictionary<string, (JsonSchema, string)>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in Directory.EnumerateFiles(directory, "*.schema.json"))
        {
            var name = Path.GetFileName(file).Replace(".schema.json", string.Empty, StringComparison.OrdinalIgnoreCase);
            var raw = File.ReadAllText(file);
            schemas[name] = (JsonSchema.FromText(raw), raw);
        }

        return new SchemaValidator(schemas);
    }

    public string? GetRaw(string name) => _schemas.TryGetValue(name, out var entry) ? entry.Raw : null;

    /// <summary>Devuelve la lista de errores ("ruta: mensaje"); vacía si el documento es válido.</summary>
    public IReadOnlyList<string> Validate(string name, JsonNode? document)
    {
        if (!_schemas.TryGetValue(name, out var entry))
        {
            throw new KeyNotFoundException($"No existe el schema '{name}'.");
        }

        var results = entry.Schema.Evaluate(document, new EvaluationOptions { OutputFormat = OutputFormat.List });
        if (results.IsValid)
        {
            return [];
        }

        var errors = new List<string>();
        Collect(results, errors);
        return errors.Count > 0 ? errors.Distinct().ToList() : ["El documento no cumple el schema."];
    }

    private static void Collect(EvaluationResults results, List<string> errors)
    {
        if (results.Errors is not null)
        {
            foreach (var (keyword, message) in results.Errors)
            {
                var location = results.InstanceLocation.ToString();
                errors.Add($"{(string.IsNullOrEmpty(location) ? "/" : location)}: {message} ({keyword})");
            }
        }

        foreach (var detail in results.Details)
        {
            Collect(detail, errors);
        }
    }
}
