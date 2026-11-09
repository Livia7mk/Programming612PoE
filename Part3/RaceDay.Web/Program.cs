using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using RaceDay.Web.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();
builder.Services.AddHttpContextAccessor();

// Server-side session: holds the API's login cookie, the user's ID and role.
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.Cookie.Name = "RaceDay.Web.Session";
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.IdleTimeout = TimeSpan.FromHours(8);
});

// The MVC app's own login cookie, so [Authorize] and role checks work on every page.
builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "RaceDay.Web.Auth";
        options.LoginPath = "/Account/Login";
        options.AccessDeniedPath = "/Account/AccessDenied";
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
    });

builder.Services.AddAuthorization();

// Every data operation goes through the Part 2 API. The address comes from settings,
// so Docker can point it somewhere else without changing code.
builder.Services
    .AddHttpClient<ApiClient>(client =>
    {
        var baseUrl = builder.Configuration["Api:BaseUrl"]
            ?? throw new InvalidOperationException("Api:BaseUrl is not configured.");
        client.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
    })
    // Cookies are forwarded by hand, one per user, and never shared between users.
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { UseCookies = false });

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
}

app.UseStaticFiles();
app.UseRouting();
app.UseSession();
app.UseAuthentication();

// If the session has expired but the login cookie is still there, sign the user out cleanly.
app.Use(async (context, next) =>
{
    if (context.User.Identity?.IsAuthenticated == true &&
        string.IsNullOrEmpty(context.Session.GetString(SessionKeys.ApiCookie)))
    {
        await context.SignOutAsync();
        context.Response.Redirect("/Account/Login");
        return;
    }

    await next();
});

app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();