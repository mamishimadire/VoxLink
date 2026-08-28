namespace VoxLink.Api.Billing;

/// <summary>
/// Checks every hour for subscriptions whose billing period has ended and
/// generates/emails their invoice automatically. Safe to run alongside a
/// manual trigger — both call the same idempotent RunOnceAsync.
/// </summary>
public class InvoiceGenerationBackgroundService : BackgroundService
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(1);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<InvoiceGenerationBackgroundService> _logger;

    public InvoiceGenerationBackgroundService(IServiceScopeFactory scopeFactory, ILogger<InvoiceGenerationBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<InvoiceGenerationService>();
                var generated = await service.RunOnceAsync(stoppingToken);
                if (generated > 0)
                {
                    _logger.LogInformation("Generated {Count} invoice(s)", generated);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Invoice generation cycle failed");
            }

            try
            {
                await Task.Delay(CheckInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
            }
        }
    }
}
