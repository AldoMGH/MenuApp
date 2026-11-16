using System.ComponentModel;
using System.Globalization;
using System.Text;
using MenuPlanner.Application.Suggestions;
using MenuPlanner.Domain;
using ModelContextProtocol.Server;

namespace MenuPlanner.Mcp.Tools;

/// <summary>
/// Platillos nuevos con lo que el usuario suele tener. Claude propone la receta; la app la valida contra el plan,
/// calcula porciones y macros y solo la guarda si el usuario acepta.
/// </summary>
[McpServerToolType]
public sealed class SuggestionTools(SuggestionContextBuilder context, RecipeDraftService drafts, PantryCookability cookability)
{
    [McpServerTool(Name = "get_suggestion_context", Title = "Contexto para proponer un platillo", ReadOnly = true)]
    [Description("Devuelve todo lo necesario para proponer un platillo NUEVO que cumpla el plan de alimentación: plantilla del plato, " +
                 "tablas de intercambio, reglas, ingredientes que el usuario suele tener (con ids), el catálogo, sus gustos y el formato JSON. " +
                 "Úsalo antes de preview_recipe.")]
    public Task<string> GetSuggestionContext(
        [Description("Qué busca el usuario (p. ej. 'algo con pescado', 'rápido', 'estilo asiático'). Opcional.")] string? focus = null,
        [Description("Ingredientes a evitar (ids o nombres). Opcional.")] string[]? avoid = null,
        CancellationToken ct = default) =>
        MenuTools.Safe(() => context.BuildAsync(focus, avoid, ct));

    [McpServerTool(Name = "preview_recipe", Title = "Revisar receta propuesta", ReadOnly = true)]
    [Description("Valida una receta propuesta (JSON con el formato de get_suggestion_context) contra el plan y el catálogo, " +
                 "y calcula el lote para 4 porciones, la porción a servir, macros aproximados y qué ingredientes ya tiene el usuario. " +
                 "No guarda nada. Si hay errores, corrígelos y vuelve a llamar.")]
    public Task<string> PreviewRecipe(
        [Description("La receta en JSON.")] string recipeJson,
        CancellationToken ct = default) =>
        MenuTools.Safe(async () =>
        {
            var draft = await drafts.PreviewAsync(recipeJson, ct);
            return draft.Valid ? Describe(draft, "Vista previa (no se ha guardado).") : Errors(draft);
        });

    [McpServerTool(Name = "save_recipe", Title = "Guardar receta propuesta")]
    [Description("Guarda la receta propuesta SOLO cuando el usuario la aceptó. Queda marcada como 'ia-sugerida' y 'revisar-nutriologa', " +
                 "se agrega a su catálogo de platillos y, si se pide, al menú en borrador.")]
    public Task<string> SaveRecipe(
        [Description("La receta en JSON (la misma que se revisó con preview_recipe).")] string recipeJson,
        [Description("Agregarla al menú en borrador.")] bool addToMenu = false,
        [Description("Id del menú. Opcional: el borrador más reciente.")] string? menuId = null,
        CancellationToken ct = default) =>
        MenuTools.Safe(async () =>
        {
            var (draft, saved) = await drafts.SaveAsync(recipeJson, addToMenu, menuId, ct);
            if (saved is null)
            {
                return Errors(draft);
            }

            var sb = new StringBuilder();
            sb.AppendLine($"Receta guardada: {saved.Recipe.Name} (id {saved.Recipe.Id}). Quedó marcada para revisarla con la nutrióloga.");
            if (saved.Menu is not null)
            {
                sb.AppendLine($"Se agregó al menú {saved.Menu.Id} ({saved.Menu.Dishes.Count} platillos).");
            }

            return sb.ToString();
        });

    [McpServerTool(Name = "cookable_recipes", Title = "Qué puedo cocinar con lo que tengo", ReadOnly = true)]
    [Description("Recetas existentes ordenadas por qué tanto de sus ingredientes ya suele tener el usuario (despensa, básicos y compras frecuentes), con lo que falta comprar.")]
    public Task<string> CookableRecipes([Description("Cuántas mostrar (por defecto 10).")] int top = 10, CancellationToken ct = default) =>
        MenuTools.Safe(async () =>
        {
            var list = await cookability.RankAsync(top, ct);
            var sb = new StringBuilder();
            foreach (var r in list)
            {
                sb.AppendLine($"- {r.Name} ({r.RecipeId}): tienes {r.Coverage:P0}{(r.Missing.Count == 0 ? string.Empty : $"; falta: {string.Join(", ", r.Missing)}")}");
            }

            return sb.Length == 0 ? "No hay recetas." : sb.ToString();
        });

    internal static string Describe(RecipeDraftResult draft, string header)
    {
        var sb = new StringBuilder();
        sb.AppendLine(header);
        sb.AppendLine(RecipeTools.FormatRecipe(draft.Portioned!, draft.Recipe));
        if (draft.Availability is { } a)
        {
            sb.AppendLine($"Ya sueles tener el {a.Coverage:P0} de los ingredientes.{(a.Missing.Count == 0 ? string.Empty : $" Habría que comprar: {string.Join(", ", a.Missing)}.")}");
        }

        return sb.ToString();
    }

    internal static string Errors(RecipeDraftResult draft) =>
        "La receta no es válida todavía. Corrige y vuelve a intentar:\n" + string.Join("\n", draft.Errors.Select(e => $"- {e}"));

    internal static string Macros(RecipeNutrition? nutrition)
    {
        if (nutrition is null)
        {
            return string.Empty;
        }

        var p = nutrition.PerServing;
        var mx = CultureInfo.InvariantCulture;
        var text = $"Por porción (aprox.): {p.Kcal.ToString("0", mx)} kcal · {p.Protein.ToString("0", mx)} g proteína · {p.Carbs.ToString("0", mx)} g carbohidratos · {p.Fat.ToString("0", mx)} g grasa · {p.Fiber.ToString("0", mx)} g fibra";
        return nutrition.Missing.Count == 0 ? text : $"{text} (sin datos de: {string.Join(", ", nutrition.Missing)})";
    }
}

[McpServerPromptType]
public sealed class SuggestionPrompts
{
    [McpServerPrompt(Name = "sugerir_platillo", Title = "Sugerir un platillo nuevo")]
    [Description("Propone un platillo nuevo con lo que el usuario suele tener, que cumpla su plan de alimentación.")]
    public static string SuggestDish([Description("Idea o antojo (p. ej. 'algo con pescado', 'rápido para entre semana'). Opcional.")] string? idea = null) =>
        $"""
        Quiero un platillo NUEVO para mi menú de comidas{(string.IsNullOrWhiteSpace(idea) ? string.Empty : $": {idea}")}.
        1. Llama a get_suggestion_context{(string.IsNullOrWhiteSpace(idea) ? string.Empty : $" con focus = \"{idea}\"")} y usa preferentemente los ingredientes que suelo tener.
        2. Propón 2 o 3 ideas breves que se parezcan a lo que me gusta pero que no estén ya en mis recetas; deja que elija una.
        3. Escribe la receta elegida en el formato JSON indicado y llama a preview_recipe. Si devuelve errores, corrígelos y repite.
        4. Muéstrame el resultado de preview_recipe (tu porción, el lote, los macros y lo que tendría que comprar).
        5. Solo si te digo que sí, llama a save_recipe (pregúntame si la agrego al menú en borrador).
        No inventes ingredientes que no estén en el catálogo, no uses métodos fritos, capeados ni empanizados y respeta la proteína magra.
        """;
}
