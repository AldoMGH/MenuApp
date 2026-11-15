using System.Text.Json.Nodes;
using MenuPlanner.Application;
using MenuPlanner.Application.Catalog;
using MenuPlanner.Application.Menus;
using MenuPlanner.Application.Validation;
using MenuPlanner.Domain;

namespace MenuPlanner.Api.Endpoints;

public static class CatalogEndpoints
{
    public static IEndpointRouteBuilder MapCatalogEndpoints(this IEndpointRouteBuilder app)
    {
        var ingredients = app.MapGroup("/api/ingredients").WithTags("Ingredientes");

        ingredients.MapGet("/", (IIngredientRepository repo, CancellationToken ct) => repo.ListAsync(ct));

        ingredients.MapGet("/{id}", async (string id, IIngredientRepository repo, CancellationToken ct) =>
            await repo.GetAsync(id, ct) ?? throw new NotFoundException($"No existe el ingrediente '{id}'."));

        ingredients.MapPut("/{id}", async (string id, JsonNode body, SchemaValidator validator, IIngredientRepository repo, CancellationToken ct) =>
            {
                var ingredient = ValidatedJson.Read<Ingredient>(validator, SchemaNames.Ingredient, body, id);
                await repo.UpsertAsync(ingredient, ct);
                return Results.Ok(ingredient);
            })
            .WithSummary("Crea o reemplaza un ingrediente (validado con schemas/ingredient.schema.json).");

        ingredients.MapDelete("/{id}", async (string id, IIngredientRepository repo, IRecipeRepository recipes, CancellationToken ct) =>
        {
            var usedBy = (await recipes.ListAsync(ct: ct))
                .Where(r => r.FixedIngredients.Any(i => i.IngredientId == id) ||
                            r.Components.Any(c => c.Ingredients.Any(i => i.IngredientId == id)))
                .Select(r => r.Name)
                .ToList();
            if (usedBy.Count > 0)
            {
                throw new ConflictException($"El ingrediente se usa en: {string.Join(", ", usedBy)}.");
            }

            return await repo.DeleteAsync(id, ct) ? Results.NoContent() : Results.NotFound();
        });

        var recipes = app.MapGroup("/api/recipes").WithTags("Recetas");

        recipes.MapGet("/", (string? meal, IRecipeRepository repo, CancellationToken ct) => repo.ListAsync(meal, ct));

        recipes.MapGet("/{id}", async (string id, IRecipeRepository repo, CancellationToken ct) =>
            await repo.GetAsync(id, ct) ?? throw new NotFoundException($"No existe la receta '{id}'."));

        recipes.MapPut("/{id}", async (
                string id,
                JsonNode body,
                SchemaValidator validator,
                IRecipeRepository repo,
                IIngredientRepository ingredientRepo,
                IPlanRepository plans,
                CancellationToken ct) =>
            {
                var recipe = ValidatedJson.Read<Recipe>(validator, SchemaNames.Recipe, body, id);
                var catalog = (await ingredientRepo.ListAsync(ct)).ToDictionary(i => i.Id);
                var errors = CatalogValidator.ValidateRecipe(recipe, catalog, await plans.GetActiveAsync(ct));
                if (errors.Count > 0)
                {
                    throw new BadRequestException("La receta no es consistente con el catálogo o el plan.", errors);
                }

                // Conserva las estadísticas si la receta ya existía y no se enviaron.
                if (body is JsonObject obj && !obj.ContainsKey("stats") && await repo.GetAsync(id, ct) is { } existing)
                {
                    recipe.Stats = existing.Stats;
                }

                await repo.UpsertAsync(recipe, ct);
                return Results.Ok(recipe);
            })
            .WithSummary("Crea o reemplaza una receta (validada con schemas/recipe.schema.json y contra el catálogo).");

        recipes.MapDelete("/{id}", async (string id, IRecipeRepository repo, CancellationToken ct) =>
            await repo.DeleteAsync(id, ct) ? Results.NoContent() : Results.NotFound());

        recipes.MapGet("/{id}/portioned", (
                string id,
                string? proteinTarget,
                int? legumeSideServings,
                MenuService service,
                CancellationToken ct) =>
            {
                ProteinTarget? target = null;
                if (!string.IsNullOrWhiteSpace(proteinTarget))
                {
                    target = Enum.TryParse<ProteinTarget>(proteinTarget, ignoreCase: true, out var parsed)
                        ? parsed
                        : throw new BadRequestException("proteinTarget debe ser min, mid o max.");
                }

                return service.PortionRecipeAsync(id, target, legumeSideServings, ct);
            })
            .WithSummary("Receta con cantidades para el lote de 4 y tu porción según el plan activo.");

        return app;
    }
}
