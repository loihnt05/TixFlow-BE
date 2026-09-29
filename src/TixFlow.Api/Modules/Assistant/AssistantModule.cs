namespace TixFlow.Api.Modules.Assistant;

public static class AssistantModule
{
    public static IEndpointRouteBuilder MapAssistantModule(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGroup("/assistant")
            .WithTags("Assistant")
            .MapGet("/status", () => Results.Ok(new
            {
                module = "AI Booking Assistant",
                status = "planned",
                guardrail = "All state changes must go through Booking APIs after user confirmation."
            }));

        return endpoints;
    }
}

