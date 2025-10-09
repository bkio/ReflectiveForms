// Copyright (c) 2022- Burak Kara, AGPL-3.0 license
// See LICENSE file in the project root for full license information.

using System.Net;
using ReflectiveForms.Core;
using ReflectiveForms.Sample1;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorPages();

// Create rf logger
using var loggerFactory = LoggerFactory.Create(logging =>
{
    logging.AddConsole();
    logging.AddDebug();
});
var rfLogger = loggerFactory.CreateLogger<Program>();

builder.WebHost.ConfigureKestrel(options =>
{
    options.Listen(IPAddress.Loopback, 9000);
});

// Build the app with reflective fields
var app = builder.BuildWithReflectiveFields(RfBuilder.Build(rfLogger));

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
}

app.UseStaticFiles();
app.MapRazorPages();

app.Run();
