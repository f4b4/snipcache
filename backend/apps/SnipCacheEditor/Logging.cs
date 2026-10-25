using Serilog;
using Serilog.Formatting.Compact;

namespace SnipCacheEditor;

/// <summary>
/// Serilog setup, condensed from the Cestius <c>AddCestiusLogging</c>: console always, plus a
/// daily rolling file under <c>{App:DataDirectory or content root}/logs</c>, readable text in
/// development and compact JSON in production. The <c>Serilog</c> config section is applied
/// last, so levels and extra sinks can be changed from appsettings.
/// </summary>
public static class LoggingExtensions
{
    private const string TextTemplate = "{Timestamp:HH:mm:ss} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}";

    public static WebApplicationBuilder AddSnipCacheLogging(this WebApplicationBuilder builder)
    {
        var env = builder.Environment;
        var baseDirectory = builder.Configuration["App:DataDirectory"];
        if (string.IsNullOrWhiteSpace(baseDirectory)) baseDirectory = env.ContentRootPath;
        var logFile = Path.Combine(baseDirectory, "logs", $"{env.ApplicationName.ToLowerInvariant()}-.log");

        builder.Services.AddSerilog((services, lc) =>
        {
            lc.MinimumLevel.Information()
              .Enrich.FromLogContext()
              .ReadFrom.Services(services)
              .WriteTo.Console(outputTemplate: TextTemplate);

            if (env.IsDevelopment())
                lc.WriteTo.File(logFile, outputTemplate: TextTemplate,
                    rollingInterval: RollingInterval.Day, retainedFileCountLimit: 7);
            else
                lc.WriteTo.File(new CompactJsonFormatter(), logFile,
                    rollingInterval: RollingInterval.Day, retainedFileCountLimit: 14,
                    fileSizeLimitBytes: 50L * 1024 * 1024, rollOnFileSizeLimit: true);

            lc.ReadFrom.Configuration(builder.Configuration);
        });
        return builder;
    }
}
