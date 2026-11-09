using Microsoft.EntityFrameworkCore;
using Npgsql;
using TixFlow.Application.Identity;
using TixFlow.Domain.Identity;
using TixFlow.Infrastructure.Persistence;

namespace TixFlow.Infrastructure.Identity;

public sealed class LocalUserSynchronizer(TixFlowDbContext dbContext) : ILocalUserSynchronizer
{
    public async Task<User> SynchronizeAsync(IdentityProfile profile, CancellationToken cancellationToken)
    {
        string email = profile.Email.Trim();
        string normalizedEmail = email.ToUpperInvariant();
        string role = profile.PrimaryRole.ToString();
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                // Parameterized PostgreSQL upsert: the unique subject index arbitrates concurrent first logins.
                // Existing rows are NEVER claimed by matching an email. Their status is also never reset.
                // A token phone only seeds an empty local phone; a phone saved via PATCH /users/me always wins.
                await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO users
                        (id, identity_subject, email, normalized_email, display_name, phone_number, password_hash, role, status)
                    VALUES
                        ({Guid.NewGuid()}, {profile.Subject}, {email}, {normalizedEmail}, {profile.Name}, {profile.PhoneNumber}, NULL, {role}, 'Active')
                    ON CONFLICT (identity_subject) DO UPDATE
                    SET email = EXCLUDED.email,
                        normalized_email = EXCLUDED.normalized_email,
                        display_name = EXCLUDED.display_name,
                        phone_number = COALESCE(users.phone_number, EXCLUDED.phone_number),
                        role = EXCLUDED.role,
                        updated_at_utc = now()
                    WHERE (users.email, users.normalized_email, users.display_name, users.role, users.phone_number)
                        IS DISTINCT FROM
                        (EXCLUDED.email, EXCLUDED.normalized_email, EXCLUDED.display_name, EXCLUDED.role,
                         COALESCE(users.phone_number, EXCLUDED.phone_number))
                    """, cancellationToken);
                break;
            }
            catch (PostgresException exception) when (
                exception.SqlState == PostgresErrorCodes.UniqueViolation
                && exception.ConstraintName == "ux_users_normalized_email")
            {
                // PostgreSQL can arbitrate the email index before the subject index on concurrent
                // first logins. Retry only if the winning row belongs to this exact OIDC subject.
                if (attempt == 0 && await dbContext.Users.AsNoTracking().AnyAsync(
                    user => user.IdentitySubject == profile.Subject && user.NormalizedEmail == normalizedEmail,
                    cancellationToken)) continue;
                throw new ProfileConflictException(exception);
            }
        }

        return await dbContext.Users.AsNoTracking()
            .SingleAsync(user => user.IdentitySubject == profile.Subject, cancellationToken);
    }
}
