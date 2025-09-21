// Copyright (c) 2022- Burak Kara, AGPL-3.0 license
// See LICENSE file in the project root for full license information.

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ReflectiveForms.Sample1.Pages;

public class LogoutModel : PageModel
{
    public async Task<IActionResult> OnGetAsync()
    {
        // Sign out from cookie authentication
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

        // Redirect to home page
        return RedirectToPage("/Index");
    }

    public async Task<IActionResult> OnPostAsync()
    {
        // Sign out from cookie authentication
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

        // Return success for AJAX calls
        if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
        {
            return new JsonResult(new { success = true, redirectUrl = "/" });
        }

        // Redirect to home page
        return RedirectToPage("/Index");
    }
}
