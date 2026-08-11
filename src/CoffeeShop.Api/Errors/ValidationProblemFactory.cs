using FluentValidation;
using Microsoft.AspNetCore.Http;

namespace CoffeeShop.Api.Errors;

public static class ValidationProblemFactory
{
    public static HttpValidationProblemDetails Create(ValidationException exception)
    {
        var errors = exception.Errors
            .GroupBy(failure => failure.PropertyName, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Select(failure => failure.ErrorMessage)
                    .OrderBy(message => message, StringComparer.Ordinal)
                    .ToArray(),
                StringComparer.Ordinal);

        return new HttpValidationProblemDetails(errors)
        {
            Type = ProblemTypes.Validation,
            Title = ProblemTypes.ValidationTitle,
            Status = StatusCodes.Status400BadRequest
        };
    }
}
