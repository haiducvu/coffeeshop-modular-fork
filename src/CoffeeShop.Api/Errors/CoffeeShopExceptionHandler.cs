using System.Diagnostics;
using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace CoffeeShop.Api.Errors;

public sealed class CoffeeShopExceptionHandler(
    ILogger<CoffeeShopExceptionHandler> logger,
    IProblemDetailsService problemDetailsService) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var problemDetails = exception switch
        {
            ValidationException validationException => ValidationProblemFactory.Create(validationException),
            OrderNotFoundException => ProblemTypes.Create(
                ProblemTypes.OrderNotFound,
                ProblemTypes.OrderNotFoundTitle,
                StatusCodes.Status404NotFound),
            OrderConcurrencyException => ProblemTypes.Create(
                ProblemTypes.OrderConflict,
                ProblemTypes.OrderConflictTitle,
                StatusCodes.Status409Conflict),
            _ => CreateUnexpectedProblem(httpContext, exception)
        };

        httpContext.Response.StatusCode = problemDetails.Status
            ?? StatusCodes.Status500InternalServerError;
        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problemDetails
        });
    }

    private ProblemDetails CreateUnexpectedProblem(HttpContext httpContext, Exception exception)
    {
        logger.LogError(
            exception,
            "Unhandled exception for trace ID {TraceId}",
            Activity.Current?.Id ?? httpContext.TraceIdentifier);

        return ProblemTypes.Create(
            ProblemTypes.Internal,
            ProblemTypes.InternalTitle,
            StatusCodes.Status500InternalServerError);
    }
}
