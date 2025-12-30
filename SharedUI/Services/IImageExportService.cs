using Microsoft.AspNetCore.Components;

namespace SharedUI.Services;

public interface IImageExportService
{
    Task SaveAsync(ElementReference canvas, string suggestedFileName, ImageExportFormat format, CancellationToken cancellationToken = default);
}
