using System.Globalization;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using MenuPlanner.Contracts;
using MenuPlanner.Domain;
using Microsoft.JSInterop;

namespace MenuPlanner.Web.Services;

/// <summary>Error de la API (ProblemDetails) con los mensajes de validación, si los hay.</summary>
public sealed class ApiException(string message, int status, IReadOnlyList<string> errors) : Exception(message)
{
    public int Status { get; } = status;

    public IReadOnlyList<string> Errors { get; } = errors;
}

/// <summary>Cliente tipado de la API REST del planificador (mismo origen que la app).</summary>
public sealed class ApiClient(HttpClient http)
{
    /// <summary>
    /// Mensaje cuando la app no está hablando con la API: pasa si se arranca MenuPlanner.Web por separado
    /// (un servidor de archivos estáticos que responde index.html o 405) en lugar de MenuPlanner.Api.
    /// </summary>
    public const string NotConnectedMessage =
        "La app no está conectada a la API. Arranca MenuPlanner.Api (dotnet run --project src/MenuPlanner.Api; " +
        "en Visual Studio: proyecto de inicio = MenuPlanner.Api) y abre http://localhost:5033.";

    private static readonly JsonSerializerOptions Json = AppJson.Options;

    // ---- Menús
    public Task<MenuView> GenerateMenuAsync(GenerateMenuRequest request) => PostAsync<MenuView>("api/menus/generate", request);

    public Task<MenuView> GetMenuAsync(string id) => GetAsync<MenuView>($"api/menus/{E(id)}");

    public Task<List<MenuListItem>> ListMenusAsync(int limit = 20) => GetAsync<List<MenuListItem>>($"api/menus?limit={limit}");

    public Task<MenuView> AddDishAsync(string menuId, string recipeId) =>
        PostAsync<MenuView>($"api/menus/{E(menuId)}/dishes", new AddDishRequest(recipeId));

    public Task<MenuView> RemoveDishAsync(string menuId, string recipeId) => SendAsync<MenuView>(HttpMethod.Delete, $"api/menus/{E(menuId)}/dishes/{E(recipeId)}");

    public Task<MenuView> SwapDishAsync(string menuId, string recipeId, SwapCriteria criteria) =>
        PostAsync<MenuView>($"api/menus/{E(menuId)}/dishes/{E(recipeId)}/swap", criteria);

    public Task<MenuView> ConfirmMenuAsync(string menuId) => SendAsync<MenuView>(HttpMethod.Post, $"api/menus/{E(menuId)}/confirm");

    public Task<ShoppingList> GetShoppingListAsync(string menuId) => GetAsync<ShoppingList>($"api/menus/{E(menuId)}/shopping-list");

    public Task<string> GetShoppingListTextAsync(string menuId) => GetTextAsync($"api/menus/{E(menuId)}/shopping-list?format=text");

    // ---- Recetas, platillos, ingredientes
    public Task<List<Recipe>> ListRecipesAsync() => GetAsync<List<Recipe>>("api/recipes?meal=comida");

    public Task<PortionedRecipe> GetPortionedAsync(string recipeId, ProteinTarget? target, int? legumeSideServings)
    {
        var query = new List<string>();
        if (target is not null)
        {
            query.Add($"proteinTarget={target.Value.ToString().ToLowerInvariant()}");
        }

        if (legumeSideServings is not null)
        {
            query.Add($"legumeSideServings={legumeSideServings.Value.ToString(CultureInfo.InvariantCulture)}");
        }

        return GetAsync<PortionedRecipe>($"api/recipes/{E(recipeId)}/portioned{(query.Count == 0 ? string.Empty : "?" + string.Join('&', query))}");
    }

    public Task<List<CookableRecipe>> GetCookableAsync(int limit = 12) => GetAsync<List<CookableRecipe>>($"api/recipes/cookable?limit={limit}");

    public Task<List<DishView>> ListDishesAsync(string sort = "frequency") => GetAsync<List<DishView>>($"api/dishes?sort={sort}");

    public Task<List<Ingredient>> ListIngredientsAsync() => GetAsync<List<Ingredient>>("api/ingredients");

    public Task DeleteAsync(string path) => SendAsync<JsonNode?>(HttpMethod.Delete, path);

    // ---- Plan
    public Task<List<NutritionPlan>> ListPlansAsync() => GetAsync<List<NutritionPlan>>("api/plans");

    public Task ActivatePlanAsync(string id) => SendAsync<JsonNode?>(HttpMethod.Post, $"api/plans/{E(id)}/activate");

    // ---- Despensa y básicos
    public Task<Pantry> GetPantryAsync() => GetAsync<Pantry>("api/pantry");

    public Task<Pantry> SavePantryAsync(Pantry pantry) => SendAsync<Pantry>(HttpMethod.Put, "api/pantry", pantry);

    public Task<BasicsConfig> GetBasicsAsync() => GetAsync<BasicsConfig>("api/basics");

    public Task<BasicsConfig> SaveBasicsAsync(BasicsConfig basics) => SendAsync<BasicsConfig>(HttpMethod.Put, "api/basics", basics);

    public Task<List<PantrySuggestion>> GetPantrySuggestionsAsync(int months, double minShare) =>
        GetAsync<List<PantrySuggestion>>($"api/pantry/suggestions?months={months}&minShare={minShare.ToString(CultureInfo.InvariantCulture)}");

    // ---- Importación de Keep
    public async Task<ImportJob> ImportKeepAsync(string text, string fileName, bool dryRun, int? latestYear)
    {
        using var form = new MultipartFormDataContent();
        var file = new StringContent(text, Encoding.UTF8, "text/markdown");
        form.Add(file, "file", fileName);
        var url = $"api/imports/keep?dryRun={(dryRun ? "true" : "false")}{(latestYear is null ? string.Empty : $"&latestYear={latestYear}")}";
        using var response = await http.PostAsync(url, form);
        return await ReadAsync<ImportJob>(response);
    }

    public Task<List<ImportJob>> ListImportsAsync() => GetAsync<List<ImportJob>>("api/imports");

    public Task<ImportJob> GetImportAsync(string id) => GetAsync<ImportJob>($"api/imports/{E(id)}");

    public Task<ImportJob> ReviewImportAsync(string id, ReviewRequest request) => PostAsync<ImportJob>($"api/imports/{E(id)}/review", request);

    public Task<SeedSyncResult> SyncSeedAsync() => SendAsync<SeedSyncResult>(HttpMethod.Post, "api/seed/sync");

    // ---- Documentos genéricos para el formulario dinámico (JSON Schema)
    public Task<string> GetSchemaAsync(string name) => GetTextAsync($"api/schemas/{E(name)}");

    public async Task<JsonObject?> GetDocumentAsync(string path)
    {
        using var response = await http.GetAsync(path);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        return await ReadAsync<JsonObject>(response);
    }

    public Task<JsonObject> PutDocumentAsync(string path, JsonObject document) => SendAsync<JsonObject>(HttpMethod.Put, path, document);

    private async Task<T> GetAsync<T>(string url)
    {
        using var response = await http.GetAsync(url);
        return await ReadAsync<T>(response);
    }

    private async Task<string> GetTextAsync(string url)
    {
        using var response = await http.GetAsync(url);
        await EnsureSuccessAsync(response);
        if (IsHtml(response))
        {
            throw new ApiException(NotConnectedMessage, (int)response.StatusCode, []);
        }

        return await response.Content.ReadAsStringAsync();
    }

    private Task<T> PostAsync<T>(string url, object body) => SendAsync<T>(HttpMethod.Post, url, body);

    private async Task<T> SendAsync<T>(HttpMethod method, string url, object? body = null)
    {
        using var request = new HttpRequestMessage(method, url);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, body.GetType(), options: Json);
        }

        using var response = await http.SendAsync(request);
        return await ReadAsync<T>(response);
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        await EnsureSuccessAsync(response);
        if (IsHtml(response))
        {
            throw new ApiException(NotConnectedMessage, (int)response.StatusCode, []);
        }

        if (response.StatusCode == System.Net.HttpStatusCode.NoContent || response.Content.Headers.ContentLength == 0)
        {
            return default!;
        }

        return (await response.Content.ReadFromJsonAsync<T>(Json))!;
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        if (response.StatusCode == System.Net.HttpStatusCode.MethodNotAllowed || IsHtml(response))
        {
            throw new ApiException(NotConnectedMessage, (int)response.StatusCode, []);
        }

        var errors = new List<string>();
        var message = $"La API respondió {(int)response.StatusCode}.";
        try
        {
            var problem = JsonNode.Parse(await response.Content.ReadAsStringAsync()) as JsonObject;
            message = problem?["detail"]?.GetValue<string>() ?? problem?["title"]?.GetValue<string>() ?? message;
            if (problem?["errors"] is JsonArray array)
            {
                errors.AddRange(array.Select(e => e?.ToString() ?? string.Empty));
            }
        }
        catch (JsonException)
        {
            // Respuesta sin ProblemDetails: se conserva el mensaje genérico.
        }

        throw new ApiException(message, (int)response.StatusCode, errors);
    }

    private static bool IsHtml(HttpResponseMessage response) =>
        string.Equals(response.Content.Headers.ContentType?.MediaType, "text/html", StringComparison.OrdinalIgnoreCase);

    private static string E(string value) => Uri.EscapeDataString(value);
}

/// <summary>localStorage del navegador (palomitas de la lista de compras). Si no está disponible, no pasa nada.</summary>
public sealed class BrowserStorage(Microsoft.JSInterop.IJSRuntime js)
{
    public async Task<string?> GetAsync(string key)
    {
        try
        {
            return await js.InvokeAsync<string?>("menuApp.getItem", key);
        }
        catch (Microsoft.JSInterop.JSException)
        {
            return null;
        }
    }

    public async Task SetAsync(string key, string value)
    {
        try
        {
            await js.InvokeVoidAsync("menuApp.setItem", key, value);
        }
        catch (Microsoft.JSInterop.JSException)
        {
            // Sin almacenamiento local: las palomitas solo viven en la sesión.
        }
    }

    public async Task<bool> CopyAsync(string text)
    {
        try
        {
            await js.InvokeVoidAsync("menuApp.copy", text);
            return true;
        }
        catch (Microsoft.JSInterop.JSException)
        {
            return false;
        }
    }
}
