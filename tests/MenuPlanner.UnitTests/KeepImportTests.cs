using MenuPlanner.Application;
using MenuPlanner.Application.Imports;
using MenuPlanner.Application.Menus;
using MenuPlanner.Application.Seeding;
using MenuPlanner.Domain;
using MenuPlanner.Importers;

namespace MenuPlanner.UnitTests;

internal static class Fixtures
{
    public static readonly DateTime Today = new(2026, 11, 15);

    public static string Drive => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "keep-sample-drive.md"));

    public static string Docs => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "keep-sample-docs.txt"));

    public static DishMatcher Matcher()
    {
        var seed = TestData.Load();
        return new DishMatcher(seed.Dishes, seed.ImportRules);
    }
}

public class KeepNotesParserTests
{
    [Theory]
    [InlineData("**Noviembre 1**", 11, 1)]
    [InlineData("Lista Julio - 2", 7, 2)]
    [InlineData("🎃🕸️ Octubre 4", 10, 4)]
    [InlineData("Diciembre 2🎅🎄", 12, 2)]
    [InlineData("Octubre", 10, null)]
    [InlineData("Lista mayo 2", 5, 2)]
    public void Parses_titles(string title, int month, int? week)
    {
        Assert.True(KeepNotesParser.TryParseTitle(TextNormalizer.Clean(title), out var m, out var w));
        Assert.Equal(month, m);
        Assert.Equal(week, w);
    }

    [Theory]
    [InlineData("Pollo con mole")]
    [InlineData("Comidas")]
    [InlineData("1 kg de pechuga")]
    public void Ignores_non_titles(string line) => Assert.False(KeepNotesParser.TryParseTitle(line, out _, out _));

    [Fact]
    public void Parses_notes_sections_and_years()
    {
        var result = KeepNotesParser.Parse(Fixtures.Drive, today: Fixtures.Today);

        Assert.Equal(5, result.Notes.Count);
        Assert.Equal(["Febrero 1", "Enero 2", "Enero 1", "Diciembre 3", "Lista Noviembre - 2"], result.Notes.Select(n => n.Title));
        Assert.Equal([2026, 2026, 2026, 2025, 2025], result.Notes.Select(n => n.Year));
        Assert.Equal(new DateTime(2026, 2, 2), result.Notes[0].Date);           // primer lunes de febrero
        Assert.Equal(new DateTime(2025, 12, 15), result.Notes[3].Date);         // tercera semana de diciembre

        var first = result.Notes[0];
        Assert.Equal(4, first.Items.Count(i => i.Section == NoteSection.Comidas));
        Assert.Equal(2, first.Items.Count(i => i.Section == NoteSection.Carnes));
        Assert.Equal(3, first.Items.Count(i => i.Section == NoteSection.Verduras));
        Assert.Equal(2, first.Items.Count(i => i.Section == NoteSection.Procesados));
        Assert.DoesNotContain(first.Items, i => i.Text.Contains("Papel", StringComparison.Ordinal)); // Limpieza se ignora
    }

    [Fact]
    public void Drive_and_docs_exports_produce_the_same_notes()
    {
        var drive = KeepNotesParser.Parse(Fixtures.Drive, today: Fixtures.Today);
        var docs = KeepNotesParser.Parse(Fixtures.Docs, today: Fixtures.Today);

        Assert.Equal(drive.Notes.Select(n => n.Hash), docs.Notes.Select(n => n.Hash));
        Assert.Equal(drive.Notes.Select(n => n.Date), docs.Notes.Select(n => n.Date));
    }

    [Fact]
    public void Latest_year_can_be_given_and_a_future_month_means_last_year()
    {
        Assert.Equal(2024, KeepNotesParser.Parse(Fixtures.Drive, latestYear: 2024, today: Fixtures.Today).Notes[0].Year);

        // La nota más reciente es de diciembre pero hoy es marzo → es del año anterior.
        var december = KeepNotesParser.Parse("Diciembre 1\n- Comidas\n- Alambre", today: new DateTime(2026, 3, 1));
        Assert.Equal(2025, december.Notes[0].Year);
    }

    [Fact]
    public void Warns_about_duplicated_titles()
    {
        var result = KeepNotesParser.Parse("Septiembre 3\n- Comidas\n- Alambre\n-----\nSeptiembre 3\n- Comidas\n- Teriyaki", today: Fixtures.Today);

        Assert.Equal(2, result.Notes.Count);
        Assert.Contains(result.Warnings, w => w.Contains("dos notas", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(2026, 11, 1, "2026-11-02")]
    [InlineData(2026, 11, 5, "2026-11-30")]
    [InlineData(2026, 2, null, "2026-02-02")]
    [InlineData(2025, 9, 5, "2025-09-29")]
    public void Week_start_is_a_monday_inside_the_month(int year, int month, int? week, string expected) =>
        Assert.Equal(DateTime.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), KeepNotesParser.WeekStart(year, month, week).Date);
}

public class QuantityParserTests
{
    [Theory]
    [InlineData("1 kg piernil deshuesado", 1000, Unit.G, "piernil deshuesado")]
    [InlineData("3/4 kg pechuga", 750, Unit.G, "pechuga")]
    [InlineData("600g carne molida", 600, Unit.G, "carne molida")]
    [InlineData("2L de leche", 2000, Unit.Ml, "leche")]
    [InlineData("3 latas de atún", 3, Unit.Lata, "atún")]
    [InlineData("2 - 3 Jalapeño", 3, Unit.Pieza, "Jalapeño")]
    [InlineData("1.2kg pecho/chamorro", 1200, Unit.G, "pecho/chamorro")]
    public void Parses_quantities(string text, double qty, Unit unit, string name)
    {
        var parsed = QuantityParser.Parse(text);
        Assert.Equal(qty, parsed.Qty);
        Assert.Equal(unit, parsed.Unit);
        Assert.Equal(name, parsed.Name);
    }

    [Fact]
    public void Text_without_quantity_is_kept_as_name()
    {
        var parsed = QuantityParser.Parse("Carne molida 600g");
        Assert.Null(parsed.Qty);
        Assert.Equal("Carne molida 600g", parsed.Name);
    }
}

public class DishMatcherTests
{
    private readonly DishMatcher _matcher = Fixtures.Matcher();

    [Theory]
    [InlineData("Poyo ajonjolí", "pollo-ajonjoli")]
    [InlineData("Mollo con Pole", "pollo-con-mole")]
    [InlineData("Anvorgesas", "hamburguesas")]
    [InlineData("Bongles/Alitas", "boneless")]
    [InlineData("Teriyaki c arroz", "pollo-teriyaki")]
    [InlineData("Sopa de tortiya c/ pollo", "sopa-de-tortilla")]
    [InlineData("Pollo a las hierbas con salsa de tomate", "pollo-a-las-hierbas")]
    [InlineData("Ramyeon negro con pollo", "jajangmyeon")]
    [InlineData("Enchiladas", "enchiladas-suizas")]
    public void Matches_the_way_you_write(string raw, string dishId)
    {
        var match = _matcher.Match(raw);
        Assert.Equal(dishId, match.DishId);
        Assert.Equal(MatchStatus.Matched, match.Status);
    }

    [Fact]
    public void Splits_side_dishes()
    {
        var match = _matcher.Match("Pollo adobado/lentejas");
        Assert.Equal("pollo-adobado", match.DishId);
        Assert.Equal("lentejas", match.Side);
    }

    [Fact]
    public void Ignores_lines_that_are_not_dishes() =>
        Assert.Equal(MatchStatus.Ignored, _matcher.Match("Vinagre de arroz").Status);

    [Fact]
    public void Close_misspellings_are_fuzzy_matches()
    {
        var match = _matcher.Match("Alambreh");
        Assert.Equal(MatchStatus.Fuzzy, match.Status);
        Assert.Equal("alambre", match.DishId);
        Assert.InRange(match.Confidence, DishMatcher.FuzzyThreshold, 0.99);
    }

    [Fact]
    public void Unknown_names_are_left_for_review_with_suggestions()
    {
        var match = _matcher.Match("Platillo misterioso");
        Assert.Equal(MatchStatus.Unmatched, match.Status);
        Assert.Null(match.DishId);
        Assert.Equal(3, match.Suggestions.Count);
    }
}

public class IngredientMatcherTests
{
    [Theory]
    [InlineData("piernil deshuesado", "pierna-cerdo")]
    [InlineData("pechuga", "pechuga-pollo")]
    [InlineData("Pepinos", "pepino")]
    [InlineData("Seboya", "cebolla")]
    [InlineData("Leche deslactosada", "leche")]
    [InlineData("Tomate verde", "tomate-verde")]
    [InlineData("tomates", "jitomate")]
    [InlineData("1 pimiento", "pimiento")]
    [InlineData("Detergente", null)]
    public void Links_purchases_with_the_catalog(string name, string? ingredientId)
    {
        var seed = TestData.Load();
        Assert.Equal(ingredientId, new IngredientMatcher(seed.Ingredients, seed.ImportRules).Match(name));
    }
}

public class ImportServiceTests
{
    [Fact]
    public async Task Imports_history_and_feeds_recipe_stats()
    {
        var store = TestData.Store();
        var service = store.CreateImportService();

        var job = await service.ImportKeepAsync(Fixtures.Drive, "Menus.md", latestYear: null, dryRun: false);

        Assert.Equal(5, job.TotalNotes);
        Assert.Equal(5, job.NewNotes);
        Assert.Equal(13, job.DishOccurrences);
        Assert.Equal(1, job.Ignored);                                  // vinagre de arroz
        Assert.Equal(1, job.Fuzzy);                                    // alambreh
        var unmatched = Assert.Single(job.Unmatched);
        Assert.Equal("platillo misterioso", unmatched.Name);
        Assert.Equal(2, unmatched.Count);
        Assert.Equal(new DateTime(2025, 11, 10), job.FirstDate);
        Assert.Equal(new DateTime(2026, 2, 2), job.LastDate);

        var suizas = store.DishList.Single(d => d.Id == "enchiladas-suizas");
        Assert.Equal(2, suizas.Stats.TimesCooked);
        Assert.Equal(new DateTime(2026, 1, 5), suizas.Stats.LastCooked);
        Assert.Equal(1, suizas.Stats.Months[0]);                       // enero
        Assert.Equal(1, suizas.Stats.Months[10]);                      // noviembre

        var alambre = store.RecipeList.Single(r => r.Id == "alambre");
        Assert.Equal(2, alambre.Stats.HistoryCount);                   // "Alambre" + "Alambreh"
        Assert.Equal(12, alambre.Stats.HistoryMonths.Count);

        Assert.Equal(17, job.PurchaseRecords);
        Assert.Contains(store.Purchases, p => p.IngredientId == "pierna-cerdo" && p.Qty == 1000 && p.Unit == Unit.G);
    }

    [Fact]
    public async Task Reimporting_any_export_format_adds_nothing()
    {
        var store = TestData.Store();
        var service = store.CreateImportService();
        await service.ImportKeepAsync(Fixtures.Drive, null, null, false);

        var again = await service.ImportKeepAsync(Fixtures.Docs, null, null, false);

        Assert.Equal(0, again.NewNotes);
        Assert.Equal(5, again.DuplicateNotes);
        Assert.Equal(13, store.Occurrences.Count);
    }

    [Fact]
    public async Task Dry_run_saves_nothing()
    {
        var store = TestData.Store();

        var job = await store.CreateImportService().ImportKeepAsync(Fixtures.Drive, null, null, dryRun: true);

        Assert.Equal(5, job.NewNotes);
        Assert.Empty(store.Notes);
        Assert.Empty(store.Jobs);
        Assert.All(store.DishList, d => Assert.Equal(0, d.Stats.TimesCooked));
    }

    [Fact]
    public async Task Review_creates_dishes_adds_aliases_and_ignores()
    {
        var store = TestData.Store();
        var service = store.CreateImportService();
        var job = await service.ImportKeepAsync(Fixtures.Drive, null, null, false);

        var reviewed = await service.ReviewAsync(job.Id, new ReviewRequest(
        [
            new ReviewMapping("Platillo misterioso", NewDishName: "Platillo misterioso de la casa"),
            new ReviewMapping("alambreh", DishId: "alambre"),
        ]));

        Assert.Empty(reviewed.Unmatched);
        Assert.Empty(reviewed.FuzzyMatches);
        Assert.Equal(1.0, reviewed.MatchRate);
        var created = store.DishList.Single(d => d.Id == "platillo-misterioso-de-la-casa");
        Assert.Equal(2, created.Stats.TimesCooked);
        Assert.Contains("alambreh", store.DishList.Single(d => d.Id == "alambre").Aliases);

        await service.ReviewAsync(job.Id, new ReviewRequest([new ReviewMapping("Platillo misterioso de la casa", Ignore: true)]));
        Assert.Contains("platillo misterioso de la casa", store.Rules.Ignore);

        await Assert.ThrowsAsync<BadRequestException>(() => service.ReviewAsync(job.Id, new ReviewRequest([new ReviewMapping("x", DishId: "no-existe")])));
    }

    [Fact]
    public async Task Pantry_suggestions_come_from_your_shopping_lists()
    {
        var store = TestData.Store();
        store.Pantry = new Pantry { IngredientIds = ["pepino"] };
        var service = store.CreateImportService();
        await service.ImportKeepAsync(Fixtures.Drive, null, null, false);

        var suggestions = await service.GetPantrySuggestionsAsync(months: 12, minShare: 0.5);

        var pierna = suggestions.Single(s => s.IngredientId == "pierna-cerdo");
        Assert.Equal(3, pierna.WeeksBought);
        Assert.Equal(5, pierna.WeeksInWindow);
        Assert.Equal(1000, pierna.TypicalQty);
        Assert.Equal(Unit.G, pierna.TypicalUnit);
        Assert.True(suggestions.Single(s => s.IngredientId == "pepino").InPantry);
        Assert.Equal(suggestions.OrderByDescending(s => s.Share).Select(s => s.Name), suggestions.Select(s => s.Name));
    }

    [Fact]
    public async Task Rejects_text_without_notes() =>
        await Assert.ThrowsAsync<BadRequestException>(() =>
            TestData.Store().CreateImportService().ImportKeepAsync("hola\nmundo", null, null, false));

    [SkippableFact]
    public async Task Real_document_if_present()
    {
        // data/ está en .gitignore: esta prueba solo corre en tu máquina si guardas ahí tu documento de Keep.
        var file = FindRepoFile(Path.Combine("data", "Menus-drive.md"));
        Skip.If(file is null, "No hay data/Menus-drive.md (tus notas reales no se versionan).");

        var store = TestData.Store();
        var job = await store.CreateImportService().ImportKeepAsync(File.ReadAllText(file!), null, null, dryRun: true);

        Assert.True(job.TotalNotes >= 70, $"notas: {job.TotalNotes}");
        Assert.True(job.MatchRate >= 0.95, $"asignados: {job.MatchRate:P1}; sin asignar: {string.Join(", ", job.Unmatched.Select(u => u.Name))}");
    }

    private static string? FindRepoFile(string relative)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "MenuApp.sln")))
            {
                var path = Path.Combine(dir.FullName, relative);
                return File.Exists(path) ? path : null;
            }
        }

        return null;
    }
}

public class HistoryWeightTests
{
    private static readonly DateTime WeekOf = new(2026, 11, 16, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void History_and_season_raise_the_weight()
    {
        var options = new GenerationOptions();
        var recipe = TestData.Recipe("alambre");
        var candidate = new RecipeCandidate(recipe, "res", true);
        Assert.Equal(1, MenuGenerator.Weight(candidate, WeekOf, options), 3);

        recipe.Stats.HistoryCount = 16;
        recipe.Stats.HistoryLastCooked = WeekOf.AddDays(-60);
        Assert.Equal(9, MenuGenerator.Weight(candidate, WeekOf, options), 3);            // 1 + 2·√16

        recipe.Stats.HistoryMonths = [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 3, 0];               // noviembre
        Assert.Equal(9 * 1.25, MenuGenerator.Weight(candidate, WeekOf, options), 3);

        recipe.Stats.HistoryLastCooked = WeekOf.AddDays(-7);
        Assert.Equal(9 * 1.25 * 0.2, MenuGenerator.Weight(candidate, WeekOf, options), 3);
    }

    [Fact]
    public async Task Imported_favorites_show_up_more_often()
    {
        var store = TestData.Store();
        foreach (var recipe in store.RecipeList)
        {
            recipe.Stats.HistoryCount = recipe.Id == "alambre" ? 25 : 0;
        }

        var service = store.CreateService();
        var hits = 0;
        for (var seed = 1; seed <= 40; seed++)
        {
            var menu = await service.GenerateAsync(new GenerateMenuRequest(3, new DateOnly(2026, 11, 16), Seed: seed));
            hits += menu.Dishes.Count(d => d.RecipeId == "alambre");
        }

        // Sin historial, alambre saldría en ~3/25 de los platillos (≈5 de 40 menús); con 25 veces en Keep sale mucho más.
        Assert.True(hits >= 15, $"alambre salió {hits} de 40 veces");
    }
}

public class SeedSyncServiceTests
{
    [Fact]
    public async Task Brings_new_seed_into_an_existing_database_without_losing_stats()
    {
        var store = TestData.Store();
        var seed = TestData.Load();

        // Base "vieja": sin los ingredientes nuevos, sin diccionario, con una receta inventada y estadísticas propias.
        store.IngredientList.RemoveAll(i => i.Id is "pierna-cerdo" or "queso-panela");
        store.DishList.Clear();
        store.Rules = new ImportRules();
        store.RecipeList.Add(new Recipe { Id = "fajitas-res", Name = "Fajitas", Source = SeedSyncService.RemovedSource });
        store.RecipeList.Single(r => r.Id == "alambre").Stats.TimesCooked = 4;

        var sync = new SeedSyncService(store, store, store, store, store, store.CreateImportService());
        var result = await sync.SyncAsync(seed);

        Assert.Equal(2, result.IngredientsAdded);
        Assert.Equal(1, result.RecipesRemoved);
        Assert.Equal(seed.Dishes.Count, result.DishesAdded);
        Assert.DoesNotContain(store.RecipeList, r => r.Id == "fajitas-res");
        Assert.Equal(4, store.RecipeList.Single(r => r.Id == "alambre").Stats.TimesCooked);
        Assert.NotEmpty(store.Rules.SpellingFixes);

        var again = await sync.SyncAsync(seed);
        Assert.Equal(0, again.IngredientsAdded + again.RecipesRemoved + again.DishesAdded + again.DishesUpdated + again.RulesAdded);
    }
}
