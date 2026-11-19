using System.Text;
using System.Text.Json;
using FamilyResilience.Application.Questionnaires;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.Extensions.Options;
using MvcJsonOptions = Microsoft.AspNetCore.Mvc.JsonOptions;

namespace FamilyResilience.Api.Questionnaires;

/// <summary>
/// Reads the content-import and instrument-create bodies with <see cref="ContentJson.Options"/>, the same strict rules as
/// the embedded content files: an unknown, missing, duplicate or case-variant property or a number in a string is a
/// 400, never a silent default. Every other body keeps MVC's JSON formatter. A <see cref="JsonException"/> is reported
/// exactly as that formatter reports it: under its JSON path, and with its message only when
/// <see cref="MvcJsonOptions.AllowInputFormatterExceptionMessages"/> allows.
/// </summary>
public sealed class ContentJsonInputFormatter : TextInputFormatter
{
    private static readonly Type[] StrictTypes = [typeof(InstrumentContentDocument), typeof(CreateInstrumentRequest)];

    public ContentJsonInputFormatter()
    {
        SupportedEncodings.Add(UTF8EncodingWithoutBOM);
        SupportedEncodings.Add(UTF16EncodingLittleEndian);
        SupportedMediaTypes.Add("application/json");
        SupportedMediaTypes.Add("text/json");
        SupportedMediaTypes.Add("application/*+json");
    }

    protected override bool CanReadType(Type type) => StrictTypes.Contains(type);

    public override async Task<InputFormatterResult> ReadRequestBodyAsync(InputFormatterContext context, Encoding encoding)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(encoding);

        var httpContext = context.HttpContext;
        var transcode = encoding.CodePage != Encoding.UTF8.CodePage;
        var body = transcode
            ? Encoding.CreateTranscodingStream(httpContext.Request.Body, encoding, Encoding.UTF8, leaveOpen: true)
            : httpContext.Request.Body;

        object? model;
        try
        {
            model = await JsonSerializer.DeserializeAsync(body, context.ModelType, ContentJson.Options, httpContext.RequestAborted);
        }
        catch (JsonException exception)
        {
            var allowMessages = httpContext.RequestServices.GetRequiredService<IOptions<MvcJsonOptions>>().Value
                .AllowInputFormatterExceptionMessages;
            context.ModelState.TryAddModelError(
                exception.Path ?? string.Empty,
                allowMessages ? new InputFormatterException(exception.Message, exception) : exception,
                context.Metadata);
            return InputFormatterResult.Failure();
        }
        finally
        {
            if (transcode)
            {
                await body.DisposeAsync();
            }
        }

        // The JSON literal null is no body at all, as MVC's own formatter treats it.
        return model is null && !context.TreatEmptyInputAsDefaultValue
            ? InputFormatterResult.NoValue()
            : InputFormatterResult.Success(model);
    }
}
