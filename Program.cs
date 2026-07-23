using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using HomeAccounts;
using HomeAccounts.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });

builder.Services.AddScoped<LocalDbService>();
builder.Services.AddScoped<PrevisionService>();
builder.Services.AddScoped<ComparativaService>();
builder.Services.AddScoped<RecurrenteAvisoService>();
builder.Services.AddScoped<DriveService>();
builder.Services.AddScoped<SyncService>();

var app = builder.Build();

// Inicializar datos por defecto (cuentas y categorías predefinidas)
using var scope = app.Services.CreateScope();
// La inicialización la hace el App.razor al arrancar para tener acceso a IJSRuntime

await app.RunAsync();
