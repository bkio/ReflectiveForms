using System.Net;
using ReflectiveForms.Core;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy =>
    {
        policy.WithOrigins(
                "http://localhost:{{FRONTEND_PORT}}",
                "http://127.0.0.1:{{FRONTEND_PORT}}")
              .AllowAnyMethod()
              .AllowAnyHeader()
              .AllowCredentials();
    });
});

using var loggerFactory = LoggerFactory.Create(logging =>
{
    logging.AddConsole();
    logging.AddDebug();
});
var rfLogger = loggerFactory.CreateLogger<Program>();

builder.WebHost.ConfigureKestrel(options =>
{
    options.Listen(IPAddress.Loopback, {{BACKEND_PORT}});
});

var app = builder.BuildWithReflectiveFields(RfBuilder.Build(rfLogger));

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
}

app.UseCors("Frontend");
app.UseStaticFiles();
app.Run();
