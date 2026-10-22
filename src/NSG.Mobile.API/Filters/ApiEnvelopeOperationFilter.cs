using Microsoft.AspNetCore.Mvc;
using Microsoft.OpenApi.Models;
using NSG.Mobile.Application.Common;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace NSG.Mobile.API.Filters;

/// <summary>
/// Documents the standard <c>{ success, message, data }</c> envelope in Swagger: each JSON response is shown as the envelope with its
/// payload type in <c>data</c>; problem and bodyless responses are shown as the envelope with <c>data</c> null; a 204 is shown as 200.
/// File downloads are left as they are.
/// </summary>
public sealed class ApiEnvelopeOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        // Build the replacement set first, so the response dictionary is not modified while it is read.
        List<(string Code, OpenApiResponse Response)> replacements = new();

        foreach (KeyValuePair<string, OpenApiResponse> entry in operation.Responses)
        {
            if (!int.TryParse(entry.Key, out int status)) continue;

            Type? payload = context.ApiDescription.SupportedResponseTypes
                .FirstOrDefault(r => r.StatusCode == status)?.Type;

            if (payload != null && IsFile(payload)) continue;

            OpenApiSchema envelope = status >= StatusCodes.Status400BadRequest
                ? Envelope(context, success: false, payload: null)
                : Envelope(context, success: true, payload: Unwrap(payload));

            string code = status == StatusCodes.Status204NoContent ? "200" : entry.Key;
            replacements.Add((code, new OpenApiResponse
            {
                Description = status == StatusCodes.Status204NoContent ? "Success. The message states the outcome; data is null." : entry.Value.Description,
                Content = { ["application/json"] = new OpenApiMediaType { Schema = envelope } }
            }));
            operation.Responses.Remove(entry.Key);
        }

        foreach ((string code, OpenApiResponse response) in replacements)
            operation.Responses[code] = response;
    }

    private static bool IsFile(Type type) => typeof(FileResult).IsAssignableFrom(type);

    // The service-level wrappers are flattened by the filter at run time: their message becomes `message`, their payload `data`.
    private static Type? Unwrap(Type? type)
    {
        if (type == null || type == typeof(void) || type == typeof(ApiMessageResponse) || type == typeof(ProblemDetails)) return null;
        if (type.IsGenericType)
        {
            Type definition = type.GetGenericTypeDefinition();
            if (definition == typeof(ApiResult<>) || definition == typeof(ApiResponse<>)) return type.GetGenericArguments()[0];
        }
        return type;
    }

    private static OpenApiSchema Envelope(OperationFilterContext context, bool success, Type? payload)
    {
        OpenApiSchema data = payload == null
            ? new OpenApiSchema { Nullable = true, Description = "Always null." }
            : context.SchemaGenerator.GenerateSchema(payload, context.SchemaRepository);

        return new OpenApiSchema
        {
            Type = "object",
            Properties =
            {
                ["success"] = new OpenApiSchema { Type = "boolean", Example = new Microsoft.OpenApi.Any.OpenApiBoolean(success) },
                ["message"] = new OpenApiSchema { Type = "string", Description = "Human-readable, client-safe message." },
                ["data"] = data
            },
            Required = new HashSet<string> { "success", "message", "data" }
        };
    }
}
