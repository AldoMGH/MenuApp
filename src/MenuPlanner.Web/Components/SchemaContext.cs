using System.Text.Json.Nodes;

namespace MenuPlanner.Web.Components;

public enum SchemaKind
{
    Object,
    Array,
    String,
    Number,
    Integer,
    Boolean,
    Enum,
    Unknown,
}

public sealed record FieldOption(string Value, string Label);

public sealed record SchemaDescription(SchemaKind Kind, bool Nullable, JsonObject Schema, IReadOnlyList<string> EnumValues);

/// <summary>
/// Estado compartido del formulario dinámico: schema raíz (para resolver <c>$ref</c>), errores por ruta JSON Pointer,
/// proveedores de opciones (autocompletado de ids) y notificación de cambios.
/// </summary>
public sealed class SchemaContext(JsonObject root)
{
    public JsonObject Root { get; } = root;

    /// <summary>Errores de validación por JSON Pointer ("" = raíz, "/components/0/ingredientId"…).</summary>
    public Dictionary<string, List<string>> Errors { get; } = new(StringComparer.Ordinal);

    /// <summary>Opciones para un campo por nombre de propiedad (p. ej. <c>ingredientId</c> → catálogo).</summary>
    public Func<string, IReadOnlyList<FieldOption>?>? OptionsFor { get; init; }

    /// <summary>Rutas que se muestran pero no se editan (p. ej. "/id" de un documento existente).</summary>
    public HashSet<string> LockedPaths { get; } = new(StringComparer.Ordinal);

    public Action? OnChanged { get; set; }

    public void NotifyChanged() => OnChanged?.Invoke();

    public IReadOnlyList<string> ErrorsAt(string path) => Errors.TryGetValue(path, out var list) ? list : [];

    public static string Child(string path, string key) => $"{path}/{key.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal)}";

    public static string Child(string path, int index) => $"{path}/{index}";

    /// <summary>Resuelve <c>$ref</c> a <c>#/$defs/…</c>; las propiedades locales (title, description…) ganan.</summary>
    public JsonObject Resolve(JsonObject schema)
    {
        var current = schema;
        for (var depth = 0; depth < 16 && current["$ref"]?.GetValue<string>() is { } reference; depth++)
        {
            var name = reference.Split('/').Last();
            if (Root["$defs"]?[name] is not JsonObject target)
            {
                break;
            }

            var merged = (JsonObject)target.DeepClone();
            foreach (var (key, value) in current)
            {
                if (key != "$ref")
                {
                    merged[key] = value?.DeepClone();
                }
            }

            current = merged;
        }

        return current;
    }

    public SchemaDescription Describe(JsonObject schema)
    {
        var s = Resolve(schema);

        if (s["anyOf"] is JsonArray anyOf)
        {
            var branches = anyOf.OfType<JsonObject>().ToList();
            var nullable = branches.Any(IsNullType);
            var main = branches.FirstOrDefault(b => !IsNullType(b));
            if (main is not null)
            {
                var merged = (JsonObject)Resolve(main).DeepClone();
                foreach (var (key, value) in s)
                {
                    if (key != "anyOf")
                    {
                        merged[key] = value?.DeepClone();
                    }
                }

                var inner = Describe(merged);
                return inner with { Nullable = inner.Nullable || nullable };
            }
        }

        var (type, isNullable) = ReadType(s);
        if (s["enum"] is JsonArray values)
        {
            var list = values.Where(v => v is not null).Select(v => v!.ToString()).ToList();
            return new SchemaDescription(SchemaKind.Enum, isNullable || values.Any(v => v is null), s, list);
        }

        var kind = type switch
        {
            "object" => SchemaKind.Object,
            "array" => SchemaKind.Array,
            "string" => SchemaKind.String,
            "number" => SchemaKind.Number,
            "integer" => SchemaKind.Integer,
            "boolean" => SchemaKind.Boolean,
            null when s["properties"] is JsonObject => SchemaKind.Object,
            _ => SchemaKind.Unknown,
        };
        return new SchemaDescription(kind, isNullable, s, []);
    }

    public IEnumerable<(string Name, JsonObject Schema)> Properties(JsonObject resolved) =>
        (resolved["properties"] as JsonObject ?? [])
        .Where(p => p.Value is JsonObject)
        .Select(p => (p.Key, (JsonObject)p.Value!));

    public static HashSet<string> RequiredOf(JsonObject resolved) =>
        (resolved["required"] as JsonArray ?? []).Select(r => r?.GetValue<string>() ?? string.Empty).ToHashSet(StringComparer.Ordinal);

    public static bool IsReadOnly(JsonObject resolved) => resolved["readOnly"]?.GetValue<bool>() == true;

    public static string? Title(JsonObject resolved) => resolved["title"]?.GetValue<string>();

    public static string? Description(JsonObject resolved) => resolved["description"]?.GetValue<string>();

    /// <summary>Valor inicial para un campo nuevo (respeta <c>default</c>, <c>required</c>, <c>enum</c> y mínimos).</summary>
    public JsonNode? CreateDefault(JsonObject schema)
    {
        var d = Describe(schema);
        if (d.Schema["default"] is { } defaultValue)
        {
            return defaultValue.DeepClone();
        }

        switch (d.Kind)
        {
            case SchemaKind.Object:
                var obj = new JsonObject();
                var required = RequiredOf(d.Schema);
                foreach (var (name, propSchema) in Properties(d.Schema))
                {
                    if (required.Contains(name) && !IsReadOnly(Resolve(propSchema)))
                    {
                        obj[name] = CreateDefault(propSchema);
                    }
                }

                return obj;
            case SchemaKind.Array:
                return new JsonArray();
            case SchemaKind.Enum:
                return d.EnumValues.Count > 0 ? JsonValue.Create(d.EnumValues[0]) : null;
            case SchemaKind.String:
                return d.Nullable ? null : JsonValue.Create(string.Empty);
            case SchemaKind.Integer:
                return JsonValue.Create((int)Math.Ceiling(Minimum(d.Schema)));
            case SchemaKind.Number:
                return JsonValue.Create(Minimum(d.Schema));
            case SchemaKind.Boolean:
                return JsonValue.Create(false);
            default:
                return null;
        }
    }

    private static double Minimum(JsonObject s)
    {
        if (s["minimum"] is JsonValue min)
        {
            return min.GetValue<double>();
        }

        return s["exclusiveMinimum"] is JsonValue exclusive ? exclusive.GetValue<double>() + 1 : 0;
    }

    private static (string? Type, bool Nullable) ReadType(JsonObject s) => s["type"] switch
    {
        JsonValue value => (value.GetValue<string>(), false),
        JsonArray array => (
            array.Select(t => t?.GetValue<string>()).FirstOrDefault(t => t is not null and not "null"),
            array.Any(t => t?.GetValue<string>() == "null")),
        _ => (null, false),
    };

    private static bool IsNullType(JsonObject branch) => branch["type"]?.ToString() == "null";
}
