using System.ComponentModel;
using System.Net.Mail;
using System.Reflection;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using AcceptanceTestsWebAPI.Configuration;
using AcceptanceTestsWebAPI.Requests;

// Load environment variables from .env file.
dotenv.net.DotEnv.Load();
WebApplicationBuilder builder = Builder.CreateBuilder(args);
WebApplication app = Application.CreateApplication(builder);

app.MapGet("/", Generic.Default)
    .AllowAnonymous()
    .Produces(StatusCodes.Status200OK);

app.MapGet("/api/health", Generic.Health)
    .AllowAnonymous()
    .Produces(StatusCodes.Status200OK);

app.MapGet("/api/pr/open", PullRequestEndpoints.Open)
    .AllowAnonymous()
    .Produces(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status400BadRequest)
    .Produces(StatusCodes.Status404NotFound)
    .ProducesProblem(StatusCodes.Status500InternalServerError);

app.MapGet("/api/pr/start", PullRequestEndpoints.Start)
    .AllowAnonymous()
    .Produces(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status400BadRequest)
    .Produces(StatusCodes.Status404NotFound)
    .ProducesProblem(StatusCodes.Status500InternalServerError);

app.MapGet("/api/pr/jobcomplete", PullRequestEndpoints.JobComplete)
    .AllowAnonymous()
    .Produces(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status400BadRequest)
    .Produces(StatusCodes.Status404NotFound)
    .ProducesProblem(StatusCodes.Status500InternalServerError);

app.MapGet("/api/test/clear", PullRequestEndpoints.Clear)
    .AllowAnonymous()
    .Produces(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status400BadRequest)
    .Produces(StatusCodes.Status404NotFound)
    .ProducesProblem(StatusCodes.Status500InternalServerError);

app.Run();

/// <summary>Marker type used by WebApplicationFactory to locate the AcceptanceTestsWebAPI entry point.</summary>
//public class AcceptanceTestsWebApiMarker { }

/*
const string downloadAccessPurposeClaim = "download-access";

app.MapGet("/api/downloads/link", async (string email, AcceptanceTestsDbContext db) =>
{
    if (string.IsNullOrWhiteSpace(downloadBaseUrl))
    {
        return Results.Problem("Download base URL is not configured.", statusCode: StatusCodes.Status500InternalServerError);
    }

    if (string.IsNullOrWhiteSpace(email))
    {
        return Results.BadRequest("Email is required.");
    }

    string normalizedEmail;
    try
    {
        normalizedEmail = new MailAddress(email.Trim()).Address;
    }
    catch (FormatException)
    {
        return Results.BadRequest("Email is not valid.");
    }

    var user = await db.Users
        .AsNoTracking()
        .FirstOrDefaultAsync(x => x.Email == normalizedEmail);

    if (user is null || user.LicenceStatus is UserLicenceStatus.None or UserLicenceStatus.Pending)
    {
        return Results.NotFound("No eligible AcceptanceTests was found for that email address.");
    }

    var now = DateTime.UtcNow;
    var expiresAt = now.AddHours(downloadTokenLifetimeHours);
    var claims = new[]
    {
        new Claim(JwtRegisteredClaimNames.Sub, normalizedEmail),
        new Claim(JwtRegisteredClaimNames.Email, normalizedEmail),
        new Claim("purpose", downloadAccessPurposeClaim),
        new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
    };

    var signingCredentials = new SigningCredentials(
        new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSigningKey)),
        SecurityAlgorithms.HmacSha256);

    var token = new JwtSecurityToken(
        issuer: jwtIssuer,
        audience: jwtAudience,
        claims: claims,
        notBefore: now,
        expires: expiresAt,
        signingCredentials: signingCredentials);

    var tokenValue = new JwtSecurityTokenHandler().WriteToken(token);
    var downloadPageUrl = new Uri(new Uri(downloadBaseUrl), "download").ToString();
    var downloadLink = $"{downloadPageUrl}?token={Uri.EscapeDataString(tokenValue)}";

    if (mailUtility is not null)
    {
        await mailUtility.SendDownloadLinkEmailAsync(normalizedEmail, downloadLink);
        return Results.Ok(new { message = "Download link has been sent to your email address. It expires in 48 hours." });
    }
    else
    {
        return Results.Problem("Email service is not configured.", statusCode: StatusCodes.Status500InternalServerError);
    }
})
    .AllowAnonymous()
    .RequireRateLimiting("public-downloads")
    .WithName("CreateDownloadAccessLink")
    .WithTags("Downloads")
    .Produces(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status400BadRequest)
    .Produces(StatusCodes.Status404NotFound)
    .ProducesProblem(StatusCodes.Status500InternalServerError);

app.MapGet("/api/downloads/validate", (string token) =>
{
    var tokenValidation = ValidateDownloadToken(
        token,
        jwtIssuer,
        jwtAudience,
        jwtSigningKey,
        downloadAccessPurposeClaim);

    if (tokenValidation.FailureResult is not null)
    {
        return tokenValidation.FailureResult;
    }

    return Results.Ok(new
    {
        isValid = true,
        email = tokenValidation.Email,
        expiresAtUtc = tokenValidation.ExpiresAtUtc
    });
})
    .AllowAnonymous()
    .RequireRateLimiting("public-downloads")
    .WithName("ValidateDownloadAccessToken")
    .WithTags("Downloads")
    .Produces(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status400BadRequest)
    .Produces(StatusCodes.Status401Unauthorized);

app.MapPost("/api/downloads/events", async (DownloadEventRequest request, AcceptanceTestsDbContext db) =>
{
    if (string.IsNullOrWhiteSpace(request.DownloadType))
    {
        return Results.BadRequest("Download type is required.");
    }

    if (string.IsNullOrWhiteSpace(request.Version))
    {
        return Results.BadRequest("Version is required.");
    }

    var normalizedDownloadType = request.DownloadType.Trim().ToLowerInvariant();
    if (normalizedDownloadType is not ("nextgen" or "classic"))
    {
        return Results.BadRequest("Download type must be either 'nextgen' or 'classic'.");
    }

    var tokenValidation = ValidateDownloadToken(
        request.Token,
        jwtIssuer,
        jwtAudience,
        jwtSigningKey,
        downloadAccessPurposeClaim);

    if (tokenValidation.FailureResult is not null)
    {
        return tokenValidation.FailureResult;
    }

    var email = tokenValidation.Email!;
    var userId = await db.Users
        .AsNoTracking()
        .Where(x => x.Email == email)
        .Select(x => (Guid?)x.Id)
        .FirstOrDefaultAsync();

    var entity = new DownloadAuditEntity
    {
        Id = Guid.NewGuid(),
        DownloadedAtUtc = DateTime.UtcNow,
        UserEmail = email,
        UserId = userId,
        DownloadType = normalizedDownloadType,
        Version = request.Version.Trim(),
        Platform = string.IsNullOrWhiteSpace(request.Platform) ? null : request.Platform.Trim(),
        DownloadUrl = string.IsNullOrWhiteSpace(request.DownloadUrl) ? null : request.DownloadUrl.Trim()
    };

    db.DownloadAudits.Add(entity);
    await db.SaveChangesAsync();

    return Results.Ok(new
    {
        id = entity.Id,
        downloadedAtUtc = entity.DownloadedAtUtc
    });
})
    .AllowAnonymous()
    .RequireRateLimiting("public-downloads")
    .WithName("RecordDownloadEvent")
    .WithTags("Downloads")
    .Produces(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status400BadRequest)
    .Produces(StatusCodes.Status401Unauthorized);

app.MapGet(
    "/api/downloads/events",
    async (
        AcceptanceTestsDbContext db,
        DateTime? fromUtc,
        DateTime? toUtc,
        string? email,
        string? downloadType,
        int? skip,
        int? take) =>
    {
        var normalizedSkip = Math.Max(0, skip ?? 0);
        var normalizedTake = Math.Clamp(take ?? 100, 1, 500);

        var query = db.DownloadAudits.AsNoTracking().AsQueryable();

        if (fromUtc.HasValue)
        {
            query = query.Where(x => x.DownloadedAtUtc >= fromUtc.Value);
        }

        if (toUtc.HasValue)
        {
            query = query.Where(x => x.DownloadedAtUtc <= toUtc.Value);
        }

        if (!string.IsNullOrWhiteSpace(email))
        {
            var normalizedEmail = email.Trim();
            query = query.Where(x => x.UserEmail == normalizedEmail);
        }

        if (!string.IsNullOrWhiteSpace(downloadType))
        {
            var normalizedType = downloadType.Trim().ToLowerInvariant();
            if (normalizedType is not ("nextgen" or "classic"))
            {
                return Results.BadRequest("Download type must be either 'nextgen' or 'classic'.");
            }

            query = query.Where(x => x.DownloadType == normalizedType);
        }

        var total = await query.CountAsync();
        var entities = await query
            .OrderByDescending(x => x.DownloadedAtUtc)
            .ThenByDescending(x => x.Id)
            .Skip(normalizedSkip)
            .Take(normalizedTake)
            .ToListAsync();

        var items = entities.Select(ToDownloadAuditResponse).ToList();

        return Results.Ok(new
        {
            total,
            skip = normalizedSkip,
            take = normalizedTake,
            items
        });
    })
    .RequireRateLimiting("authenticated-api")
    .RequireAuthorization()
    .WithName("ListDownloadEvents")
    .WithTags("Downloads")
    .Produces(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status400BadRequest)
    .Produces(StatusCodes.Status401Unauthorized);

app.MapGet(
    "/api/downloads/events/export",
    async (
        AcceptanceTestsDbContext db,
        DateTime? fromUtc,
        DateTime? toUtc,
        string? email,
        string? downloadType) =>
    {
        var query = db.DownloadAudits.AsNoTracking().AsQueryable();

        if (fromUtc.HasValue)
        {
            query = query.Where(x => x.DownloadedAtUtc >= fromUtc.Value);
        }

        if (toUtc.HasValue)
        {
            query = query.Where(x => x.DownloadedAtUtc <= toUtc.Value);
        }

        if (!string.IsNullOrWhiteSpace(email))
        {
            var normalizedEmail = email.Trim();
            query = query.Where(x => x.UserEmail == normalizedEmail);
        }

        if (!string.IsNullOrWhiteSpace(downloadType))
        {
            var normalizedType = downloadType.Trim().ToLowerInvariant();
            if (normalizedType is not ("nextgen" or "classic"))
            {
                return Results.BadRequest("Download type must be either 'nextgen' or 'classic'.");
            }

            query = query.Where(x => x.DownloadType == normalizedType);
        }

        var audits = await query
            .OrderByDescending(x => x.DownloadedAtUtc)
            .ThenByDescending(x => x.Id)
            .ToListAsync();

        var csv = BuildDownloadAuditCsv(audits);
        var fileName = $"download-events-{DateTime.UtcNow:yyyyMMddHHmmss}.csv";
        return Results.File(Encoding.UTF8.GetBytes(csv), "text/csv; charset=utf-8", fileName);
    })
    .RequireRateLimiting("authenticated-api")
    .RequireAuthorization()
    .WithName("ExportDownloadEventsCsv")
    .WithTags("Downloads")
    .Produces(StatusCodes.Status200OK, contentType: "text/csv")
    .Produces(StatusCodes.Status400BadRequest)
    .Produces(StatusCodes.Status401Unauthorized);

var users = app.MapGroup("/api/users")
    .WithTags("Users")
    .RequireRateLimiting("authenticated-api")
    .RequireAuthorization();

users.MapGet("/", async (AcceptanceTestsDbContext db) =>
{
    if (!await db.Users.AnyAsync())
    {
        return Results.Ok(Array.Empty<User>());
    }

    var entities = await db.Users
        .AsNoTracking()
        .OrderBy(x => x.Email)
        .ToListAsync();

    return Results.Ok(entities.Select(ToUserModel));
})
    .WithName("ListUsers")
    .Produces<List<User>>(StatusCodes.Status200OK);

users.MapGet("/{id:guid}", async (Guid id, AcceptanceTestsDbContext db) =>
{
    var entity = await db.Users
        .AsNoTracking()
        .FirstOrDefaultAsync(x => x.Id == id);

    return entity is null ? Results.NotFound() : Results.Ok(ToUserModel(entity));
})
    .WithName("GetUserById")
    .Produces<User>(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status404NotFound);

users.MapPost("/", async (User user, AcceptanceTestsDbContext db, HttpContext http) =>
{
    var validationError = await ValidateUserAsync(user, db);
    if (validationError is not null)
    {
        return validationError;
    }

    var entity = ToUserEntity(user);
    if (entity.Id == Guid.Empty)
    {
        entity.Id = Guid.NewGuid();
    }

    if (entity.DateCreated == default)
    {
        entity.DateCreated = DateTime.UtcNow;
    }

    entity.LicenceStatus = UserLicenceStatus.Pending;
    entity.EmailVerificationToken = Guid.NewGuid().ToString("N");
    entity.EmailVerificationTokenExpiryUtc = DateTime.UtcNow.AddHours(verificationTokenLifetimeHours);

    db.Users.Add(entity);
    await db.SaveChangesAsync();

    if (mailUtility is not null)
    {
        var verificationPageUrl = new Uri(new Uri(downloadBaseUrl), "verify").ToString();
        var verificationLink = $"{verificationPageUrl}?token={Uri.EscapeDataString(entity.EmailVerificationToken)}";
        await mailUtility.SendVerificationEmailAsync(entity.Email, verificationLink);
    }

    return Results.Created($"/api/users/{entity.Id}", ToUserModel(entity));
})
    .WithName("CreateUser")
    .Produces<User>(StatusCodes.Status201Created)
    .ProducesValidationProblem()
    .Produces(StatusCodes.Status409Conflict);

users.MapPut("/{id:guid}", async (Guid id, User user, AcceptanceTestsDbContext db) =>
{
    var entity = await db.Users.FirstOrDefaultAsync(x => x.Id == id);
    if (entity is null)
    {
        return Results.NotFound();
    }

    var validationError = await ValidateUserAsync(user, db, id);
    if (validationError is not null)
    {
        return validationError;
    }

    entity.Email = user.Email.Trim();
    entity.LicenceStatus = user.LicenceStatus;

    await db.SaveChangesAsync();

    return Results.Ok(ToUserModel(entity));
})
    .WithName("UpdateUser")
    .Produces<User>(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status404NotFound)
    .ProducesValidationProblem()
    .Produces(StatusCodes.Status409Conflict);

users.MapDelete("/{id:guid}", async (Guid id, AcceptanceTestsDbContext db) =>
{
    var entity = await db.Users.FirstOrDefaultAsync(x => x.Id == id);
    if (entity is null)
    {
        return Results.NotFound();
    }

    db.Users.Remove(entity);
    await db.SaveChangesAsync();

    return Results.NoContent();
})
    .WithName("DeleteUser")
    .Produces(StatusCodes.Status204NoContent)
    .Produces(StatusCodes.Status404NotFound);

users.MapGet("/verify", async (string token, AcceptanceTestsDbContext db) =>
{
    if (string.IsNullOrWhiteSpace(token))
    {
        return Results.BadRequest("Verification token is required.");
    }

    var entity = await db.Users.FirstOrDefaultAsync(x => x.EmailVerificationToken == token);
    if (entity is null)
    {
        return Results.NotFound("Invalid verification token.");
    }

    if (entity.EmailVerificationTokenExpiryUtc is null || entity.EmailVerificationTokenExpiryUtc < DateTime.UtcNow)
    {
        return Results.BadRequest("Verification token has expired.");
    }

    entity.LicenceStatus = UserLicenceStatus.General;
    entity.EmailVerificationToken = null;
    entity.EmailVerificationTokenExpiryUtc = null;
    await db.SaveChangesAsync();

    var downloadLink = CreateDownloadAccessLink(
        entity.Email,
        DateTime.UtcNow,
        downloadTokenLifetimeHours,
        jwtIssuer,
        jwtAudience,
        jwtSigningKey,
        downloadAccessPurposeClaim,
        downloadBaseUrl);

    return Results.Ok(new
    {
        verified = true,
        email = entity.Email,
        downloadUrl = downloadLink
    });
})
    .AllowAnonymous()
    .WithName("VerifyUserEmail")
    .Produces(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status400BadRequest)
    .Produces(StatusCodes.Status404NotFound);

var organisations = app.MapGroup("/api/organisations")
    .WithTags("Organisations")
    .RequireRateLimiting("authenticated-api")
    .RequireAuthorization();

organisations.MapGet("/", async (AcceptanceTestsDbContext db) =>
{
    var entities = await db.Organisations
        .AsNoTracking()
        .OrderBy(x => x.Name)
        .ToListAsync();

    return Results.Ok(entities.Select(ToOrganisationModel));
})
    .WithName("ListOrganisations")
    .Produces<List<Organisation>>(StatusCodes.Status200OK);

organisations.MapGet("/{id:guid}", async (Guid id, AcceptanceTestsDbContext db) =>
{
    var entity = await db.Organisations
        .AsNoTracking()
        .FirstOrDefaultAsync(x => x.Id == id);

    return entity is null ? Results.NotFound() : Results.Ok(ToOrganisationModel(entity));
})
    .WithName("GetOrganisationById")
    .Produces<Organisation>(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status404NotFound);

organisations.MapPost("/", async (Organisation organisation, AcceptanceTestsDbContext db, HttpContext http) =>
{
    var validationError = await ValidateOrganisationAsync(organisation, db);
    if (validationError is not null)
    {
        return validationError;
    }

    var entity = ToOrganisationEntity(organisation);
    if (entity.Id == Guid.Empty)
    {
        entity.Id = Guid.NewGuid();
    }

    if (entity.DateCreated == default)
    {
        entity.DateCreated = DateTime.UtcNow;
    }

    entity.LicenceStatus = OrganisationLicenceStatus.Pending;
    entity.EmailVerificationToken = Guid.NewGuid().ToString("N");
    entity.EmailVerificationTokenExpiryUtc = DateTime.UtcNow.AddHours(verificationTokenLifetimeHours);

    var verificationPayload = new OrganisationVerificationPayload
    {
        OrganisationId = entity.Id,
        OrganisationName = organisation.Name.Trim(),
        ContactName = organisation.ContactName.Trim(),
        ContactEmail = organisation.ContactEmail.Trim(),
        ContactPhone = organisation.ContactPhone.Trim(),
        ContactAddress = organisation.ContactAddress.Trim(),
        OrganisationEmails = organisation.Emails.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
        LicencePathway = organisation.LicencePathway,
        AnnualTurnover = organisation.AnnualTurnover,
        DateCreatedUtc = entity.DateCreated
    };

    db.Organisations.Add(entity);
    await db.SaveChangesAsync();

    var persistedVerificationData = await db.Organisations
        .AsNoTracking()
        .Where(x => x.Id == entity.Id)
        .Select(x => new
        {
            x.EmailVerificationToken,
            x.EmailVerificationTokenExpiryUtc
        })
        .FirstOrDefaultAsync();

    if (persistedVerificationData is null ||
        string.IsNullOrWhiteSpace(persistedVerificationData.EmailVerificationToken) ||
        persistedVerificationData.EmailVerificationTokenExpiryUtc is null)
    {
        return Results.Problem(
            "Unable to persist organisation verification token details.",
            statusCode: StatusCodes.Status500InternalServerError);
    }

    if (mailUtility is not null)
    {
        var protectedPayload = ProtectOrganisationVerificationPayload(verificationPayload, organisationVerificationPayloadProtector);
        var verificationPageUrl = new Uri(new Uri(downloadBaseUrl), "verify-organisation").ToString();
        var verificationLink = $"{verificationPageUrl}?token={Uri.EscapeDataString(persistedVerificationData.EmailVerificationToken)}&payload={Uri.EscapeDataString(protectedPayload)}";
        await mailUtility.SendVerificationEmailAsync(verificationPayload.ContactEmail, verificationLink);
    }

    return Results.Created($"/api/organisations/{entity.Id}", ToOrganisationModel(entity));
})
    .WithName("CreateOrganisation")
    .Produces<Organisation>(StatusCodes.Status201Created)
    .ProducesValidationProblem()
    .Produces(StatusCodes.Status409Conflict);

organisations.MapPut("/{id:guid}", async (Guid id, Organisation organisation, AcceptanceTestsDbContext db) =>
{
    var entity = await db.Organisations
        .FirstOrDefaultAsync(x => x.Id == id);

    if (entity is null)
    {
        return Results.NotFound();
    }
    // Change this to just check for duplicate org name.
    bool isNameDuplicate = await IsOrgNameADuplicate(organisation, db, id);

    if(isNameDuplicate)
        return Results.Conflict();
    

    entity.Name = organisation.Name.Trim();
    entity.Emails = organisation.Emails.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    entity.LicenceStatus = organisation.LicenceStatus;
    entity.LicencePathway = organisation.LicencePathway;

    await db.SaveChangesAsync();

    return Results.Ok(ToOrganisationModel(entity));
})
    .WithName("UpdateOrganisation")
    .Produces<Organisation>(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status404NotFound)
    .ProducesValidationProblem()
    .Produces(StatusCodes.Status409Conflict);

organisations.MapDelete("/{id:guid}", async (Guid id, AcceptanceTestsDbContext db) =>
{
    var entity = await db.Organisations.FirstOrDefaultAsync(x => x.Id == id);
    if (entity is null)
    {
        return Results.NotFound();
    }

    db.Organisations.Remove(entity);
    await db.SaveChangesAsync();

    return Results.NoContent();
})
    .WithName("DeleteOrganisation")
    .Produces(StatusCodes.Status204NoContent)
    .Produces(StatusCodes.Status404NotFound)
    .Produces(StatusCodes.Status409Conflict);

organisations.MapGet("/verify", async (string token, string? payload, AcceptanceTestsDbContext db) =>
{
    if (string.IsNullOrWhiteSpace(token))
    {
        return Results.BadRequest("Verification token is required.");
    }
    var entities = await db.Organisations.ToListAsync();
    var entity = await db.Organisations.FirstOrDefaultAsync(x => x.EmailVerificationToken == token);
    if (entity is null)
    {
        return Results.NotFound("Invalid verification token.");
    }

    if (entity.EmailVerificationTokenExpiryUtc is null || entity.EmailVerificationTokenExpiryUtc < DateTime.UtcNow)
    {
        return Results.BadRequest("Verification token has expired.");
    }

    entity.LicenceStatus = OrganisationLicenceStatus.EmailVerified;
    entity.EmailVerificationToken = null;
    entity.EmailVerificationTokenExpiryUtc = null;
    await db.SaveChangesAsync();

    if (mailUtility is not null)
    {
        var verificationPayload = UnprotectOrganisationVerificationPayload(payload, organisationVerificationPayloadProtector);
        if (verificationPayload is not null)
        {
            await mailUtility.SendOrganisationVerificationSummaryEmailAsync(
                verificationPayload.ContactEmail,
                verificationPayload.OrganisationName,
                verificationPayload.ContactName,
                verificationPayload.ContactEmail,
                verificationPayload.ContactPhone,
                verificationPayload.ContactAddress,
                verificationPayload.OrganisationEmails,
                GetEnumDescription(verificationPayload.LicencePathway),
                GetEnumDescription(verificationPayload.AnnualTurnover),
                verificationPayload.DateCreatedUtc);
        }
    }

    return Results.Ok(new
    {
        verified = true,
        organisationId = entity.Id,
        organisationName = entity.Name
    });
})
    .AllowAnonymous()
    .WithName("VerifyOrganisationEmail")
    .Produces(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status400BadRequest)
    .Produces(StatusCodes.Status404NotFound);






static string GetEnumDescription<TEnum>(TEnum value)
    where TEnum : Enum
{
    var memberInfo = value.GetType().GetMember(value.ToString()).FirstOrDefault();
    var description = memberInfo?.GetCustomAttributes(typeof(DescriptionAttribute), false)
        .OfType<DescriptionAttribute>()
        .FirstOrDefault();

    return description?.Description ?? value.ToString();
}

static (string? Email, DateTime ExpiresAtUtc, IResult? FailureResult) ValidateDownloadToken(
    string token,
    string jwtIssuer,
    string jwtAudience,
    string jwtSigningKey,
    string expectedPurposeClaim)
{
    if (string.IsNullOrWhiteSpace(token))
    {
        return (null, default, Results.BadRequest("Download token is required."));
    }

    var validationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateIssuerSigningKey = true,
        ValidateLifetime = true,
        ValidIssuer = jwtIssuer,
        ValidAudience = jwtAudience,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSigningKey)),
        ClockSkew = TimeSpan.FromMinutes(1)
    };

    try
    {
        var handler = new JwtSecurityTokenHandler();
        var principal = handler.ValidateToken(token, validationParameters, out var validatedToken);
        var jwtToken = validatedToken as JwtSecurityToken;

        var purpose = principal.Claims.FirstOrDefault(c => c.Type == "purpose")?.Value;
        var email = principal.Claims.FirstOrDefault(c => c.Type == JwtRegisteredClaimNames.Email)?.Value
            ?? principal.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Email)?.Value;

        if (!string.Equals(purpose, expectedPurposeClaim, StringComparison.Ordinal) || string.IsNullOrWhiteSpace(email) || jwtToken is null)
        {
            return (null, default, Results.Unauthorized());
        }

        return (email, jwtToken.ValidTo, null);
    }
    catch (SecurityTokenExpiredException)
    {
        return (null, default, Results.BadRequest("Download token has expired."));
    }
    catch (SecurityTokenException)
    {
        return (null, default, Results.Unauthorized());
    }
}

/// <summary>
/// Maps a UserEntity from the database to a User model used in API responses.
/// </summary> <param name="entity">The UserEntity to map.</param>
/// <returns>A User model with properties copied from the entity.</returns> 
static User ToUserModel(UserEntity entity) =>
    new()
    {
        Id = entity.Id,
        Email = entity.Email,
        DateCreated = entity.DateCreated,
        LicenceStatus = entity.LicenceStatus,
        Country = entity.Country
    };

static UserEntity ToUserEntity(User model) =>
    new()
    {
        Id = model.Id,
        Email = model.Email.Trim(),
        DateCreated = model.DateCreated,
        LicenceStatus = model.LicenceStatus,
        Country = model.Country
    };

static Organisation ToOrganisationModel(OrganisationEntity entity) =>
    new()
    {
        Id = entity.Id,
        Name = entity.Name,
        Emails = entity.Emails,
        LicenceStatus = entity.LicenceStatus,
        LicencePathway = entity.LicencePathway,
        DateCreated = entity.DateCreated
    };

static OrganisationEntity ToOrganisationEntity(Organisation model) =>
    new()
    {
        Id = model.Id,
        Name = model.Name.Trim(),
        Emails = model.Emails.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
        LicenceStatus = model.LicenceStatus,
        LicencePathway = model.LicencePathway,
        DateCreated = model.DateCreated
    };

static DownloadAuditResponse ToDownloadAuditResponse(DownloadAuditEntity entity) =>
    new()
    {
        Id = entity.Id,
        DownloadedAtUtc = entity.DownloadedAtUtc,
        UserEmail = entity.UserEmail,
        UserId = entity.UserId,
        DownloadType = entity.DownloadType,
        Version = entity.Version,
        Platform = entity.Platform,
        DownloadUrl = entity.DownloadUrl
    };

static string BuildDownloadAuditCsv(IEnumerable<DownloadAuditEntity> audits)
{
    var lines = new List<string>
    {
        "DownloadedAtUtc,UserEmail,UserId,DownloadType,Version,Platform,DownloadUrl"
    };

    foreach (var audit in audits)
    {
        lines.Add(string.Join(",",
            EscapeCsv(audit.DownloadedAtUtc.ToString("O")),
            EscapeCsv(audit.UserEmail),
            EscapeCsv(audit.UserId?.ToString()),
            EscapeCsv(audit.DownloadType),
            EscapeCsv(audit.Version),
            EscapeCsv(audit.Platform),
            EscapeCsv(audit.DownloadUrl)));
    }

    return string.Join(Environment.NewLine, lines) + Environment.NewLine;
}

static string EscapeCsv(string? value)
{
    if (string.IsNullOrEmpty(value))
    {
        return string.Empty;
    }

    var escapedValue = value.Replace("\"", "\"\"");
    if (escapedValue.Contains(',') || escapedValue.Contains('"') || escapedValue.Contains('\n') || escapedValue.Contains('\r'))
    {
        return $"\"{escapedValue}\"";
    }

    return escapedValue;
}

static string CreateDownloadAccessLink(
    string email,
    DateTime nowUtc,
    int tokenLifetimeHours,
    string jwtIssuer,
    string jwtAudience,
    string jwtSigningKey,
    string downloadAccessPurposeClaim,
    string downloadBaseUrl)
{
    var expiresAt = nowUtc.AddHours(tokenLifetimeHours);
    var claims = new[]
    {
        new Claim(JwtRegisteredClaimNames.Sub, email),
        new Claim(JwtRegisteredClaimNames.Email, email),
        new Claim("purpose", downloadAccessPurposeClaim),
        new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
    };

    var signingCredentials = new SigningCredentials(
        new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSigningKey)),
        SecurityAlgorithms.HmacSha256);

    var token = new JwtSecurityToken(
        issuer: jwtIssuer,
        audience: jwtAudience,
        claims: claims,
        notBefore: nowUtc,
        expires: expiresAt,
        signingCredentials: signingCredentials);

    var tokenValue = new JwtSecurityTokenHandler().WriteToken(token);
    var downloadPageUrl = new Uri(new Uri(downloadBaseUrl), "download").ToString();
    return $"{downloadPageUrl}?token={Uri.EscapeDataString(tokenValue)}";
}

static async Task<IResult?> ValidateUserAsync(User user, AcceptanceTestsDbContext db, Guid? currentUserId = null)
{
    if (string.IsNullOrWhiteSpace(user.Email))
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["email"] = ["Email is required."]
        });
    }

    try
    {
        _ = new MailAddress(user.Email);
    }
    catch (FormatException)
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["email"] = ["Email is not valid."]
        });
    }

    var normalizedEmail = user.Email.Trim();

    var duplicateEmail = await db.Users.AnyAsync(x =>
        x.Email == normalizedEmail &&
        (!currentUserId.HasValue || x.Id != currentUserId.Value));

    if (duplicateEmail)
    {
        return Results.Conflict("A user with the same email already exists.");
    }

    return null;
}

static async Task<IResult?> ValidateOrganisationAsync(
    Organisation organisation,
    AcceptanceTestsDbContext db,
    Guid? currentOrganisationId = null)
{
    List<(string, string)> fields =
    [
        ("Name", "Name is required."),
        ("ContactName", "ContactName is required."),
        ("ContactEmail", "ContactEmail is required."),
        ("ContactAddress", "ContactAddress is required."),
    ];

    var errors = new Dictionary<string, string[]>();
    foreach((string,string) field in fields)
    {
        object? value = null;
        PropertyInfo? propInfo = typeof(Organisation).GetProperty(field.Item1);
        if (propInfo != null)
            value = propInfo.GetValue(organisation);
        if (string.IsNullOrEmpty(value?.ToString()))
            errors[field.Item1.ToString()] = [$"{field.Item1} is required."];
    }

    if (errors.Count > 0)
        return Results.ValidationProblem(errors);

    bool isNameDuplicate = await IsOrgNameADuplicate(organisation, db, currentOrganisationId);
    if (isNameDuplicate)
        return Results.Conflict("An organisation with the same name already exists.");

    return null;
}

static string ProtectOrganisationVerificationPayload(
    OrganisationVerificationPayload payload,
    IDataProtector protector)
{
    var payloadJson = JsonSerializer.Serialize(payload);
    return protector.Protect(payloadJson);
}

static OrganisationVerificationPayload? UnprotectOrganisationVerificationPayload(
    string? protectedPayload,
    IDataProtector protector)
{
    if (string.IsNullOrWhiteSpace(protectedPayload))
    {
        return null;
    }

    try
    {
        var payloadJson = protector.Unprotect(protectedPayload);
        return JsonSerializer.Deserialize<OrganisationVerificationPayload>(payloadJson);
    }
    catch
    {
        return null;
    }
}

static MailUtility CreateMailUtility(string smtpApiKey)
{
    if (!string.IsNullOrWhiteSpace(smtpApiKey))
    {
        MailUtility newMailUtility = new(smtpApiKey);
        return newMailUtility;
    }
    throw new Exception("Unable to create MailUtility: SMTP API key is not configured.");
}


static async Task<bool> IsOrgNameADuplicate(
    Organisation organisation,
    AcceptanceTestsDbContext db,
    Guid? currentOrganisationId = null)
{
    
    string normalizedName = organisation.Name.Trim();
    bool duplicateName = await db.Organisations.AnyAsync(x =>
        x.Name == normalizedName &&
        (!currentOrganisationId.HasValue || x.Id != currentOrganisationId.Value));
    return duplicateName;
}


*/