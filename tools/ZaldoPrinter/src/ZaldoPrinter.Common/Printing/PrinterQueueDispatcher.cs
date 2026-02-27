using System.Collections.Concurrent;
using System.Threading.Channels;
using ZaldoPrinter.Common.Models;
using ZaldoPrinter.Common.System;

namespace ZaldoPrinter.Common.Printing;

public enum PrintOperationType
{
    Receipt,
    Drawer,
    Cut,
    Test,
    Raw,
}

public sealed class PrintJobResult
{
    public bool Ok { get; init; }
    public string Message { get; init; } = string.Empty;
    public string PrinterId { get; init; } = string.Empty;
    public string JobId { get; init; } = string.Empty;
    public int Attempts { get; init; }
    public string Operation { get; init; } = string.Empty;
    public DateTimeOffset CompletedAt { get; init; } = DateTimeOffset.Now;
}

public sealed class PrintJobRequest
{
    public string JobId { get; init; } = Guid.NewGuid().ToString("N");
    public PrinterProfile Printer { get; init; } = new();
    public byte[] Bytes { get; init; } = Array.Empty<byte>();
    public int RetryCount { get; init; } = 1;
    public int TimeoutMs { get; init; } = 3000;
    public PrintOperationType OperationType { get; init; }
    public TaskCompletionSource<PrintJobResult> Completion { get; init; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
}

public sealed class PrinterQueueDispatcher : IDisposable
{
    private readonly ConcurrentDictionary<string, PrinterQueueWorker> _workers = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, PrintJobResult> _lastByPrinter = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentQueue<string> _recentErrors = new();
    private readonly IPrinterTransport _networkTransport;
    private readonly IPrinterTransport _usbTransport;
    private readonly FileLog _log;

    public PrinterQueueDispatcher(IPrinterTransport networkTransport, IPrinterTransport usbTransport, FileLog log)
    {
        _networkTransport = networkTransport;
        _usbTransport = usbTransport;
        _log = log;
    }

    public Task<PrintJobResult> EnqueueAsync(PrintJobRequest request, CancellationToken cancellationToken)
    {
        if (request.Printer.Id.Length == 0)
        {
            throw new InvalidOperationException("Printer id is required.");
        }

        var worker = _workers.GetOrAdd(request.Printer.Id, _ => new PrinterQueueWorker(request.Printer.Id, ExecuteAsync, RegisterResult, _log));
        return worker.EnqueueAsync(request, cancellationToken);
    }

    public IReadOnlyDictionary<string, int> PendingByPrinter()
    {
        return _workers.ToDictionary(pair => pair.Key, pair => pair.Value.PendingCount, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyDictionary<string, PrintJobResult> LastByPrinter()
    {
        return _lastByPrinter.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<string> RecentErrors(int maxItems = 20)
    {
        var safeMax = Math.Clamp(maxItems, 1, 100);
        var snapshot = _recentErrors.ToArray();
        if (snapshot.Length <= safeMax)
        {
            return snapshot;
        }

        return snapshot[^safeMax..];
    }

    private async Task<PrintJobResult> ExecuteAsync(PrintJobRequest request, CancellationToken cancellationToken)
    {
        var attempts = Math.Max(1, request.RetryCount + 1);
        for (var currentAttempt = 1; currentAttempt <= attempts; currentAttempt++)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var transport = request.Printer.ModeNormalized() == "network" ? _networkTransport : _usbTransport;
                await transport.SendAsync(request.Printer, request.Bytes, request.TimeoutMs, cancellationToken).ConfigureAwait(false);

                var success = new PrintJobResult
                {
                    Ok = true,
                    Message = "Printed",
                    PrinterId = request.Printer.Id,
                    JobId = request.JobId,
                    Attempts = currentAttempt,
                    Operation = request.OperationType.ToString(),
                    CompletedAt = DateTimeOffset.Now,
                };
                RegisterResult(success);
                return success;
            }
            catch (Exception ex)
            {
                _log.Error($"Print job failed (job={request.JobId}, printer={request.Printer.Id}, attempt={currentAttempt}/{attempts}).", ex);

                if (currentAttempt >= attempts)
                {
                    var failed = new PrintJobResult
                    {
                        Ok = false,
                        Message = ex.Message,
                        PrinterId = request.Printer.Id,
                        JobId = request.JobId,
                        Attempts = currentAttempt,
                        Operation = request.OperationType.ToString(),
                        CompletedAt = DateTimeOffset.Now,
                    };
                    RegisterResult(failed);
                    return failed;
                }

                var backoff = Math.Min(1000, 180 * currentAttempt);
                await Task.Delay(backoff, cancellationToken).ConfigureAwait(false);
            }
        }

        var fallback = new PrintJobResult
        {
            Ok = false,
            Message = "Unexpected queue failure.",
            PrinterId = request.Printer.Id,
            JobId = request.JobId,
            Attempts = attempts,
            Operation = request.OperationType.ToString(),
            CompletedAt = DateTimeOffset.Now,
        };
        RegisterResult(fallback);
        return fallback;
    }

    private void RegisterResult(PrintJobResult result)
    {
        if (result.PrinterId.Length > 0)
        {
            _lastByPrinter[result.PrinterId] = result;
        }

        if (result.Ok)
        {
            return;
        }

        var stamp = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss");
        _recentErrors.Enqueue($"[{stamp}] {result.PrinterId}: {result.Message}");
        while (_recentErrors.Count > 120)
        {
            _recentErrors.TryDequeue(out _);
        }
    }

    public void Dispose()
    {
        foreach (var worker in _workers.Values)
        {
            worker.Dispose();
        }
        _workers.Clear();
    }

    private sealed class PrinterQueueWorker : IDisposable
    {
        private readonly Channel<PrintJobRequest> _channel;
        private readonly CancellationTokenSource _cts = new();
        private readonly Func<PrintJobRequest, CancellationToken, Task<PrintJobResult>> _executor;
        private readonly Action<PrintJobResult> _resultSink;
        private readonly FileLog _log;
        private readonly Task _loop;
        private int _pending;

        public PrinterQueueWorker(
            string printerId,
            Func<PrintJobRequest, CancellationToken, Task<PrintJobResult>> executor,
            Action<PrintJobResult> resultSink,
            FileLog log)
        {
            PrinterId = printerId;
            _executor = executor;
            _resultSink = resultSink;
            _log = log;
            _channel = Channel.CreateUnbounded<PrintJobRequest>(new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false,
                AllowSynchronousContinuations = false,
            });
            _loop = Task.Run(ProcessLoopAsync);
        }

        public string PrinterId { get; }

        public int PendingCount => Math.Max(0, Volatile.Read(ref _pending));

        public async Task<PrintJobResult> EnqueueAsync(PrintJobRequest request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _pending);
            await _channel.Writer.WriteAsync(request, cancellationToken).ConfigureAwait(false);
            using var registration = cancellationToken.Register(() => request.Completion.TrySetCanceled(cancellationToken));
            return await request.Completion.Task.ConfigureAwait(false);
        }

        private async Task ProcessLoopAsync()
        {
            try
            {
                await foreach (var request in _channel.Reader.ReadAllAsync(_cts.Token).ConfigureAwait(false))
                {
                    try
                    {
                        var result = await _executor(request, _cts.Token).ConfigureAwait(false);
                        request.Completion.TrySetResult(result);
                    }
                    catch (Exception ex)
                    {
                        _log.Error($"Queue worker crashed while processing printer '{PrinterId}'.", ex);
                        var result = new PrintJobResult
                        {
                            Ok = false,
                            Message = ex.Message,
                            PrinterId = request.Printer.Id,
                            JobId = request.JobId,
                            Attempts = 1,
                            Operation = request.OperationType.ToString(),
                            CompletedAt = DateTimeOffset.Now,
                        };
                        _resultSink(result);
                        request.Completion.TrySetResult(result);
                    }
                    finally
                    {
                        Interlocked.Decrement(ref _pending);
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
        }

        public void Dispose()
        {
            _channel.Writer.TryComplete();
            _cts.Cancel();
            try
            {
                _loop.Wait(TimeSpan.FromSeconds(2));
            }
            catch
            {
            }
            _cts.Dispose();
        }
    }
}
