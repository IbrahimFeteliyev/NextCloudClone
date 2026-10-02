using System.Text;
using Atlas.Api.Auth;
using Atlas.Api.Data;
using Atlas.Api.Entities;
using Atlas.Api.Permissions;
using Atlas.Api.Services;
using Atlas.Api.Storage;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using Minio;
var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: false).AddEnvironmentVariables();
builder.Services.AddDbContext<AppDbContext>(o => o.UseNpgsql(builder.Configuration.GetConnectionString("Database")));
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<CurrentUser>(); builder.Services.AddScoped<PermissionService>(); builder.Services.AddScoped<DocumentService>();
builder.Services.AddScoped<FileContentService>();
builder.Services.AddScoped<AuditService>(); builder.Services.AddScoped<SeedData>(); builder.Services.AddScoped<ObjectStorage>(); builder.Services.AddScoped<PasswordHasher<User>>();
builder.Services.AddSingleton<DocumentLocks>(); builder.Services.AddSingleton<OnlyOfficeOptions>();
builder.Services.AddScoped<OnlyOfficeTokens>(); builder.Services.AddScoped<OnlyOfficeService>(); builder.Services.AddScoped<FileVersionService>();
builder.Services.AddHttpClient<OnlyOfficeClient>(http => http.Timeout = TimeSpan.FromSeconds(100))
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false });
builder.Services.AddSingleton<IMinioClient>(_ => new MinioClient().WithEndpoint(builder.Configuration["Minio:Endpoint"] ?? "localhost:9000")
    .WithCredentials(builder.Configuration["Minio:AccessKey"], builder.Configuration["Minio:SecretKey"]).WithSSL(builder.Configuration.GetValue("Minio:UseSsl", false)).Build());
var jwtKey = builder.Configuration["Jwt:Key"];
if (string.IsNullOrEmpty(jwtKey) || Encoding.UTF8.GetByteCount(jwtKey) < 32) throw new InvalidOperationException("Copy appsettings.Local.example.json to appsettings.Local.json; JWT key must contain at least 32 bytes.");
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o => o.TokenValidationParameters = new()
{
    ValidateIssuer = true, ValidateAudience = true, ValidateLifetime = true, ValidateIssuerSigningKey = true,
    ValidIssuer = builder.Configuration["Jwt:Issuer"], ValidAudience = builder.Configuration["Jwt:Audience"],
    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)), ClockSkew = TimeSpan.FromSeconds(30)
});
builder.Services.AddAuthorization();
var frontendOrigin = builder.Configuration["FrontendOrigin"] ?? "http://localhost:5173";
var frontendOrigins = new List<string> { frontendOrigin };
if (Uri.TryCreate(frontendOrigin, UriKind.Absolute, out var frontendUri) && frontendUri.Host is "localhost" or "127.0.0.1")
    frontendOrigins.Add(new UriBuilder(frontendUri) { Host = frontendUri.Host == "localhost" ? "127.0.0.1" : "localhost" }.Uri.GetLeftPart(UriPartial.Authority));
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p.WithOrigins(frontendOrigins.ToArray()).AllowAnyHeader().AllowAnyMethod()
    .WithExposedHeaders("Accept-Ranges", "Content-Range", "Content-Length", "ETag")));
builder.Services.AddControllers(); builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(o =>
{
    o.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme { Type = SecuritySchemeType.Http, Scheme = "bearer", BearerFormat = "JWT", Description = "Paste the token returned by /api/auth/login." });
    o.AddSecurityRequirement(document => new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference("Bearer", document)] = [] });
});
var app = builder.Build();
app.Use(async (context, next) =>
{
    try { await next(); }
    catch (ApiException ex) { context.Response.StatusCode = ex.Status; await context.Response.WriteAsJsonAsync(new { message = ex.Message }); }
    catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: "23505" })
    { context.Response.StatusCode = 409; await context.Response.WriteAsJsonAsync(new { message = "This name is already in use. Refresh and try another name." }); }
    catch (Exception ex) { app.Logger.LogError(ex, "Request failed"); context.Response.StatusCode = 500; await context.Response.WriteAsJsonAsync(new { message = "The request could not be completed. Check the API log and local services." }); }
});
app.UseCors(); if (app.Environment.IsDevelopment()) { app.UseSwagger(); app.UseSwaggerUI(); }
app.UseAuthentication(); app.UseAuthorization(); app.MapControllers();
app.MapGet("/health", () => Results.Ok(new { status = "ok", application = "Atlas" }));
if (!builder.Configuration.GetValue("Demo:SkipSeed", false))
{
    using var scope = app.Services.CreateScope(); await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
    await scope.ServiceProvider.GetRequiredService<SeedData>().Initialize();
}
app.Run();
public partial class Program { }
