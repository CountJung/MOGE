using Microsoft.AspNetCore.Components;

namespace SharedUI.Services;

public interface IImageExportService
{
    Task SavePngAsync(ElementReference canvas, string suggestedFileName, CancellationToken cancellationToken = default);
}
