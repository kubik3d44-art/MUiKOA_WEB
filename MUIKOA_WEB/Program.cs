using Npgsql;
using MUiKOA_WEB.Components;
using MUiKOA_WEB.Services;

var builder = WebApplication.CreateBuilder(args);

var databaseConnectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' is not configured.");

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddSingleton(NpgsqlDataSource.Create(databaseConnectionString));
builder.Services.AddDistributedMemoryCache();
builder.Services.AddHttpContextAccessor();
builder.Services.AddSession(options =>
{
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});
builder.Services.AddScoped<UserAuthenticationService>();
builder.Services.AddScoped<UserProfileService>();
builder.Services.AddScoped<ReferenceDataService>();
builder.Services.AddScoped<AdministrationService>();
builder.Services.AddScoped<OperationalSectionService>();
builder.Services.AddScoped<UserSessionService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseSession();
app.UseAntiforgery();

app.MapPost("/login", async (HttpContext httpContext, UserAuthenticationService authenticationService, UserSessionService userSession) =>
{
    var form = await httpContext.Request.ReadFormAsync();
    var email = Convert.ToString(form["email"])?.Trim() ?? string.Empty;
    var password = Convert.ToString(form["password"]) ?? string.Empty;

    try
    {
        var result = await authenticationService.AuthenticateAsync(email, password, httpContext.RequestAborted);

        if (result.Status == AuthenticationStatus.Success && result.User is not null)
        {
            userSession.SetSession(result.User);
            return Results.Redirect("/dashboard");
        }

        return Results.Redirect(result.Status == AuthenticationStatus.AccessDenied
            ? "/?error=access-denied"
            : "/?error=invalid");
    }
    catch
    {
        return Results.Redirect("/?error=database");
    }
}).DisableAntiforgery();

app.MapGet("/logout", (UserSessionService userSession) =>
{
    userSession.Clear();
    return Results.Redirect("/");
});

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
