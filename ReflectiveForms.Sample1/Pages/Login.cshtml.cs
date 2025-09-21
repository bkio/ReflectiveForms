// Copyright (c) 2022- Burak Kara, AGPL-3.0 license
// See LICENSE file in the project root for full license information.

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Authorization;
using System.Text.Json.Serialization;

namespace ReflectiveForms.Sample1.Pages;

[AllowAnonymous]
public class LoginModel : PageModel
{
    [BindProperty]
    [JsonPropertyName("email")]
    public string? Email { get; set; }

    [BindProperty]
    [JsonPropertyName("password")]
    public string? Password { get; set; }

    [BindProperty]
    [JsonPropertyName("returnUrl")]
    public string? ReturnUrl { get; set; }

    public void OnGet(string? returnUrl = null)
    {
        ReturnUrl = returnUrl ?? Url.Content("~/");

        // Redirect if already authenticated
        if (User.Identity?.IsAuthenticated == true)
        {
            Response.Redirect(ReturnUrl);
        }
    }

    public IActionResult OnPost()
    {
        ReturnUrl ??= Url.Content("~/");

        // The actual login is handled by JavaScript calling the API endpoint
        // This method is just for fallback/server-side processing if needed
        return Page();
    }
}
