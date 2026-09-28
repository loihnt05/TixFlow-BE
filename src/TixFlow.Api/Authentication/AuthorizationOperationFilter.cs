using Microsoft.AspNetCore.Authorization;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace TixFlow.Api.Authentication;

public sealed class AuthorizationOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        IList<object> metadata = context.ApiDescription.ActionDescriptor.EndpointMetadata;
        if (!metadata.OfType<IAuthorizeData>().Any() || metadata.OfType<IAllowAnonymous>().Any()) return;
        operation.Security = [new OpenApiSecurityRequirement
        {
            [new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "oidc" }
            }] = ["openid", "profile", "email"]
        }];
        operation.Responses.TryAdd("401", new OpenApiResponse { Description = "Authentication required" });
        operation.Responses.TryAdd("403", new OpenApiResponse { Description = "Role or local account status forbids access" });
    }
}
