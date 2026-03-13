using Microsoft.EntityFrameworkCore;
using SkiTelemetry_Web.Components;
using SkiTelemetry_Web.Data;
using SkiTelemetry_Web.Services;

var builder = WebApplication.CreateBuilder(args);


// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// SET DB TO USE
builder.Services.AddDbContext<SkiDbContext>(options =>
    options.UseSqlite("Data Source=SkiTelemetry.db")); // Crea un file chiamato SkiTelemetry.db

// Registra il servizio per la gestione delle Run
builder.Services.AddScoped<RunService>();

var app = builder.Build();

// CREATE DB IF NOT EXISTS
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<SkiDbContext>();
    db.Database.EnsureCreated();
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
