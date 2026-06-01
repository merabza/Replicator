using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SystemTools.BackgroundTasks;

namespace Replicator.HostedServices;

public sealed class TimedHostedService : IHostedService, IDisposable
{
    private readonly IHostApplicationLifetime _appLifetime;
    private readonly ILogger<TimedHostedService> _logger;
    private readonly IProcesses _processes;

    private int _executionCount;
    private Timer? _timer;

    public TimedHostedService(ILogger<TimedHostedService> logger, IProcesses processes,
        IHostApplicationLifetime appLifetime)
    {
        _logger = logger;
        _processes = processes;
        _appLifetime = appLifetime;
    }

    public void Dispose()
    {
        _timer?.Dispose();
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Timed Hosted Service running.");

        _appLifetime.ApplicationStopping.Register(OnStopping);

        // ReSharper disable once DisposableConstructor
        _timer = new Timer(DoWork, null, TimeSpan.Zero, TimeSpan.FromMinutes(1));

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Timed Hosted Service is stopping.");

        _timer?.Change(Timeout.Infinite, 0);

        return Task.CompletedTask;
    }

    private void DoWork(object? state)
    {
        int count = Interlocked.Increment(ref _executionCount);

        if (_logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation("Timed Hosted Service is working. Count: {Count}, ", count);
        }
    }

    private void OnStopping()
    {
        _logger.LogInformation("Application is stopping, cancelling all processes...");
        _processes.CancelProcesses();
        _logger.LogInformation("All processes cancelled");
    }
}
