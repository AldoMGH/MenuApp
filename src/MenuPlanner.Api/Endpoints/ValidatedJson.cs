using System.Text.Json;
using System.Text.Json.Nodes;
using MenuPlanner.Application;
using MenuPlanner.Application.Validation;

namespace MenuPlanner.Api.Endpoints;

internal static class ValidatedJson
{
    /// <summary>Valida el cuerpo contra el JSON Schema y lo convierte al tipo de dominio; fija el id de la ruta.</summary>
    public static T Read<T>(SchemaValidator validator, string schema, JsonNode? body, string id)
    {
        if (body is not JsonObject obj)
        {
            throw new BadRequestException("Se esperaba un objeto JSON.");
        }

        obj["id"] = id;
        var errors = validator.Validate(schema, obj);
        if (errors.Count > 0)
        {
            throw new BadRequestException($"El documento no cumple el schema '{schema}'.", errors);
        }

        try
        {
            return obj.Deserialize<T>(AppJson.Options)
                ?? throw new BadRequestException("El documento está vacío.");
        }
        catch (JsonException ex)
        {
            throw new BadRequestException($"JSON inválido: {ex.Message}");
        }
    }
}
