using System.ComponentModel;
using System.Globalization;
using System.Text;
using MenuPlanner.Application;
using MenuPlanner.Application.Menus;
using MenuPlanner.Application.Shopping;
using MenuPlanner.Domain;
using MenuPlanner.Portioning;
using ModelContextProtocol.Server;

namespace MenuPlanner.Mcp.Tools;

/// <summary>Herramientas del menú semanal para Claude. Devuelven texto legible con los ids para encadenar llamadas.</summary>
[McpServerToolType]
public sealed class MenuTools(MenuService menus, DishResolver resolver)
{
    [McpServerTool(Name = "generate_weekly_menu", Title = "Generar menú semanal")]
    [Description("Genera un menú semanal de comidas según el plan de alimentación: N platillos de 4 porciones cada uno, " +
                 "con cantidades para cocinar, la porción a servir y el balance de leguminosa. Devuelve el id del menú (borrador).")]
    public Task<string> GenerateWeeklyMenu(
        [Description("Número de platillos (1–14). Cada platillo rinde 4 porciones.")] int dishCount,
        [Description("Lunes de la semana en formato AAAA-MM-DD. Opcional: por defecto el próximo lunes.")] string? weekOf = null,
        [Description("Platillos que deben incluirse, como los escribe el usuario (p. ej. 'enchiladas suizas', 'tinga').")] string[]? include = null,
        [Description("Platillos que no deben incluirse.")] string[]? exclude = null,
        [Description("Semilla para reproducir el mismo menú. Opcional.")] int? seed = null,
        CancellationToken ct = default) =>
        Safe(async () =>
        {
            DateOnly? week = null;
            if (!string.IsNullOrWhiteSpace(weekOf))
            {
                week = DateOnly.TryParse(weekOf, CultureInfo.InvariantCulture, out var parsed)
                    ? parsed
                    : throw new BadRequestException($"weekOf '{weekOf}' no es una fecha AAAA-MM-DD.");
            }

            var notes = new StringBuilder();
            var mustInclude = await ResolveManyAsync(include, notes, ct);
            var excluded = await ResolveManyAsync(exclude, notes, ct);
            var view = await menus.GenerateAsync(new GenerateMenuRequest(dishCount, week, seed, mustInclude, excluded), ct);
            return Render(view, notes.ToString());
        });

    [McpServerTool(Name = "get_menu", Title = "Ver menú", ReadOnly = true)]
    [Description("Muestra un menú con sus platillos, porciones y avisos. Sin id, muestra el borrador más reciente.")]
    public Task<string> GetMenu([Description("Id del menú. Opcional.")] string? menuId = null, CancellationToken ct = default) =>
        Safe(async () => Render(await menus.GetAsync(await MenuIdAsync(menuId, ct), ct)));

    [McpServerTool(Name = "list_menus", Title = "Menús recientes", ReadOnly = true)]
    [Description("Lista los menús recientes (borradores y confirmados) con su semana y platillos.")]
    public Task<string> ListMenus([Description("Cuántos mostrar (por defecto 10).")] int limit = 10, CancellationToken ct = default) =>
        Safe(async () =>
        {
            var list = await menus.ListAsync(Math.Clamp(limit, 1, 50), ct);
            if (list.Count == 0)
            {
                return "Todavía no hay menús. Usa generate_weekly_menu.";
            }

            var sb = new StringBuilder();
            foreach (var menu in list)
            {
                var status = menu.Status == MenuStatus.Confirmed ? "confirmado" : "borrador";
                sb.AppendLine($"- {menu.Id} · semana del {MenuService.FormatWeek(menu.WeekOf)} · {status}: {string.Join(", ", menu.Dishes)}");
            }

            return sb.ToString();
        });

    [McpServerTool(Name = "swap_dish", Title = "Cambiar un platillo")]
    [Description("Cambia un platillo del menú (borrador) por otro compatible. Puede pedirse una proteína ('pescado', 'pollo', 'res', 'cerdo', 'mariscos') o una etiqueta ('tacos', 'asiatica', 'sopa').")]
    public Task<string> SwapDish(
        [Description("Platillo a cambiar, como lo escribe el usuario.")] string dish,
        [Description("Id del menú. Opcional: el borrador más reciente.")] string? menuId = null,
        [Description("Proteína deseada para el reemplazo. Opcional.")] string? proteinFamily = null,
        [Description("Etiqueta deseada para el reemplazo. Opcional.")] string? tag = null,
        CancellationToken ct = default) =>
        Safe(async () =>
        {
            var id = await MenuIdAsync(menuId, ct);
            var recipe = await ResolveInMenuAsync(id, dish, ct);
            return Render(await menus.SwapDishAsync(id, recipe.RecipeId, new SwapCriteria(proteinFamily, tag), ct), $"Se cambió '{recipe.Name}'.");
        });

    [McpServerTool(Name = "add_dish", Title = "Agregar un platillo")]
    [Description("Agrega un platillo al menú (borrador).")]
    public Task<string> AddDish(
        [Description("Platillo a agregar, como lo escribe el usuario.")] string dish,
        [Description("Id del menú. Opcional: el borrador más reciente.")] string? menuId = null,
        CancellationToken ct = default) =>
        Safe(async () =>
        {
            var id = await MenuIdAsync(menuId, ct);
            var recipe = Require(await resolver.ResolveAsync(dish, ct: ct));
            return Render(await menus.AddDishAsync(id, recipe.RecipeId, ct), $"Se agregó '{recipe.Name}'.");
        });

    [McpServerTool(Name = "remove_dish", Title = "Quitar un platillo", Destructive = true)]
    [Description("Quita un platillo del menú (borrador).")]
    public Task<string> RemoveDish(
        [Description("Platillo a quitar, como lo escribe el usuario.")] string dish,
        [Description("Id del menú. Opcional: el borrador más reciente.")] string? menuId = null,
        CancellationToken ct = default) =>
        Safe(async () =>
        {
            var id = await MenuIdAsync(menuId, ct);
            var recipe = await ResolveInMenuAsync(id, dish, ct);
            return Render(await menus.RemoveDishAsync(id, recipe.RecipeId, ct), $"Se quitó '{recipe.Name}'.");
        });

    [McpServerTool(Name = "confirm_menu", Title = "Confirmar menú")]
    [Description("Confirma el menú: congela recetas y lista de compras y registra los platillos en el historial. Después ya no se puede editar.")]
    public Task<string> ConfirmMenu([Description("Id del menú. Opcional: el borrador más reciente.")] string? menuId = null, CancellationToken ct = default) =>
        Safe(async () => Render(await menus.ConfirmAsync(await MenuIdAsync(menuId, ct), ct), "Menú confirmado."));

    [McpServerTool(Name = "get_shopping_list", Title = "Lista de compras", ReadOnly = true)]
    [Description("Lista de compras del menú agrupada por pasillo, con cantidades redondeadas a cómo se compran. Incluye los básicos diarios (colación) y separa lo que ya hay en la despensa.")]
    public Task<string> GetShoppingList([Description("Id del menú. Opcional: el borrador más reciente.")] string? menuId = null, CancellationToken ct = default) =>
        Safe(async () =>
        {
            var id = await MenuIdAsync(menuId, ct);
            var view = await menus.GetAsync(id, ct);
            var list = await menus.GetShoppingListAsync(id, ct);
            return ShoppingListBuilder.ToText(list, $"Lista de compras · semana del {MenuService.FormatWeek(view.WeekOf)} (menú {id})");
        });

    internal static async Task<string> Safe(Func<Task<string>> action)
    {
        try
        {
            return await action();
        }
        catch (Exception ex) when (ex is AppException or ConversionException)
        {
            return $"⚠️ {ex.Message}";
        }
    }

    private static string Render(MenuView view, string? note = null)
    {
        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(note))
        {
            sb.AppendLine(note.Trim());
            sb.AppendLine();
        }

        sb.Append(MenuTextFormatter.Format(view));
        sb.AppendLine();
        sb.AppendLine($"Id del menú: {view.Id}");
        if (view.Status == MenuStatus.Draft)
        {
            sb.AppendLine("Puedes cambiar, agregar o quitar platillos, pedir la lista de compras o confirmar el menú.");
        }

        return sb.ToString();
    }

    private async Task<string> MenuIdAsync(string? menuId, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(menuId))
        {
            return menuId.Trim();
        }

        var list = await menus.ListAsync(50, ct);
        return (list.FirstOrDefault(m => m.Status == MenuStatus.Draft) ?? list.FirstOrDefault())?.Id
            ?? throw new NotFoundException("No hay menús todavía. Genera uno con generate_weekly_menu.");
    }

    private async Task<RecipeOption> ResolveInMenuAsync(string menuId, string dish, CancellationToken ct)
    {
        var view = await menus.GetAsync(menuId, ct);
        return Require(await resolver.ResolveAsync(dish, view.Dishes.Select(d => d.RecipeId).ToList(), ct));
    }

    private async Task<List<string>> ResolveManyAsync(string[]? names, StringBuilder notes, CancellationToken ct)
    {
        var ids = new List<string>();
        foreach (var name in names ?? [])
        {
            var resolution = await resolver.ResolveAsync(name, ct: ct);
            if (resolution.Recipe is not null)
            {
                ids.Add(resolution.Recipe.RecipeId);
            }
            else
            {
                notes.AppendLine($"No se pudo usar '{name}': {Explain(resolution)}");
            }
        }

        return ids;
    }

    private static RecipeOption Require(DishResolution resolution) =>
        resolution.Recipe ?? throw new BadRequestException(Explain(resolution));

    internal static string Explain(DishResolution resolution)
    {
        var message = resolution.Message ?? $"No encontré '{resolution.Query}'.";
        return resolution.Candidates.Count == 0
            ? message
            : $"{message} Opciones: {string.Join("; ", resolution.Candidates.Select(c => $"{c.Name} ({c.RecipeId})"))}.";
    }
}
