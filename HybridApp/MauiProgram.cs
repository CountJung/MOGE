using Microsoft.Extensions.Logging;
using MudBlazor.Services;
using HybridApp.Services;
using HybridApp.Services.Logging;
using HybridApp.Services.Settings;
using SharedUI.Logging;
using SharedUI.Services;
using SharedUI.Services.Settings;

namespace HybridApp;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
			});

		builder.Services.AddMauiBlazorWebView();
		builder.Services.AddMudServices();
		builder.Services.AddScoped<ImageDocumentState>();
		builder.Services.AddScoped<IImageFilePicker, MauiImageFilePicker>();
		builder.Services.AddScoped<IImageExportService, WindowsImageExportService>();
		builder.Services.AddScoped<ImageProcessorService>();
		builder.Services.AddScoped<IAppSettingsStore, MauiAppSettingsStore>();
		builder.Services.AddScoped<AppSettingsService>();

		builder.Services.AddSingleton(new MogeLogOptions(PlatformSubfolder: "app"));
		builder.Services.AddSingleton<ILogFileStore, MauiLogFileStore>();
		builder.Services.AddSingleton<MogeLogService>();
		builder.Logging.Services.AddSingleton<ILoggerProvider, MogeFileLoggerProvider>();
		builder.Services.AddScoped<ILogExportService, WindowsLogExportService>();

#if DEBUG
		builder.Services.AddBlazorWebViewDeveloperTools();
		builder.Logging.AddDebug();
#endif

		return builder.Build();
	}
}
