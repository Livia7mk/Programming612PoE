using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RaceDay.Web.Models;
using RaceDay.Web.Services;

namespace RaceDay.Web.Controllers;

public class AccountController : Controller
{
    private readonly ApiClient _api;

    public AccountController(ApiClient api)
    {
        _api = api;
    }

    // ---------- Log in ----------

    [HttpGet, AllowAnonymous]
    public IActionResult Login(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true)
            return RedirectToAction("Index", "Home");

        return View(new LoginViewModel { ReturnUrl = returnUrl });
    }

    [HttpPost, AllowAnonymous, ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel model)
    {
        if (!ModelState.IsValid) return View(model);

        var (result, apiCookie) = await _api.LoginAsync(model.Email, model.Password);

        if (!result.Success || result.Data is null || apiCookie is null)
        {
            ModelState.AddModelError(string.Empty,
                result.StatusCode == 401
                    ? "Incorrect email or password."
                    : result.Error ?? "We could not log you in. Please try again.");
            return View(model);
        }

        await StartSessionAsync(result.Data, apiCookie);

        return !string.IsNullOrEmpty(model.ReturnUrl) && Url.IsLocalUrl(model.ReturnUrl)
            ? LocalRedirect(model.ReturnUrl)
            : RedirectToAction("Index", "Home");
    }

    // ---------- Register ----------

    [HttpGet, AllowAnonymous]
    public IActionResult Register()
    {
        if (User.Identity?.IsAuthenticated == true)
            return RedirectToAction("Index", "Home");

        return View(new RegisterViewModel());
    }

    [HttpPost, AllowAnonymous, ValidateAntiForgeryToken]
    public async Task<IActionResult> Register(RegisterViewModel model)
    {
        if (!ModelState.IsValid) return View(model);

        var created = await _api.PostAsync<UserDto>("api/auth/register", new
        {
            fullName = model.FullName,
            email = model.Email,
            password = model.Password,
            phone = model.Phone,
            role = model.Role,
            organisationName = model.OrganisationName
        });

        if (!created.Success)
        {
            ModelState.AddModelError(string.Empty, created.Error ?? "We could not create your account. Please try again.");
            return View(model);
        }

        // Log the new user straight in.
        var (login, apiCookie) = await _api.LoginAsync(model.Email, model.Password);
        if (!login.Success || login.Data is null || apiCookie is null)
        {
            TempData["Success"] = "Your account was created. Please log in.";
            return RedirectToAction(nameof(Login));
        }

        await StartSessionAsync(login.Data, apiCookie);
        TempData["Success"] = "Welcome to RaceDay!";
        return RedirectToAction("Index", "Home");
    }

    // ---------- Log out ----------

    [HttpPost, Authorize, ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await _api.PostAsync<NoData>("api/auth/logout");   // ends the API session too

        HttpContext.Session.Clear();
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

        TempData["Success"] = "You have been logged out.";
        return RedirectToAction(nameof(Login));
    }

    [AllowAnonymous]
    public IActionResult AccessDenied()
    {
        Response.StatusCode = StatusCodes.Status403Forbidden;
        return View();
    }

    // ---------- Helpers ----------

    // Stores the user's ID, role and API cookie in the session, and signs them in on this site.
    private async Task StartSessionAsync(UserDto user, string apiCookie)
    {
        HttpContext.Session.Clear();
        HttpContext.Session.SetString(SessionKeys.ApiCookie, apiCookie);
        HttpContext.Session.SetInt32(SessionKeys.UserId, user.UserID);
        HttpContext.Session.SetString(SessionKeys.Role, user.Role);
        HttpContext.Session.SetString(SessionKeys.FullName, user.FullName);

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.UserID.ToString()),
            new(ClaimTypes.Name, user.FullName),
            new(ClaimTypes.Email, user.Email),
            new(ClaimTypes.Role, user.Role)
        };

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity));
    }
}