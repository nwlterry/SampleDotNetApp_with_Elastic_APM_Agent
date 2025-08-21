using Elastic.Apm;
using Elastic.Apm.Api;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http; // Required for IHttpContextAccessor
using Microsoft.AspNetCore.OpenApi; // Required for WithOpenApi
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging; // Required for ILogger
using Microsoft.OpenApi.Models; // Required for Swagger/OpenAPI
using SampleDotNetApp; // Required for MicrosoftLoggerToApmLogger
using System.Net; // Required for ServicePointManager
using System.Net.Security; // Required for SslPolicyErrors

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "SampleDotNetApp", Version = "v1" });
});
builder.Services.AddHttpContextAccessor(); // Required for Elastic APM

// Configure HttpClient for Elastic APM (bypass certificate validation for testing only)
builder.Services.AddHttpClient("ElasticApm").ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
{
    ServerCertificateCustomValidationCallback = (message, cert, chain, errors) =>
    {
        // Log SSL errors to console for debugging
        if (errors != SslPolicyErrors.None)
        {
            Console.WriteLine($"SSL validation failed: {errors}, Cert: {cert?.Subject}, Issuer: {cert?.Issuer}");
        }
        return true; // Bypass for testing
    }
});

// Force TLS 1.2 or 1.3 to avoid SSLv3 issues
ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls13;

var app = builder.Build();

// Configure the HTTP request pipeline.
app.Logger.LogInformation("Starting application...");

// Enable Swagger in all environments for testing
app.UseSwagger();
app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "SampleDotNetApp v1"));
app.Logger.LogInformation("Swagger enabled at /swagger");

// Manually configure Elastic APM
try
{
    app.Logger.LogInformation("Initializing Elastic APM...");
    Agent.Setup(new AgentComponents(logger: new MicrosoftLoggerToApmLogger(app.Logger)));
    app.Logger.LogInformation("Elastic APM initialized successfully");
}
catch (Exception ex)
{
    app.Logger.LogError(ex, "Failed to initialize Elastic APM. Continuing without APM.");
}

// Use Elastic APM middleware to capture HTTP transactions
app.Use(async (context, next) =>
{
    app.Logger.LogInformation($"Processing request: {context.Request.Method} {context.Request.Path}");
    var transaction = Agent.Tracer.StartTransaction($"HTTP {context.Request.Method} {context.Request.Path}", ApiConstants.TypeRequest);
    try
    {
        await next(context);
    }
    catch (Exception ex)
    {
        transaction?.CaptureException(ex);
        app.Logger.LogError(ex, "Request processing failed");
        throw;
    }
    finally
    {
        transaction?.End();
    }
});

app.MapGet("/api/hello", () =>
{
    app.Logger.LogInformation("Handling /api/hello request");
    return "Hello from Elastic APM Sample App!";
})
   .WithName("HelloWorld")
   .WithOpenApi(c =>
   {
       c.Tags = new List<OpenApiTag> { new() { Name = "Sample" } };
       return c;
   });

app.MapGet("/api/error", () =>
{
    app.Logger.LogInformation("Handling /api/error request");
    throw new Exception("This is a test error!");
})
   .WithName("TestError")
   .WithOpenApi(c =>
   {
       c.Tags = new List<OpenApiTag> { new() { Name = "Sample" } };
       return c;
   });

app.MapGet("/api/messaging", () =>
{
    app.Logger.LogInformation("Handling /api/messaging request");
    // Start a custom transaction of type "messaging"
    var transaction = Agent.Tracer.StartTransaction("ProcessMessage", "messaging");
    try
    {
        // Simulate some work, e.g., processing a message
        var span = transaction.StartSpan("SimulateMessageSend", "messaging.send");
        try
        {
            // Pretend to send a message (e.g., delay or external call)
            Thread.Sleep(500); // Simulate delay
        }
        finally
        {
            span.End();
        }
        return "Messaging trace sent!";
    }
    catch (Exception ex)
    {
        transaction.CaptureException(ex);
        throw;
    }
    finally
    {
        transaction.End();
    }
})
   .WithName("MessagingTrace")
   .WithOpenApi(c =>
   {
       c.Tags = new List<OpenApiTag> { new() { Name = "Messaging" } };
       return c;
   });

app.Logger.LogInformation("Application running");
app.Run();
