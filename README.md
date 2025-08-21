# Sample .NET 8.0 Application with Elastic APM for OpenShift

This ASP.NET Core Web API targets .NET 8.0.1, integrates with Elastic APM, and is configured for deployment on OpenShift. It connects to an existing APM Server and Elasticsearch and includes a sample trace with a "messaging" transaction type. The changes add a custom `openssl.cnf` to the Docker image to set `CipherString = DEFAULT@SECLEVEL=2`, fix the Swagger 404 error, and ensure the correct APM Server URL to resolve connection issues.

## Program.cs
```csharp
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
```

## MicrosoftLoggerToApmLogger.cs
```csharp
using Elastic.Apm.Logging;
using Microsoft.Extensions.Logging;
using System;

namespace SampleDotNetApp
{
    public class MicrosoftLoggerToApmLogger : IApmLogger
    {
        private readonly ILogger _logger;

        public MicrosoftLoggerToApmLogger(ILogger logger)
        {
            _logger = logger;
        }

        public bool IsEnabled(Elastic.Apm.Logging.LogLevel level) => _logger.IsEnabled(ConvertLevel(level));

        public void Log<TState>(Elastic.Apm.Logging.LogLevel level, TState state, Exception e, Func<TState, Exception, string> formatter)
        {
            _logger.Log(ConvertLevel(level), e, formatter(state, e));
        }

        private static Microsoft.Extensions.Logging.LogLevel ConvertLevel(Elastic.Apm.Logging.LogLevel level)
        {
            return level switch
            {
                Elastic.Apm.Logging.LogLevel.Trace => Microsoft.Extensions.Logging.LogLevel.Trace,
                Elastic.Apm.Logging.LogLevel.Debug => Microsoft.Extensions.Logging.LogLevel.Debug,
                Elastic.Apm.Logging.LogLevel.Information => Microsoft.Extensions.Logging.LogLevel.Information,
                Elastic.Apm.Logging.LogLevel.Warning => Microsoft.Extensions.Logging.LogLevel.Warning,
                Elastic.Apm.Logging.LogLevel.Error => Microsoft.Extensions.Logging.LogLevel.Error,
                Elastic.Apm.Logging.LogLevel.Critical => Microsoft.Extensions.Logging.LogLevel.Critical,
                _ => Microsoft.Extensions.Logging.LogLevel.None,
            };
        }
    }
}
```

## openssl.cnf
```
[openssl_init]
ssl_conf = ssl_sect

[ssl_sect]
system_default = system_default_sect

[system_default_sect]
CipherString = DEFAULT@SECLEVEL=2
```

## appsettings.json
```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning",
      "Elastic.Apm": "Trace",
      "SampleDotNetApp": "Trace"
    }
  },
  "AllowedHosts": "*",
  "Kestrel": {
    "Endpoints": {
      "Http": {
        "Url": "http://localhost:8080"
      }
    }
  },
  "ElasticApm": {
    "ServiceName": "SampleDotNetApp",
    "Environment": "development",
    "TransactionSampleRate": 1.0,
    "LogLevel": "Trace",
    "ServerUrls": "<your-apm-server-url>", // e.g., https://apm-server.example.com:8200
    "SecretToken": "<your-apm-secret-token-if-required>" // Optional
  }
}
```

## SampleDotNetApp.csproj
```xml
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Elastic.Apm.NetCoreAll" Version="1.27.3" />
    <PackageReference Include="Swashbuckle.AspNetCore" Version="6.7.3" />
    <PackageReference Include="Microsoft.AspNetCore.OpenApi" Version="8.0.8" />
  </ItemGroup>
  <ItemGroup>
    <None Update="openssl.cnf">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    </None>
  </ItemGroup>
</Project>
```

## Dockerfile
```dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 80
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:8.0.100 AS build
WORKDIR /src
COPY ["SampleDotNetApp.csproj", "."]
RUN dotnet restore "SampleDotNetApp.csproj"
COPY . .
WORKDIR "/src"
RUN dotnet build "SampleDotNetApp.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "SampleDotNetApp.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
# Add CA certificates and custom OpenSSL config
RUN apt-get update && apt-get install -y ca-certificates openssl
COPY openssl.cnf /etc/ssl/openssl.cnf
RUN update-ca-certificates
ENV OPENSSL_CONF=/etc/ssl/openssl.cnf
ENTRYPOINT ["dotnet", "SampleDotNetApp.dll"]
```

## OpenShift Deployment Files

### deployment.yaml
```yaml
apiVersion: apps/v1
kind: Deployment
metadata:
  name: sample-dotnet-app
spec:
  replicas: 1
  selector:
    matchLabels:
      app: sample-dotnet-app
  template:
    metadata:
      labels:
        app: sample-dotnet-app
    spec:
      containers:
      - name: sample-dotnet-app
        image: quay.io/nwlterry/elk-apm-sampledotnetapp:0.5
        ports:
        - containerPort: 80
        env:
        - name: ASPNETCORE_ENVIRONMENT
          value: "Development"
        - name: ELASTIC_APM_SERVER_URLS
          value: "<your-apm-server-url>"  # e.g., https://apm-server.example.com:8200
        - name: ELASTIC_APM_SECRET_TOKEN
          value: "<your-apm-secret-token-if-required>"  # Optional
        - name: OPENSSL_CONF
          value: "/etc/ssl/openssl.cnf"
        # Optional: Add custom CA certificate path if needed
        # - name: ELASTIC_APM_SSL_CA_CERT
        #   value: "/usr/local/share/ca-certificates/ca-cert.pem"
```

### service.yaml
```yaml
apiVersion: v1
kind: Service
metadata:
  name: sample-dotnet-app
spec:
  selector:
    app: sample-dotnet-app
  ports:
    - protocol: TCP
      port: 80
      targetPort: 80
```

### route.yaml
```yaml
apiVersion: route.openshift.io/v1
kind: Route
metadata:
  name: sample-dotnet-app
spec:
  to:
    kind: Service
    name: sample-dotnet-app
  port:
    targetPort: 80
  tls:
    termination: edge
```

## Steps to Resolve Issues and Deploy

### 1. Create `openssl.cnf`
- Create a file named `openssl.cnf` in the `SampleDotNetApp` directory with the content:
  ```
  [openssl_init]
  ssl_conf = ssl_sect

  [ssl_sect]
  system_default = system_default_sect

  [system_default_sect]
  CipherString = DEFAULT@SECLEVEL=2
  ```

### 2. Build and Test Locally
- **Restore and Build**:
  - Navigate to the project directory:
    ```powershell
    cd C:\Users\terry.ng\SampleDotNetApp
    ```
  - Restore packages:
    ```powershell
    dotnet restore SampleDotNetApp.csproj
    ```
  - Build the project:
    ```powershell
    dotnet build SampleDotNetApp.csproj -c Release
    ```

- **Run Locally**:
  - Set environment variables:
    ```powershell
    $env:ASPNETCORE_ENVIRONMENT="Development"
    $env:ELASTIC_APM_SERVER_URLS="<your-apm-server-url>"  # e.g., https://apm-server.example.com:8200
    $env:OPENSSL_CONF="C:\Users\terry.ng\SampleDotNetApp\openssl.cnf"
    ```
  - Run the application:
    ```powershell
    dotnet run --project SampleDotNetApp.csproj
    ```
  - Test Swagger and endpoints:
    ```powershell
    curl -v http://127.0.0.1:8080/swagger
    curl http://127.0.0.1:8080/api/hello
    curl http://127.0.0.1:8080/api/messaging
    ```

### 3. Build Docker Image
- Build the updated image:
  ```powershell
  docker build -t quay.io/nwlterry/elk-apm-sampledotnetapp:0.5 .
  docker push quay.io/nwlterry/elk-apm-sampledotnetapp:0.5
  ```

### 4. Run Docker Locally
- Stop any existing container:
  ```powershell
  docker stop 141b7d781c89
  ```
- Run with correct port mapping and environment variables:
  ```powershell
  docker run -d -p 8080:80 -e ASPNETCORE_ENVIRONMENT=Development -e ELASTIC_APM_SERVER_URLS="<your-apm-server-url>" -e OPENSSL_CONF=/etc/ssl/openssl.cnf quay.io/nwlterry/elk-apm-sampledotnetapp:0.5
  ```
- Check logs:
  ```powershell
  docker logs <new-container-id>
  ```
- Test Swagger:
  ```powershell
  curl -v http://127.0.0.1:8080/swagger
  ```

### 5. Address SSL Handshake Failure
- **Verify APM Server URL**:
  - Update `appsettings.json` and `deployment.yaml` with the correct `ELASTIC_APM_SERVER_URLS` (e.g., `https://apm-server.example.com:8200`).
  - Test connectivity:
    ```powershell
    curl -v <your-apm-server-url>
    ```

- **Handle HTTPS (If Applicable)**:
  - The `Program.cs` includes a certificate validation bypass for testing:
    ```csharp
    ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => { ... return true; }
    ```
  - For production, add the CA certificate:
    1. Obtain `ca-cert.pem` for the APM Server.
    2. Update the `Dockerfile`:
       ```dockerfile
       COPY ca-cert.pem /usr/local/share/ca-certificates/ca-cert.pem
       RUN update-ca-certificates
       ```
    3. Place `ca-cert.pem` in the project directory.
    4. Remove the certificate bypass in `Program.cs`:
       ```csharp
       // Remove this line
       ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => { ... return true; }
       ```
    5. Add to `deployment.yaml`:
       ```yaml
       - name: ELASTIC_APM_SSL_CA_CERT
         value: "/usr/local/share/ca-certificates/ca-cert.pem"
       ```

- **Verify TLS Version**:
  - The `Program.cs` forces TLS 1.2 or 1.3:
    ```csharp
    ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls13;
    ```
  - Test the APM Server’s TLS support:
    ```powershell
    openssl s_client -connect apm-server.example.com:8200 -tls1_2
    ```

### 6. Deploy to OpenShift
- Update `deployment.yaml` with the new image tag:
  ```yaml
  image: quay.io/nwlterry/elk-apm-sampledotnetapp:0.5
  ```
- Apply the manifests:
  ```powershell
  oc apply -f deployment.yaml
  oc apply -f service.yaml
  oc apply -f route.yaml
  ```
- Get the route URL:
  ```powershell
  oc get route sample-dotnet-app -o jsonpath='{.spec.host}'
  ```

### 7. Test the Application
- Test Swagger and endpoints:
  ```powershell
  curl -v http://127.0.0.1:8080/swagger
  curl http://127.0.0.1:8080/api/hello
  curl http://127.0.0.1:8080/api/messaging
  ```
- In OpenShift:
  ```powershell
  curl -v http://<route-url>/swagger
  curl http://<route-url>/api/hello
  curl http://<route-url>/api/messaging
  ```
- Verify traces in Kibana APM Dashboard (Observability > APM > Services) under "SampleDotNetApp".

## Changes Made
- **Added `openssl.cnf`**:
  - Included `openssl.cnf` in the project with `CipherString = DEFAULT@SECLEVEL=2`.
  - Updated `SampleDotNetApp.csproj` to copy `openssl.cnf` to the output directory.
  - Modified `Dockerfile` to copy `openssl.cnf` to `/etc/ssl/openssl.cnf` and set `OPENSSL_CONF`.
  - Added `OPENSSL_CONF` to `deployment.yaml`.
- **Fixed Swagger 404**:
  - Ensured Swagger is enabled in all environments in `Program.cs`.
  - Added `Kestrel` configuration in `appsettings.json` to use port 8080 locally.
- **APM Server URL**:
  - Ensured `ELASTIC_APM_SERVER_URLS` is set correctly in `appsettings.json` and `deployment.yaml`.
- **Retained Components**:
  - Kept `MicrosoftLoggerToApmLogger.cs`, custom middleware, and `/api/messaging` endpoint.
  - Preserved `WithOpenApi` for Swagger metadata.

## Troubleshooting
- **Swagger 404 or Empty Response**:
  - Verify `ASPNETCORE_ENVIRONMENT=Development` is set.
  - Check logs:
    ```powershell
    docker logs <container-id>
    ```
  - Test locally:
    ```powershell
    $env:ASPNETCORE_ENVIRONMENT="Development"
    $env:ELASTIC_APM_SERVER_URLS="<your-apm-server-url>"
    dotnet run --project SampleDotNetApp.csproj
    ```

- **SSL/Connection Issues**:
  - Check logs for SSL errors:
    ```powershell
    docker logs <container-id>
    oc logs <pod-name>
    ```
  - Test APM Server connectivity:
    ```powershell
    docker exec -it <container-id> bash
    curl -v <your-apm-server-url>
    ```
    Or in OpenShift:
    ```powershell
    oc rsh <pod-name>
    curl -v <your-apm-server-url>
    ```

- **OpenShift Issues**:
  - Verify pod status:
    ```powershell
    oc get pods -l app=sample-dotnet-app
    ```
  - Check route and service:
    ```powershell
    oc describe service sample-dotnet-app
    oc describe route sample-dotnet-app
    ```

## References
- .NET 8.0 SDK: https://dotnet.microsoft.com/download/dotnet/8.0
- Elastic APM .NET Agent: https://www.elastic.co/guide/en/apm/agent/dotnet/current/index.html
- Swashbuckle.AspNetCore: https://github.com/domaindrivendev/Swashbuckle.AspNetCore
- Microsoft.AspNetCore.OpenApi: https://www.nuget.org/packages/Microsoft.AspNetCore.OpenApi
- OpenShift: https://docs.openshift.com/
- .NET SSL/TLS Troubleshooting: https://docs.microsoft.com/en-us/dotnet/core/extensions/httpclient#ssl-and-tls
