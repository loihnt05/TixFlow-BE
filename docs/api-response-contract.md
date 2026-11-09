# Shared API response templates

Success: `ApiResponse<T>` from `TixFlow.Api.Contracts`.

```json
{"data":{"id":"..."},"message":"Request completed successfully.","success":true}
```

Use `Results.Ok(new ApiResponse<MyDto>(dto))` or `Results.Created(location, new ApiResponse<MyDto>(dto, "Created successfully."))`. Keep the actual HTTP status (200/201); 204 has no response body.

Error: `ApiErrorResponse.Create(context, status, code, message, errors, retryable).ToResult()`.

```json
{"type":"about:blank","title":"VALIDATION_ERROR","status":422,"detail":"Check your input.","instance":"/api/v1/holds","code":"VALIDATION_ERROR","traceId":"...","retryable":false,"errors":{"quantity":["Must be positive."]},"success":false,"message":"Check your input."}
```

Errors use `application/problem+json` with the actual 4xx/5xx status. `message` aliases `detail` for compatibility. `errors` is optional. `code` is the machine-readable discriminator; do not branch on human-readable message text. `traceId` comes from the server request, never from user input. Do not supply raw exception messages, credentials, or SQL in `message`. `ApiMessages` provides neutral defaults by HTTP status; a 409 does not imply an email conflict. Retryable must be explicitly selected by the endpoint.

Adoption: Booking hold success envelopes and Booking/Orders error helpers use these shared types. Existing stored idempotent response bodies are replayed unchanged and may lack the new additive fields. Other existing endpoint shapes (catalog, profile, ticket responses, list pagination) remain unchanged until migrated with their frontend consumers. This is not a global response rewriting middleware; it does not yet normalize every exception, authentication response or existing ProblemDetails endpoint. Use these types for new endpoints and declare `.Produces<ApiResponse<MyDto>>()` / `.Produces<ApiErrorResponse>(422, "application/problem+json")` with their real status codes for OpenAPI.
