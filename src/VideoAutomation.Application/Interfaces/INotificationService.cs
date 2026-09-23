namespace VideoAutomation.Application.Interfaces;

public interface INotificationService
{
    Task SendAsync(string message, CancellationToken ct = default);
    Task SendWithImageAsync(string message, string imagePath, CancellationToken ct = default);
}
