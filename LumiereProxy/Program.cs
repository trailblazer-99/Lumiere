using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var host = new HostBuilder()
    .ConfigureFunctionsWebApplication()
    .ConfigureServices(services =>
    {
        services.AddHttpClient(Microsoft.Extensions.Options.Options.DefaultName, client => 
        {
            client.Timeout = TimeSpan.FromSeconds(15);
        });
    })
    .Build();

host.Run();
