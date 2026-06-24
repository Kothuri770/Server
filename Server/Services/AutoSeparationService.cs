using Dapper;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using Server.Models;
using Server.Repositories;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SkiaSharp;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TrueCapture.Services;
using ZXing;
using ZXing.Common;
using Microsoft.AspNetCore.SignalR;
using Server.Hubs;
using Microsoft.Extensions.Configuration;
using Server.Services.Scanner;
using IConfigurationService = Server.Services.Configuration.IConfigurationService;

namespace Server.Services
{
    public class AutoSeparationService : BackgroundService
    {
        private readonly ILogger<AutoSeparationService> _logger;
        private readonly IServiceProvider _serviceProvider;
        private readonly IHubContext<BatchLockHub> _lockHubContext;
        private readonly IConfiguration _configuration;
        private readonly string _workerId;

        public AutoSeparationService(
            ILogger<AutoSeparationService> logger,
            IServiceProvider serviceProvider,
            IHubContext<BatchLockHub> lockHubContext,
            IConfiguration configuration)
        {
            _logger = logger;
            _serviceProvider = serviceProvider;
            _lockHubContext = lockHubContext;
            _configuration = configuration;
            _workerId = $"AutoSep_Worker_{Guid.NewGuid().ToString().Substring(0, 8)}";
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Auto Separation Service is starting. WorkerId={WorkerId}", _workerId);

            Npgsql.NpgsqlConnection? listenConn = null;
            var connStr = _configuration.GetConnectionString("DefaultConnection") ?? "";
            bool isPostgres = connStr.Contains("Host=", StringComparison.OrdinalIgnoreCase);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    listenConn = await EnsureListenConnectionAsync(listenConn, connStr, isPostgres, stoppingToken);

                    await ProcessAutoSeparationBatches(stoppingToken);

                    await WaitForNextBatchAsync(listenConn, isPostgres, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    _logger.LogInformation("Auto Separation Service received cancellation. Shutting down cleanly.");
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unhandled error in Auto Separation Service main loop. Will retry in 30 seconds.");
                    if (listenConn != null) { await listenConn.DisposeAsync(); listenConn = null; }

                    try
                    {
                        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }
            }

            if (listenConn != null) await listenConn.DisposeAsync();
            _logger.LogInformation("Auto Separation Service has stopped.");
        }

        private async Task<Npgsql.NpgsqlConnection?> EnsureListenConnectionAsync(Npgsql.NpgsqlConnection? listenConn, string connStr, bool isPostgres, CancellationToken stoppingToken)
        {
            if (isPostgres && listenConn == null)
            {
                listenConn = new Npgsql.NpgsqlConnection(connStr);
                await listenConn.OpenAsync(stoppingToken);
                await using var cmd = new Npgsql.NpgsqlCommand("LISTEN batch_step_3", listenConn);
                await cmd.ExecuteNonQueryAsync(stoppingToken);
            }
            return listenConn;
        }

        private async Task WaitForNextBatchAsync(Npgsql.NpgsqlConnection? listenConn, bool isPostgres, CancellationToken stoppingToken)
        {
            if (isPostgres && listenConn != null && listenConn.State == System.Data.ConnectionState.Open)
            {
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                timeoutCts.CancelAfter(TimeSpan.FromMinutes(2));
                try
                {
                    await listenConn.WaitAsync(timeoutCts.Token);
                }
                catch (OperationCanceledException)
                {
                    // Expected during shutdown or timeout — satisfy SonarQube with a comment
                }
            }
            else
            {
                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            }
        }

        private async Task ProcessAutoSeparationBatches(CancellationToken stoppingToken)
        {
            using var scope = _serviceProvider.CreateScope();
            var batchRepository = scope.ServiceProvider.GetRequiredService<IBatchRepository>();

            var maxParallel = _configuration.GetValue<int>("AutoSeparation:MaxParallelWorkers", 5);
            var lockTimeout = _configuration.GetValue<int>("AutoSeparation:LockTimeoutMinutes", 60);

            using var semaphore = new SemaphoreSlim(maxParallel);
            var tasks = new HashSet<Task>();

            while (!stoppingToken.IsCancellationRequested)
            {
                await semaphore.WaitAsync(stoppingToken);

                Batch? batch;
                try
                {
                    batch = await batchRepository.PickNextBatchAsync(3, _workerId, lockTimeout);
                }
                catch (OperationCanceledException)
                {
                    semaphore.Release();
                    throw;
                }
                catch (Exception ex)
                {
                    semaphore.Release();
                    _logger.LogError(ex, "Error picking next AutoSeparation batch. Will retry next cycle.");
                    break;
                }

                if (batch == null)
                {
                    semaphore.Release();
                    break; // No more batches this cycle
                }

                var capturedBatch = batch;

                var task = Task.Run(async () =>
                {
                    try
                    {
                        await ProcessSingleBatchWithLock(capturedBatch);
                    }
                    catch (Exception ex)
                    {
                        // Swallow here — individual batch exception is already handled inside
                        // ProcessSingleBatchWithLock. This prevents Task.WhenAll from faulting
                        // and abandoning other in-flight tasks.
                        _logger.LogError(ex, "AutoSeparation batch {BatchId} task faulted at top level.", capturedBatch.ID);
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                });
                
                lock (tasks) { tasks.Add(task); }
                _ = task.ContinueWith(t => { lock (tasks) { tasks.Remove(t); } });
            }

            // Wait for all in-flight tasks — failures are already swallowed per-task above
            Task[] inFlight;
            lock (tasks) { inFlight = tasks.ToArray(); }
            if (inFlight.Length > 0)
            {
                await Task.WhenAll(inFlight);
            }
        }

        private async Task ProcessSingleBatchWithLock(Batch batch)
        {
            using var scope = _serviceProvider.CreateScope();
            var batchRepository = scope.ServiceProvider.GetRequiredService<IBatchRepository>();
            var verifyRepository = scope.ServiceProvider.GetRequiredService<IVerifyRepository>();
            var configService = scope.ServiceProvider.GetRequiredService<IConfigurationService>();
            var batchLogService = scope.ServiceProvider.GetRequiredService<IBatchLogService>();
            var separationService = scope.ServiceProvider.GetRequiredService<ISeparationService>();

            int batchId = batch.ID;
            int batchTypeId = batch.BatchTypeId;

            try
            {
                _logger.LogInformation("Worker {WorkerId} starting AutoSeparation for Batch {BatchId}", _workerId, batchId);

                await _lockHubContext.Clients.Group("monitor").SendAsync("BatchStatusChanged", new
                {
                    BatchId = batchId,
                    Status = "Running",
                    WorkerId = _workerId,
                    Step = "Auto Separation"
                }, CancellationToken.None);

                await batchLogService.StartTaskTimingAsync(new BatchTaskTimingRequest { BatchId = batchId, TaskId = 3, Status = "Start" });

                // Delegate to the shared separation service
                await separationService.ProcessBatchSeparationAsync(batchId, batchTypeId, verifyRepository, configService, batchLogService);

                await batchRepository.MoveToNextStepAsync(batchId, "system");
                _logger.LogInformation("Batch {BatchId} auto separation completed successfully", batchId);
                await batchLogService.StartTaskTimingAsync(new BatchTaskTimingRequest { BatchId = batchId, TaskId = 3, Status = "End" });

                await _lockHubContext.Clients.Group("monitor").SendAsync("BatchStatusChanged", new
                {
                    BatchId = batchId,
                    Status = "Completed",
                    WorkerId = _workerId,
                    Step = "Auto Separation"
                }, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in AutoSeparation for Batch {BatchId}. Moving to exception step.", batchId);
                // ... (rest of error handling stays same)
                try
                {
                    await batchRepository.MoveToExceptionStepAsync(batchId, "system");
                }
                catch (Exception moveEx)
                {
                    _logger.LogError(moveEx, "Failed to move Batch {BatchId} to exception step", batchId);
                }

                try
                {
                    await _lockHubContext.Clients.Group("monitor").SendAsync("BatchStatusChanged", new
                    {
                        BatchId = batchId,
                        Status = "Error",
                        Message = ex.Message,
                        WorkerId = _workerId,
                        Step = "Auto Separation"
                    }, CancellationToken.None);
                }
                catch (Exception sigEx)
                {
                    _logger.LogWarning(sigEx, "Failed to send SignalR error for Batch {BatchId}", batchId);
                }
            }
            finally
            {
                // Always release the distributed lock
                try
                {
                    await batchRepository.ReleaseBatchAsync(batchId, _workerId);
                }
                catch (Exception releaseEx)
                {
                    _logger.LogError(releaseEx, "CRITICAL: Failed to release lock for Batch {BatchId}. Batch may stay locked until timeout.", batchId);
                }
            }
        }
    }
}
