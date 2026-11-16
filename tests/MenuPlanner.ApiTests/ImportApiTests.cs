using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using MenuPlanner.Application;
using MenuPlanner.Domain;

namespace MenuPlanner.ApiTests;

public class ImportApiTests
{
    private static string Sample => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "keep-sample-drive.md"));

    [Fact]
    public async Task Upload_review_and_query_dishes()
    {
        await using var factory = new ApiFactory();
        var client = factory.CreateClient();

        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes(Sample));
        file.Headers.ContentType = new MediaTypeHeaderValue("text/markdown");
        form.Add(file, "file", "Menus.md");

        var response = await client.PostAsync("/api/imports/keep", form);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var job = await response.Content.ReadFromJsonAsync<JsonObject>(AppJson.Options);
        var id = job!["id"]!.GetValue<string>();
        Assert.Equal("Menus.md", job["fileName"]!.GetValue<string>());
        Assert.Equal(5, job["newNotes"]!.GetValue<int>());
        Assert.Equal("platillo misterioso", job["unmatched"]![0]!["name"]!.GetValue<string>());

        var review = await client.PostAsJsonAsync($"/api/imports/{id}/review",
            new { mappings = new object[] { new { name = "platillo misterioso", newDishName = "Pozole verde" }, new { name = "alambreh", dishId = "alambre" } } },
            AppJson.Options);
        Assert.Equal(HttpStatusCode.OK, review.StatusCode);
        var reviewed = await review.Content.ReadFromJsonAsync<JsonObject>(AppJson.Options);
        Assert.Empty(reviewed!["unmatched"]!.AsArray());

        var dishes = await client.GetFromJsonAsync<JsonArray>("/api/dishes", AppJson.Options);
        var counts = dishes!.Select(d => d!["dish"]!["stats"]!["timesCooked"]!.GetValue<int>()).ToList();
        Assert.Equal(counts.OrderByDescending(c => c), counts);                                       // ordenado por frecuencia
        Assert.Equal(2, counts[0]);
        Assert.Contains(dishes!, d => d!["dish"]!["id"]!.GetValue<string>() == "pozole-verde");

        var put = await client.PutAsJsonAsync("/api/dishes/pozole-verde",
            new { name = "Pozole verde", aliases = new[] { "platillo misterioso" }, compatibility = "adaptada", recipeId = "pollo-en-pipian" }, AppJson.Options);
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        Assert.True(factory.Store.RecipeList.Single(r => r.Id == "pollo-en-pipian").Stats.HistoryCount >= 2);

        var badPut = await client.PutAsJsonAsync("/api/dishes/pozole-verde", new { name = "x", recipeId = "no-existe" }, AppJson.Options);
        Assert.Equal(HttpStatusCode.BadRequest, badPut.StatusCode);

        var jobs = await client.GetFromJsonAsync<JsonArray>("/api/imports", AppJson.Options);
        Assert.Single(jobs!);
    }

    [Fact]
    public async Task Plain_text_body_dry_run_and_pantry_suggestions()
    {
        await using var factory = new ApiFactory();
        var client = factory.CreateClient();

        var dry = await client.PostAsync("/api/imports/keep?dryRun=true", new StringContent(Sample, Encoding.UTF8, "text/plain"));
        Assert.Equal(HttpStatusCode.OK, dry.StatusCode);
        Assert.Empty(factory.Store.Notes);

        var real = await client.PostAsync("/api/imports/keep", new StringContent(Sample, Encoding.UTF8, "text/plain"));
        Assert.Equal(HttpStatusCode.OK, real.StatusCode);

        var suggestions = await client.GetFromJsonAsync<JsonArray>("/api/pantry/suggestions?months=12&minShare=0.5", AppJson.Options);
        Assert.Contains(suggestions!, s => s!["ingredientId"]?.GetValue<string>() == "pierna-cerdo");

        var empty = await client.PostAsync("/api/imports/keep", new StringContent("nada que importar", Encoding.UTF8, "text/plain"));
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
    }

    [Fact]
    public async Task Seed_sync_is_idempotent()
    {
        await using var factory = new ApiFactory();
        factory.Store.RecipeList.Add(new Recipe { Id = "fajitas-res", Name = "Fajitas", Source = "semilla" });
        var client = factory.CreateClient();

        var first = await (await client.PostAsync("/api/seed/sync", null)).Content.ReadFromJsonAsync<JsonObject>(AppJson.Options);
        Assert.Equal(1, first!["recipesRemoved"]!.GetValue<int>());

        var second = await (await client.PostAsync("/api/seed/sync", null)).Content.ReadFromJsonAsync<JsonObject>(AppJson.Options);
        Assert.Equal(0, second!["recipesRemoved"]!.GetValue<int>());
        Assert.Equal(0, second["dishesAdded"]!.GetValue<int>());
    }
}
