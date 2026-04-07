using System;
using System.IO;
using System.Reflection;
using Figgle.Fonts;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Replicator.DependencyInjection;
using Serilog;
using WebSystemTools.ApiExceptionHandler.DependencyInjection;
using WebSystemTools.SerilogLogger;
using WebSystemTools.SwaggerTools.DependencyInjection;
using WebSystemTools.TestToolsApi.DependencyInjection;
using WebSystemTools.WindowsServiceTools;
//using ReServer.DependencyInjection;
//using WebSystemTools.ConfigurationEncrypt;

try
{
    Console.WriteLine("Loading...");

    const string appName = "Replicator";
    //const string appKey = "CF39BBE3-531B-417E-AC20-3605313D0F94";
    const int versionCount = 1;

    string header = $"{appName} {Assembly.GetEntryAssembly()?.GetName().Version}";
    Console.WriteLine(FiggleFonts.Standard.Render(header));

    var builder = WebApplication.CreateBuilder(new WebApplicationOptions
    {
        ContentRootPath = AppContext.BaseDirectory, Args = args
    });

    bool debugMode = builder.Environment.IsDevelopment();

    var logger = builder.Host.UseSerilogLogger(debugMode, builder.Configuration);
    var debugLogger = debugMode ? logger : null;

    builder.Host.UseWindowsServiceOnWindows(debugLogger, args);

    //builder.Configuration.AddConfigurationEncryption(debugLogger, appKey);

    // @formatter:off
    builder.Services
        .AddSwagger(debugLogger, true, versionCount, appName)

        .AddHostedServices(debugMode).AddHttpClient();
    // @formatter:on

    // ReSharper disable once using
    await using var app = builder.Build();

    Log.Information("Directory.GetCurrentDirectory() = {0}", Directory.GetCurrentDirectory());
    Log.Information("AppContext.BaseDirectory = {0}", AppContext.BaseDirectory);

    app.UseSwaggerServices(debugLogger);
    app.UseApiExceptionHandler(debugLogger);
    app.UseTestToolsApiEndpoints(debugLogger);

    await app.RunAsync();

    Log.Information("Finish");
    return 0;
}
catch (Exception e)
{
    Log.Fatal(e, "Host terminated unexpectedly");
    return 1;
}
finally
{
    await Log.CloseAndFlushAsync();
}
