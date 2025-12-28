using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using MudBlazor.Services;
using SharedUI;
using SharedUI.Logging;
using SharedUI.Services.Raw;
using SharedUI.Services;
using SharedUI.Services.Settings;
using WebApp.Services;
using WebApp.Services.Logging;
using WebApp.Services.Raw;
using WebApp.Services.Settings;
using WebApp;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddMudServices();
builder.Services.AddScoped<ImageDocumentState>();
builder.Services.AddScoped<BrowserRawImageProvider>();
builder.Services.AddScoped<IRawImageProvider>(sp => sp.GetRequiredService<BrowserRawImageProvider>());
builder.Services.AddScoped<IRawImageCache>(sp => sp.GetRequiredService<BrowserRawImageProvider>());
builder.Services.AddScoped<IImageFilePicker, BrowserImageFilePicker>();
builder.Services.AddScoped<IImageExportService, BrowserImageExportService>();
builder.Services.AddScoped<ImageProcessorService>();

builder.Services.AddScoped<IAppSettingsStore, BrowserAppSettingsStore>();
builder.Services.AddScoped<AppSettingsService>();

builder.Services.AddSingleton(new MogeLogOptions(PlatformSubfolder: "web"));
builder.Services.AddSingleton<ILogFileStore, BrowserLogFileStore>();
builder.Services.AddSingleton<MogeLogService>();
builder.Logging.Services.AddSingleton<ILoggerProvider, MogeFileLoggerProvider>();

builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });

await builder.Build().RunAsync();
