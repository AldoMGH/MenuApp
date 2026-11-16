using MenuPlanner.Web.Services;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace MenuPlanner.Web.Components;

/// <summary>Base de las páginas: acceso a la API y manejo uniforme de errores y "ocupado".</summary>
public abstract class AppPage : ComponentBase
{
    [Inject]
    protected ApiClient Api { get; set; } = default!;

    [Inject]
    protected ISnackbar Snackbar { get; set; } = default!;

    [Inject]
    protected NavigationManager Nav { get; set; } = default!;

    protected bool Busy { get; private set; }

    /// <summary>Ejecuta una llamada a la API; muestra el error como aviso y regresa false si falla.</summary>
    protected async Task<bool> Run(Func<Task> action, string? success = null)
    {
        Busy = true;
        StateHasChanged();
        try
        {
            await action();
            if (success is not null)
            {
                Snackbar.Add(success, Severity.Success);
            }

            return true;
        }
        catch (ApiException ex)
        {
            Snackbar.Add(ex.Errors.Count == 0 ? ex.Message : $"{ex.Message} {string.Join(" · ", ex.Errors.Take(3))}", Severity.Warning);
            return false;
        }
        catch (HttpRequestException ex)
        {
            Snackbar.Add($"No se pudo conectar con la API: {ex.Message}", Severity.Error);
            return false;
        }
        finally
        {
            Busy = false;
            StateHasChanged();
        }
    }
}
