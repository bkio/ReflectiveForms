using System.Net;
using ReflectiveForms.Core;

var builder = WebApplication.CreateBuilder(args);

using var loggerFactory = LoggerFactory.Create(logging =>
{
    logging.AddConsole();
    logging.AddDebug();
});
var rfLogger = loggerFactory.CreateLogger<Program>();

builder.WebHost.ConfigureKestrel(options =>
{
    options.Listen(IPAddress.Any, {{BACKEND_PORT}});
});

var app = builder.BuildWithReflectiveFields(RfBuilder.Build(rfLogger));

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
}

app.UseStaticFiles();
app.Run();
