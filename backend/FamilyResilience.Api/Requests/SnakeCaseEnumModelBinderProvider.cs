using System.Text.Json;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace FamilyResilience.Api.Requests;

/// <summary>
/// Binds enum values in query strings and routes by name, written the way JSON writes them (<c>in_progress</c>) or as the
/// C# name (<c>InProgress</c>). Numbers and unknown names are refused, so <c>?status=1</c> is a 400 like any other bad
/// value; an empty value (<c>?status=</c>) leaves a nullable enum unset, as MVC does. The 400 carries a fixed message:
/// what the client sent is never repeated in a response. Bodies, headers and services keep their own binders.
/// </summary>
public sealed class SnakeCaseEnumModelBinderProvider : IModelBinderProvider
{
    public const string InvalidValueMessage = "The value is not one of the allowed values.";

    public IModelBinder? GetBinder(ModelBinderProviderContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var source = context.BindingInfo.BindingSource;
        if (source is not null && source != BindingSource.Query && source != BindingSource.Path && source != BindingSource.ModelBinding)
        {
            return null;
        }

        var nullable = Nullable.GetUnderlyingType(context.Metadata.ModelType);
        var type = nullable ?? context.Metadata.ModelType;
        return type.IsEnum ? new Binder(type, nullable is not null) : null;
    }

    private sealed class Binder(Type enumType, bool nullable) : IModelBinder
    {
        private readonly Dictionary<string, object> _byName = Enum.GetValues(enumType).Cast<object>()
            .SelectMany(value => new[]
            {
                (Name: value.ToString()!, Value: value),
                (Name: JsonNamingPolicy.SnakeCaseLower.ConvertName(value.ToString()!), Value: value),
            })
            .DistinctBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(entry => entry.Name, entry => entry.Value, StringComparer.OrdinalIgnoreCase);

        public Task BindModelAsync(ModelBindingContext bindingContext)
        {
            ArgumentNullException.ThrowIfNull(bindingContext);

            var value = bindingContext.ValueProvider.GetValue(bindingContext.ModelName);
            if (value == ValueProviderResult.None)
            {
                return Task.CompletedTask;
            }

            bindingContext.ModelState.SetModelValue(bindingContext.ModelName, value);
            if (nullable && string.IsNullOrWhiteSpace(value.FirstValue))
            {
                bindingContext.Result = ModelBindingResult.Success(null);
            }
            else if (value.FirstValue is { Length: > 0 } text && _byName.TryGetValue(text, out var parsed))
            {
                bindingContext.Result = ModelBindingResult.Success(parsed);
            }
            else
            {
                bindingContext.ModelState.TryAddModelError(bindingContext.ModelName, InvalidValueMessage);
            }

            return Task.CompletedTask;
        }
    }
}
