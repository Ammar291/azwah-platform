using System.Text.Json;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;

namespace FamilyResilience.Api.Validation;

/// <summary>
/// Runs the FluentValidation validator registered for each action argument before the action executes, and
/// answers 400 ValidationProblemDetails when any rule fails.
/// </summary>
public sealed class ValidationFilter : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var services = context.HttpContext.RequestServices;
        foreach (var argument in context.ActionArguments.Values)
        {
            if (argument is null)
            {
                continue;
            }

            var validatorType = typeof(IValidator<>).MakeGenericType(argument.GetType());
            if (services.GetService(validatorType) is not IValidator validator)
            {
                continue;
            }

            var result = await validator.ValidateAsync(
                new ValidationContext<object>(argument), context.HttpContext.RequestAborted);
            foreach (var failure in result.Errors)
            {
                context.ModelState.AddModelError(ToCamelCasePath(failure.PropertyName), failure.ErrorMessage);
            }
        }

        if (!context.ModelState.IsValid)
        {
            var problem = services.GetRequiredService<ProblemDetailsFactory>()
                .CreateValidationProblemDetails(context.HttpContext, context.ModelState);
            CamelCaseKeys(problem);
            context.Result = new ObjectResult(problem)
            {
                StatusCode = problem.Status,
                ContentTypes = { "application/problem+json" },
            };
            return;
        }

        await next();
    }

    // Query-bound models enter ModelState under their PascalCase property names; bodies under their JSON names. Report
    // every key in camelCase, merging the messages of keys that collide, each message once and in order.
    private static void CamelCaseKeys(ValidationProblemDetails problem)
    {
        var merged = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var (key, messages) in problem.Errors)
        {
            var camelKey = ToCamelCasePath(key);
            if (!merged.TryGetValue(camelKey, out var list))
            {
                merged[camelKey] = list = [];
            }

            foreach (var message in messages.Where(message => !list.Contains(message, StringComparer.Ordinal)))
            {
                list.Add(message);
            }
        }

        problem.Errors.Clear();
        foreach (var (key, messages) in merged)
        {
            problem.Errors[key] = [.. messages];
        }
    }

    // "Address.Street" becomes "address.street", matching the camelCase JSON the client sent.
    private static string ToCamelCasePath(string propertyName) =>
        string.Join('.', propertyName.Split('.').Select(JsonNamingPolicy.CamelCase.ConvertName));
}
