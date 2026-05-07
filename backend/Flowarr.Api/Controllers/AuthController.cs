using System.Security.Claims;
using Flowarr.Infrastructure.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Flowarr.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController(IAdminAuthService adminAuthService) : ControllerBase
{
    [HttpGet("status")]
    [AllowAnonymous]
    public async Task<IActionResult> Status(CancellationToken cancellationToken)
    {
        return Ok(new
        {
            setupComplete = await adminAuthService.IsSetupCompleteAsync(cancellationToken),
            authenticated = User.Identity?.IsAuthenticated == true
        });
    }

    [HttpPost("setup")]
    [AllowAnonymous]
    public async Task<IActionResult> Setup(AuthRequest request, CancellationToken cancellationToken)
    {
        var created = await adminAuthService.CreateAdminAsync(request.Username, request.Password, cancellationToken);
        if (!created)
        {
            return BadRequest(new { message = "Admin already exists or password is shorter than 12 characters." });
        }

        await SignInAsync(request.Username);
        return Ok(new { ok = true });
    }

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login(AuthRequest request, CancellationToken cancellationToken)
    {
        if (!await adminAuthService.ValidateAsync(request.Username, request.Password, cancellationToken))
        {
            return Unauthorized(new { message = "Invalid username or password." });
        }

        await SignInAsync(request.Username);
        return Ok(new { ok = true });
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Ok(new { ok = true });
    }

    private async Task SignInAsync(string username)
    {
        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.Name, username)],
            CookieAuthenticationDefaults.AuthenticationScheme);
        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity),
            new AuthenticationProperties
            {
                IsPersistent = true,
                ExpiresUtc = DateTimeOffset.UtcNow.AddDays(30)
            });
    }
}

public sealed record AuthRequest(string Username, string Password);
