using MenuPlanner.Application;
using MenuPlanner.Portioning;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace MenuPlanner.Api;

/// <summary>Traduce excepciones de la aplicación a ProblemDetails con el código HTTP adecuado.</summary>
public sealed class AppExceptionHandler(IProblemDetailsService problemDetails, ILogger<AppExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var (status, title) = exception switch
        {
            NotFoundException => (StatusCodes.Status404NotFound, "No encontrado"),
            ConflictException => (StatusCodes.Status409Conflict, "Conflicto"),
            BadRequestException => (StatusCodes.Status400BadRequest, "Solicitud inválida"),
            ConversionException => (StatusCodes.Status422UnprocessableEntity, "No se pudo porcionar"),
            BadHttpRequestException bad => (bad.StatusCode, "Solicitud inválida"),
            _ => (0, string.Empty),
        };

        if (status == 0)
        {
            logger.LogError(exception, "Error no controlado en {Path}", httpContext.Request.Path);
            return false;
        }

        logger.LogInformation("{Title}: {Message}", title, exception.Message);
        httpContext.Response.StatusCode = status;

        var details = new ProblemDetails { Status = status, Title = title, Detail = exception.Message };
        if (exception is BadRequestException { Errors.Count: > 0 } badRequest)
        {
            details.Extensions["errors"] = badRequest.Errors;
        }

        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = details,
            Exception = exception,
        });
    }
}
