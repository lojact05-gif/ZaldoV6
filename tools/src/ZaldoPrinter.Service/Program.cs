using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http.Json;
using ZaldoPrinter.Common.Config;
using ZaldoPrinter.Common.Models;
using ZaldoPrinter.Common.Printing;
using ZaldoPrinter.Common.System;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseWindowsService(options =>
{
    options.ServiceName = "Zaldo Printer Service";
});

builder.WebHost.UseUrls("http://127.0.0.1:16161");

builder.Services.Configure<JsonOptions>(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
    options.SerializerOptions.WriteIndented = false;
});

builder.Services.AddSingleton<FileLog>();
builder.Services.AddSingleton<JsonConfigStore>();
builder.Services.AddSingleton<WindowsPrinterCatalog>();
builder.Services.AddSingleton<EscPosBuilder>();
builder.Services.AddSingleton<NetworkPrinterTransport>();
builder.Services.AddSingleton<UsbRawSpoolerTransport>();
builder.Services.AddSingleton<PrinterQueueDispatcher>(sp => new PrinterQueueDispatcher(
    sp.GetRequiredService<NetworkPrinterTransport>(),
    sp.GetRequiredService<UsbRawSpoolerTransport>(),
    sp.GetRequiredService<FileLog>()
));

var app = builder.Build();

var log = app.Services.GetRequiredService<FileLog>();
log.Info("Zaldo Printer service booting on 127.0.0.1:16161.");

app.Use(async (context, next) =>
{
    if (!IsLoopback(context.Connection.RemoteIpAddress))
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        await context.Response.WriteAsJsonAsync(new { ok = false, error = "Only localhost is allowed." });
        return;
    }

    await next();
});

app.Use(async (context, next) =>
{
    var store = context.RequestServices.GetRequiredService<JsonConfigStore>();
    var config = store.Get();
    ApplyCorsHeaders(context, config);

    if (HttpMethods.IsOptions(context.Request.Method))
    {
        context.Response.StatusCode = StatusCodes.Status204NoContent;
        return;
    }

    await next();
});

app.Use(async (context, next) =>
{
    if (IsPublicRoute(context.Request.Path, context.Request.Method))
    {
        await next();
        return;
    }

    var store = context.RequestServices.GetRequiredService<JsonConfigStore>();
    var config = store.Get();

    var received = context.Request.Headers["X-ZALDO-TOKEN"].ToString().Trim();
    if (!TokenMatches(config.PairingToken, received))
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await context.Response.WriteAsJsonAsync(new { ok = false, error = "Unauthorized token." });
        return;
    }

    await next();
});

app.MapGet("/health", (JsonConfigStore store, PrinterQueueDispatcher dispatcher) =>
{
    var config = store.Get();
    return Results.Ok(new
    {
        ok = true,
        app = "Zaldo Printer",
        version = "1.0.0",
        listening = "127.0.0.1:16161",
        printersConfigured = config.Printers.Count,
        defaultPrinterId = config.DefaultPrinterId,
        pendingByPrinter = dispatcher.PendingByPrinter(),
        lastPrintByPrinter = dispatcher.LastByPrinter(),
        recentErrors = dispatcher.RecentErrors(25),
        now = DateTimeOffset.Now,
    });
});

app.MapGet("/printers", (WindowsPrinterCatalog catalog, JsonConfigStore store) =>
{
    var installed = catalog.EnumerateInstalledPrinters();
    var config = store.Get();

    return Results.Ok(new
    {
        ok = true,
        printers = installed,
        configured = config.Printers,
        defaultPrinterId = config.DefaultPrinterId,
    });
});

app.MapGet("/config", (JsonConfigStore store) =>
{
    var config = store.Get();
    return Results.Ok(new { ok = true, config });
});

app.MapPost("/config", (AppConfig incoming, JsonConfigStore store, FileLog logger) =>
{
    var current = store.Get();
    if (string.IsNullOrWhiteSpace(incoming.PairingToken))
    {
        incoming.PairingToken = current.PairingToken;
    }

    var saved = store.Save(incoming);
    logger.Info($"Configuration updated. Printers={saved.Printers.Count}, default={saved.DefaultPrinterId}.");
    return Results.Ok(new { ok = true, config = saved });
});

app.MapPost("/token/regenerate", (JsonConfigStore store, FileLog logger) =>
{
    var token = store.RegenerateToken();
    logger.Warn("Pairing token regenerated.");
    return Results.Ok(new { ok = true, pairingToken = token });
});

app.MapPost("/print/receipt", async (HttpContext context, ReceiptPrintRequest request, JsonConfigStore store, EscPosBuilder escPos, PrinterQueueDispatcher dispatcher, FileLog logger) =>
{
    var config = store.Get();
    var profile = ResolveTargetPrinter(context, config, null);

    var receipt = request.Receipt ?? new ReceiptPayload();
    if (string.IsNullOrWhiteSpace(receipt.PrintedAt))
    {
        receipt.PrintedAt = DateTimeOffset.Now.ToString("dd/MM/yyyy HH:mm:ss");
    }

    var applyCut = request.Cut ?? profile.Cut.Enabled;
    var applyDrawer = request.OpenDrawer ?? profile.CashDrawer.Enabled;

    var bytes = escPos.BuildReceipt(receipt, profile, applyCut, request.CutMode, applyDrawer);
    var result = await EnqueueAndWait(dispatcher, profile, bytes, config, PrintOperationType.Receipt, context.RequestAborted);

    if (!result.Ok)
    {
        logger.Error($"Receipt print failed on printer '{profile.Id}': {result.Message}");
        return Results.UnprocessableEntity(new { ok = false, error = result.Message, printerId = profile.Id, jobId = result.JobId });
    }

    return Results.Ok(new { ok = true, message = "Receipt printed.", printerId = profile.Id, jobId = result.JobId, attempts = result.Attempts });
});

app.MapPost("/print/cashdrawer", async (HttpContext context, JsonConfigStore store, EscPosBuilder escPos, PrinterQueueDispatcher dispatcher) =>
{
    var config = store.Get();
    var profile = ResolveTargetPrinter(context, config, null);

    var bytes = escPos.BuildDrawer(profile);
    var result = await EnqueueAndWait(dispatcher, profile, bytes, config, PrintOperationType.Drawer, context.RequestAborted);

    if (!result.Ok)
    {
        return Results.UnprocessableEntity(new { ok = false, error = result.Message, printerId = profile.Id, jobId = result.JobId });
    }

    return Results.Ok(new { ok = true, message = "Cash drawer signal sent.", printerId = profile.Id, jobId = result.JobId, attempts = result.Attempts });
});

app.MapPost("/print/cut", async (HttpContext context, CutRequest request, JsonConfigStore store, EscPosBuilder escPos, PrinterQueueDispatcher dispatcher) =>
{
    var config = store.Get();
    var profile = ResolveTargetPrinter(context, config, null);

    var bytes = escPos.BuildCut(profile, request.Mode);
    var result = await EnqueueAndWait(dispatcher, profile, bytes, config, PrintOperationType.Cut, context.RequestAborted);

    if (!result.Ok)
    {
        return Results.UnprocessableEntity(new { ok = false, error = result.Message, printerId = profile.Id, jobId = result.JobId });
    }

    return Results.Ok(new { ok = true, message = "Cut command sent.", printerId = profile.Id, jobId = result.JobId, attempts = result.Attempts });
});

app.MapPost("/print/test", async (HttpContext context, TestPrintRequest request, JsonConfigStore store, EscPosBuilder escPos, PrinterQueueDispatcher dispatcher) =>
{
    var config = store.Get();
    var profile = ResolveTargetPrinter(context, config, null);

    var title = string.IsNullOrWhiteSpace(request.Title) ? "Teste Zaldo Printer" : request.Title.Trim();
    var bytes = escPos.BuildTestPrint(profile, title);
    var result = await EnqueueAndWait(dispatcher, profile, bytes, config, PrintOperationType.Test, context.RequestAborted);

    if (!result.Ok)
    {
        return Results.UnprocessableEntity(new { ok = false, error = result.Message, printerId = profile.Id, jobId = result.JobId });
    }

    return Results.Ok(new { ok = true, message = "Test print sent.", printerId = profile.Id, jobId = result.JobId, attempts = result.Attempts });
});

app.Run();

static async Task<PrintJobResult> EnqueueAndWait(
    PrinterQueueDispatcher dispatcher,
    PrinterProfile profile,
    byte[] bytes,
    AppConfig config,
    PrintOperationType operationType,
    CancellationToken cancellationToken)
{
    var request = new PrintJobRequest
    {
        Printer = profile,
        Bytes = bytes,
        RetryCount = config.RetryCount,
        TimeoutMs = config.RequestTimeoutMs,
        OperationType = operationType,
    };

    return await dispatcher.EnqueueAsync(request, cancellationToken);
}

static bool IsPublicRoute(PathString path, string method)
{
    if (path.Equals("/health", StringComparison.OrdinalIgnoreCase))
    {
        return true;
    }

    if (path.Equals("/config", StringComparison.OrdinalIgnoreCase) && HttpMethods.IsGet(method))
    {
        return true;
    }

    return false;
}

static void ApplyCorsHeaders(HttpContext context, AppConfig config)
{
    var origin = context.Request.Headers.Origin.ToString().Trim();
    if (origin.Length == 0)
    {
        return;
    }

    var allowed = config.AllowedOrigins.Any(item => string.Equals(item.Trim(), origin, StringComparison.OrdinalIgnoreCase));
    if (!allowed)
    {
        return;
    }

    context.Response.Headers["Access-Control-Allow-Origin"] = origin;
    context.Response.Headers["Vary"] = "Origin";
    context.Response.Headers["Access-Control-Allow-Headers"] = "Content-Type, X-ZALDO-TOKEN, X-PRINTER-ID";
    context.Response.Headers["Access-Control-Allow-Methods"] = "GET, POST, OPTIONS";
}

static bool IsLoopback(IPAddress? ipAddress)
{
    if (ipAddress is null)
    {
        return false;
    }

    if (IPAddress.IsLoopback(ipAddress))
    {
        return true;
    }

    if (ipAddress.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
    {
        return ipAddress.Equals(IPAddress.IPv6Loopback);
    }

    return false;
}

static bool TokenMatches(string expected, string provided)
{
    if (string.IsNullOrWhiteSpace(expected) || string.IsNullOrWhiteSpace(provided))
    {
        return false;
    }

    var left = Encoding.UTF8.GetBytes(expected.Trim());
    var right = Encoding.UTF8.GetBytes(provided.Trim());
    if (left.Length != right.Length)
    {
        return false;
    }

    return CryptographicOperations.FixedTimeEquals(left, right);
}

static PrinterProfile ResolveTargetPrinter(HttpContext context, AppConfig config, string? explicitPrinterId)
{
    var printerId = explicitPrinterId;
    if (string.IsNullOrWhiteSpace(printerId))
    {
        printerId = context.Request.Headers["X-PRINTER-ID"].ToString();
    }

    if (string.IsNullOrWhiteSpace(printerId) && context.Request.Query.TryGetValue("printerId", out var queryValue))
    {
        printerId = queryValue.ToString();
    }

    return PrinterProfileResolver.Resolve(config, printerId);
}
