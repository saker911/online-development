using System.Security.Cryptography;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting.WindowsServices;
using QuestPDF.Infrastructure;
using VehiclePermitSystemWeb.Data;
using VehiclePermitSystemWeb.Infrastructure;
using VehiclePermitSystemWeb.Infrastructure.DependencyInjection;
using VehiclePermitSystemWeb.Services.Bootstrap;
using VehiclePermitSystemWeb.Services.Common;
using VehiclePermitSystemWeb.Services.Users;

var executableDirectory = Path.GetDirectoryName(Environment.ProcessPath ?? string.Empty);
var runningAsWindowsService = WindowsServiceHelpers.IsWindowsService();
var workingDirectory = Directory.GetCurrentDirectory();
var contentRootPath = runningAsWindowsService
    ? (
        string.IsNullOrWhiteSpace(executableDirectory)
            ? AppContext.BaseDirectory
            : executableDirectory
    )
    : workingDirectory;

var builder = WebApplication.CreateBuilder(
    new WebApplicationOptions
    {
        Args = args,
        ContentRootPath = contentRootPath,
        WebRootPath = Path.Combine(contentRootPath, "wwwroot"),
    }
);
var runSchemaUpgradeOnly = args.Any(arg =>
    string.Equals(arg, "--upgrade-db", StringComparison.OrdinalIgnoreCase)
    || string.Equals(arg, "--schema-upgrade", StringComparison.OrdinalIgnoreCase)
);

if (InstallerDataProbe.TryHandle(args))
{
    return;
}

builder.Host.UseWindowsService(options =>
{
    options.ServiceName = DeploymentDefaults.WindowsServiceName;
});

AppStoragePaths.MigrateLegacyStorage(contentRootPath, builder.Environment.WebRootPath);
if (!builder.Environment.IsDevelopment())
{
    builder.Configuration.AddJsonFile(
        AppStoragePaths.GetRuntimeConfigurationPath(),
        optional: true,
        reloadOnChange: true
    );
}
builder.Configuration.AddEnvironmentVariables();

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
QuestPDF.Settings.License = LicenseType.Community;
var cookieSecurePolicy = ResolveCookieSecurePolicy(builder.Configuration, builder.Environment);

// Add services to the container.
builder.Services.AddControllersWithViews(options =>
{
    options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute());
    options.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true;
});
builder.Services.AddAntiforgery(options =>
{
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.SecurePolicy = cookieSecurePolicy;
    options.SuppressXFrameOptionsHeader = true;
});
builder.Services.AddMemoryCache();
builder
    .Services.AddDataProtection()
    .SetApplicationName("VehiclePermitSystemWeb")
    .PersistKeysToDbContext<ApplicationDbContext>();
builder.Services.AddHttpContextAccessor();
var loginPermitLimit = builder.Configuration.GetValue("Security:LoginPermitLimit", 5);
var loginWindowSeconds = builder.Configuration.GetValue("Security:LoginWindowSeconds", 60);
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy<string>(
        "login",
        context =>
        {
            var remoteIp = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            return RateLimitPartition.GetFixedWindowLimiter(
                partitionKey: remoteIp,
                factory: _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = loginPermitLimit,
                    Window = TimeSpan.FromSeconds(loginWindowSeconds),
                    QueueLimit = 0,
                    AutoReplenishment = true,
                }
            );
        }
    );
    options.AddPolicy<string>(
        "display-registration",
        context =>
        {
            var remoteIp = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            return RateLimitPartition.GetFixedWindowLimiter(
                partitionKey: remoteIp,
                factory: _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 5,
                    Window = TimeSpan.FromMinutes(15),
                    QueueLimit = 0,
                    AutoReplenishment = true,
                }
            );
        }
    );
    options.AddPolicy<string>(
        "display-operator",
        context =>
        {
            var remoteIp = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            var path = context.Request.Path.Value ?? "/display/operator";
            return RateLimitPartition.GetFixedWindowLimiter(
                partitionKey: $"{path}:{remoteIp}",
                factory: _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 5,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                    AutoReplenishment = true,
                }
            );
        }
    );
});
var dataProvider = builder.Configuration["Data:Provider"] ?? "Sqlite";
var developmentUseLocalData =
    builder.Environment.IsDevelopment()
    || builder.Configuration.GetValue<bool>("DevelopmentUseLocalData");
string? resolvedSqlitePath = null;
if (string.Equals(dataProvider, "Sqlite", StringComparison.OrdinalIgnoreCase))
{
    var sqliteConnectionString =
        builder.Configuration.GetConnectionString("SqliteConnection")
        ?? "Data Source=vehicle-permit-system.db";
    var sqliteDataSource = new System.Data.Common.DbConnectionStringBuilder
    {
        ConnectionString = sqliteConnectionString,
    };

    var sqliteFileName = "vehicle-permit-system.db";

    if (
        sqliteDataSource.TryGetValue("Data Source", out var dataSourceValue)
        && dataSourceValue is string dataSource
        && !string.IsNullOrWhiteSpace(dataSource)
    )
    {
        sqliteFileName = Path.GetFileName(dataSource);
    }

    resolvedSqlitePath = AppStoragePaths.GetDatabasePath(
        sqliteFileName,
        builder.Environment.IsDevelopment() && developmentUseLocalData
    );
}
var configuredUrls = builder.Configuration["App:Urls"];
var knownProxyAddresses = ReadKnownProxyAddresses(builder.Configuration);
var knownProxyNetworks = ReadKnownProxyNetworks(builder.Configuration);
var trustForwardedHeadersFromPlatform = builder.Configuration.GetValue<bool>(
    "ForwardedHeaders:TrustPlatformProxy"
);
if (builder.Environment.IsDevelopment() && string.IsNullOrWhiteSpace(configuredUrls))
{
    builder.WebHost.UseUrls("http://127.0.0.1:5001");
}
else if (!string.IsNullOrWhiteSpace(configuredUrls))
{
    builder.WebHost.UseUrls(configuredUrls);
}

builder.Services.AddDbContextFactory<ApplicationDbContext>(options =>
{
    if (string.Equals(dataProvider, "SqlServer", StringComparison.OrdinalIgnoreCase))
    {
        options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection"));
    }
    else if (
        string.Equals(dataProvider, "PostgreSql", StringComparison.OrdinalIgnoreCase)
        || string.Equals(dataProvider, "Postgres", StringComparison.OrdinalIgnoreCase)
        || string.Equals(dataProvider, "Npgsql", StringComparison.OrdinalIgnoreCase)
    )
    {
        // Preserve the current local-time workflow until persisted timestamps are normalized.
        AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
        var postgreSqlConnectionString = builder.Configuration.GetConnectionString(
            "PostgreSqlConnection"
        );
        if (string.IsNullOrWhiteSpace(postgreSqlConnectionString))
        {
            throw new InvalidOperationException(
                "PostgreSQL is selected, but ConnectionStrings:PostgreSqlConnection is empty."
            );
        }

        options.UseNpgsql(
            PostgreSqlConnectionStringNormalizer.Normalize(postgreSqlConnectionString)
        );
    }
    else if (string.Equals(dataProvider, "Sqlite", StringComparison.OrdinalIgnoreCase))
    {
        var sqliteConnectionString =
            builder.Configuration.GetConnectionString("SqliteConnection")
            ?? "Data Source=vehicle-permit-system.db";
        var sqliteDataSource = new System.Data.Common.DbConnectionStringBuilder
        {
            ConnectionString = sqliteConnectionString,
        };

        var sqliteFileName = "vehicle-permit-system.db";

        if (
            sqliteDataSource.TryGetValue("Data Source", out var dataSourceValue)
            && dataSourceValue is string dataSource
            && !string.IsNullOrWhiteSpace(dataSource)
        )
        {
            sqliteFileName = Path.GetFileName(dataSource);
        }

        var sqlitePath =
            resolvedSqlitePath
            ?? AppStoragePaths.GetDatabasePath(
                sqliteFileName,
                builder.Environment.IsDevelopment() && developmentUseLocalData
            );
        resolvedSqlitePath = sqlitePath;
        sqliteDataSource["Data Source"] = sqlitePath;

        options.UseSqlite(sqliteDataSource.ConnectionString);
    }
    else
    {
        options.UseInMemoryDatabase("VehiclePermitSystemWeb");
    }
});
builder.Services.AddVehiclePermitApplicationServices(runSchemaUpgradeOnly);
builder.Services.AddVehiclePermitAuthorization();
builder.Services.AddVehiclePermitCookieAuthentication(cookieSecurePolicy, builder.Configuration);

var app = builder.Build();
app.Logger.LogInformation(
    "Host paths resolved. WindowsService={WindowsService}, ContentRoot={ContentRoot}, WebRoot={WebRoot}",
    runningAsWindowsService,
    app.Environment.ContentRootPath,
    app.Environment.WebRootPath
);
app.Logger.LogInformation(
    "Security cookie secure policy resolved to {CookieSecurePolicy}. Supported values: SameAsRequest, Always, None.",
    cookieSecurePolicy
);
if (cookieSecurePolicy == CookieSecurePolicy.Always)
{
    app.Logger.LogWarning(
        "Security:CookieSecurePolicy=Always requires HTTPS requests or trusted forwarded HTTPS headers before antiforgery cookies are generated."
    );
}
if (string.Equals(dataProvider, "Sqlite", StringComparison.OrdinalIgnoreCase))
{
    app.Logger.LogInformation(
        "SQLite database path: {DatabasePath}",
        resolvedSqlitePath ?? "غير محدد"
    );
}
var databaseBootstrap = app.Services.GetRequiredService<IDatabaseBootstrapService>();
databaseBootstrap.EnsureInitialized();

if (runSchemaUpgradeOnly)
{
    app.Logger.LogInformation("Database bootstrap and schema upgrade completed successfully.");
    return;
}

var userAdminService = app.Services.GetRequiredService<IUserAdminService>();
AppClock.Configure(
    app.Services.GetRequiredService<VehiclePermitSystemWeb.Services.Common.ISystemClock>()
);

var forwardedHeadersOptions = BuildForwardedHeadersOptions(
    knownProxyAddresses,
    knownProxyNetworks,
    trustForwardedHeadersFromPlatform
);
if (forwardedHeadersOptions != null)
{
    app.UseForwardedHeaders(forwardedHeadersOptions);
}
else
{
    app.Logger.LogInformation(
        "Forwarded headers are disabled because no trusted proxies or networks are configured."
    );
}

if (cookieSecurePolicy == CookieSecurePolicy.Always)
{
    app.Use(
        async (context, next) =>
        {
            if (context.Request.IsHttps)
            {
                await next();
                return;
            }

            app.Logger.LogWarning(
                "Blocked non-HTTPS request while Security:CookieSecurePolicy=Always. Path={Path}, RemoteIp={RemoteIp}",
                context.Request.Path,
                context.Connection.RemoteIpAddress
            );
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            context.Response.ContentType = "text/html; charset=utf-8";
            await context.Response.WriteAsync(
                """
                <!doctype html>
                <html lang="ar" dir="rtl">
                <head><meta charset="utf-8"><title>إعداد HTTPS مطلوب</title></head>
                <body style="font-family:Arial,sans-serif;max-width:760px;margin:48px auto;line-height:1.8">
                    <h1>إعداد HTTPS مطلوب</h1>
                    <p>النظام مضبوط على <strong>Security:CookieSecurePolicy = Always</strong>، لكن الطلب الحالي وصل إلى التطبيق عبر HTTP.</p>
                    <p>للتشغيل الداخلي أو التثبيت الأولي استخدم <strong>SameAsRequest</strong> في appsettings.json أو appsettings.runtime.json.</p>
                    <p>استخدم <strong>Always</strong> فقط بعد ضبط HTTPS فعليًا أو reverse proxy يرسل X-Forwarded-Proto=https من مصدر موثوق.</p>
                </body>
                </html>
                """
            );
        }
    );
}

app.Use(
    async (context, next) =>
    {
        if (userAdminService.IsClientIpAllowed(context))
        {
            await next();
            return;
        }

        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        context.Response.ContentType = "text/plain; charset=utf-8";
        await context.Response.WriteAsync("هذا العنوان غير مسموح له بالوصول إلى النظام.");
    }
);

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHttpsRedirection();
    app.UseHsts();
}

app.Use(
    async (context, next) =>
    {
        var cspScriptNonce = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));
        context.Items["CspScriptNonce"] = cspScriptNonce;
        context.Items["CspStyleNonce"] = cspScriptNonce;

        context.Response.OnStarting(() =>
        {
            var headers = context.Response.Headers;

            if (!headers.ContainsKey("Content-Security-Policy"))
            {
                headers["Content-Security-Policy"] =
                    $"default-src 'self'; base-uri 'self'; form-action 'self'; frame-ancestors 'none'; object-src 'none'; img-src 'self' data: blob:; font-src 'self' data:; style-src 'self' 'nonce-{cspScriptNonce}'; script-src 'self' 'nonce-{cspScriptNonce}'; connect-src 'self';";
            }

            if (!headers.ContainsKey("X-Frame-Options"))
            {
                headers["X-Frame-Options"] = "DENY";
            }

            if (!headers.ContainsKey("X-Content-Type-Options"))
            {
                headers["X-Content-Type-Options"] = "nosniff";
            }

            if (!headers.ContainsKey("Referrer-Policy"))
            {
                headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
            }

            if (!headers.ContainsKey("Cross-Origin-Resource-Policy"))
            {
                headers["Cross-Origin-Resource-Policy"] = "same-origin";
            }

            if (!headers.ContainsKey("Permissions-Policy"))
            {
                headers["Permissions-Policy"] =
                    "camera=(self), microphone=(), geolocation=(), payment=(), usb=()";
            }

            return Task.CompletedTask;
        });

        await next();
    }
);

app.UseStaticFiles();
app.UseStaticFiles(
    new StaticFileOptions
    {
        FileProvider = new PhysicalFileProvider(AppStoragePaths.GetUploadsRoot()),
        RequestPath = "/uploads",
    }
);

app.UseRouting();

app.UseRateLimiter();

app.UseMiddleware<FirstRunSetupMiddleware>();

app.UseAuthentication();

// Session validation middleware: validate server-side sessionId claim and refresh session
app.Use(
    async (context, next) =>
    {
        if (context.User?.Identity?.IsAuthenticated ?? false)
        {
            var sessionId = context.User.Claims.FirstOrDefault(c => c.Type == "sessionId")?.Value;
            if (
                string.IsNullOrWhiteSpace(sessionId)
                || !userAdminService.ValidateSession(sessionId, out var username)
            )
            {
                await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                context.Response.Redirect("/Account/Login");
                return;
            }

            if (!string.IsNullOrWhiteSpace(username))
            {
                var currentUser = userAdminService.GetUserAccount(username);
                if (currentUser == null || !currentUser.IsActive)
                {
                    userAdminService.RemoveSession(sessionId);
                    await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                    context.Response.Redirect("/Account/Login");
                    return;
                }

                // refresh sliding expiration after confirming the account is still valid
                userAdminService.RefreshSession(sessionId);

                var requestPath = context.Request.Path;
                var isPasswordChangePath = requestPath.StartsWithSegments(
                    "/Account/ChangePassword",
                    StringComparison.OrdinalIgnoreCase
                );
                var isLogoutPath = requestPath.StartsWithSegments(
                    "/Account/Logout",
                    StringComparison.OrdinalIgnoreCase
                );

                if (
                    currentUser?.MustChangePassword == true
                    && !isPasswordChangePath
                    && !isLogoutPath
                )
                {
                    context.Response.Redirect("/Account/ChangePassword?forced=true");
                    return;
                }
            }
        }
        await next();
    }
);

app.UseAuthorization();

app.MapGet("/healthz", () => Results.Ok(new { status = "ok" })).AllowAnonymous();
app.MapControllerRoute(name: "default", pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();

static CookieSecurePolicy ResolveCookieSecurePolicy(
    IConfiguration configuration,
    IHostEnvironment environment
)
{
    var configuredPolicy = configuration["Security:CookieSecurePolicy"];
    if (
        !environment.IsDevelopment()
        && !configuration.GetValue("Security:AllowInsecureCookies", false)
    )
    {
        return CookieSecurePolicy.Always;
    }
    if (string.Equals(configuredPolicy, "SameAsRequest", StringComparison.OrdinalIgnoreCase))
    {
        return CookieSecurePolicy.SameAsRequest;
    }

    if (string.Equals(configuredPolicy, "Always", StringComparison.OrdinalIgnoreCase))
    {
        return CookieSecurePolicy.Always;
    }

    if (string.Equals(configuredPolicy, "None", StringComparison.OrdinalIgnoreCase))
    {
        return CookieSecurePolicy.None;
    }

    return CookieSecurePolicy.SameAsRequest;
}

static ForwardedHeadersOptions? BuildForwardedHeadersOptions(
    IReadOnlyCollection<System.Net.IPAddress> knownProxies,
    IReadOnlyCollection<IPNetwork> knownNetworks,
    bool trustPlatformProxy
)
{
    if (!trustPlatformProxy && knownProxies.Count == 0 && knownNetworks.Count == 0)
    {
        return null;
    }

    var options = new ForwardedHeadersOptions
    {
        ForwardedHeaders =
            ForwardedHeaders.XForwardedFor
            | ForwardedHeaders.XForwardedProto
            | ForwardedHeaders.XForwardedHost,
        ForwardLimit = 1,
    };
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();

    if (trustPlatformProxy)
    {
        return options;
    }

    foreach (var address in knownProxies)
    {
        options.KnownProxies.Add(address);
    }

    foreach (var network in knownNetworks)
    {
        options.KnownNetworks.Add(network);
    }

    return options;
}

static System.Net.IPAddress[] ReadKnownProxyAddresses(IConfiguration configuration)
{
    return ReadForwardedHeaderEntries(configuration, "ForwardedHeaders:KnownProxies")
        .Select(value => System.Net.IPAddress.TryParse(value, out var address) ? address : null)
        .Where(address => address is not null)
        .Cast<System.Net.IPAddress>()
        .Distinct()
        .ToArray();
}

static IPNetwork[] ReadKnownProxyNetworks(IConfiguration configuration)
{
    return ReadForwardedHeaderEntries(configuration, "ForwardedHeaders:KnownNetworks")
        .Select(value => TryParseIPNetwork(value, out var network) ? network : null)
        .Where(network => network is not null)
        .Cast<IPNetwork>()
        .ToArray();
}

static string[] ReadForwardedHeaderEntries(IConfiguration configuration, string key)
{
    var section = configuration.GetSection(key);
    var childValues = section
        .GetChildren()
        .Select(item => item.Value)
        .Where(value => !string.IsNullOrWhiteSpace(value));

    var values = childValues.Any() ? childValues : SplitForwardedHeaderEntries(section.Value);

    return values.Select(value => value!.Trim()).Where(value => value.Length > 0).ToArray();
}

static IEnumerable<string> SplitForwardedHeaderEntries(string? value)
{
    return string.IsNullOrWhiteSpace(value)
        ? Array.Empty<string>()
        : value.Split(
            new[] { ',', ';', '\r', '\n' },
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
        );
}

static bool TryParseIPNetwork(string value, out IPNetwork? network)
{
    network = null;
    var parts = value.Split('/', 2, StringSplitOptions.TrimEntries);
    if (
        parts.Length != 2
        || !System.Net.IPAddress.TryParse(parts[0], out var prefix)
        || !int.TryParse(parts[1], out var prefixLength)
    )
    {
        return false;
    }

    var maxPrefixLength =
        prefix.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork ? 32 : 128;
    if (prefixLength < 0 || prefixLength > maxPrefixLength)
    {
        return false;
    }

    network = new IPNetwork(prefix, prefixLength);
    return true;
}
