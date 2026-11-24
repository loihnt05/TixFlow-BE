using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace TixFlow.Api.Modules.WaitingRoom;

public sealed class WaitingRoomHoldHeaderOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        if (context.ApiDescription.HttpMethod != "POST"
            || context.ApiDescription.RelativePath?.TrimEnd('/') != "api/v1/holds") return;

        operation.Parameters ??= new List<OpenApiParameter>();
        AddHeader(operation, "Idempotency-Key", true,
            "Required for Hold creation and replay of the same intent.");
        AddHeader(operation, "X-Admission-Token", false,
            "Required when this event session's Waiting Room policy is Active or Paused; omitted when Disabled.");
    }

    private static void AddHeader(OpenApiOperation operation, string name, bool required, string description)
    {
        if (operation.Parameters.Any(parameter => parameter.In == ParameterLocation.Header
            && string.Equals(parameter.Name, name, StringComparison.OrdinalIgnoreCase))) return;
        operation.Parameters.Add(new OpenApiParameter
        {
            Name = name, In = ParameterLocation.Header, Required = required,
            Description = description, Schema = new OpenApiSchema { Type = "string" }
        });
    }
}
