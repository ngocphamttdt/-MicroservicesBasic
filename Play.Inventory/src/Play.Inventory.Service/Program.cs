
using Play.Common.MassTransit;
using Play.Common.MongoDB;
using Play.Inventory.Service.Clients;
using Play.Inventory.Service.Entities;
using Polly;
using Polly.Timeout;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddMongo()
.AddMongoRepository<InventoryItem>("inventoryItems")
.AddMongoRepository<CatalogItem>("catalogItems")
.AddMassTransitWithRabbitMq();

// Register services
builder.Services.AddLogging();

builder.Services.AddControllers(options =>
{
    options.SuppressAsyncSuffixInActionNames = false;
});

AddCatalogClient(builder);
// Add services to the container.
//Register zone

// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();
app.MapControllers();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.Run();

static void AddCatalogClient(WebApplicationBuilder builder)
{
    Random jitterer = new Random();

    builder.Services.AddHttpClient<CatalogClient>(client =>
    {
        client.BaseAddress = new Uri("https://localhost:7186");
    })
     .AddTransientHttpErrorPolicy(builder1 => builder1.Or<TimeoutRejectedException>().WaitAndRetryAsync(
                    5,
                    retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt))
                    + TimeSpan.FromMilliseconds(jitterer.Next(0, 1000)),
                    onRetry: (outcome, timespan, retryAttempt) =>
                    {
                        var serviceProvider = builder.Services.BuildServiceProvider();
                        serviceProvider.GetService<ILogger<CatalogClient>>()?
                                .LogWarning($"Delaying for {timespan.TotalSeconds} seconds, then making retry {retryAttempt}");
                    }
                ))
    .AddTransientHttpErrorPolicy(builder1 => builder1.Or<TimeoutRejectedException>().CircuitBreakerAsync(
                    3,
                    TimeSpan.FromSeconds(15),
                    onBreak: (outcome, timespan) =>
                    {
                        var serviceProvider = builder.Services.BuildServiceProvider();
                        serviceProvider.GetService<ILogger<CatalogClient>>()?
                            .LogWarning($"Opening the circuit for {timespan.TotalSeconds} seconds...");
                    },
                    onReset: () =>
                    {
                        var serviceProvider = builder.Services.BuildServiceProvider();
                        serviceProvider.GetService<ILogger<CatalogClient>>()?
                            .LogWarning($"Closing the circuit...");
                    }
                ))
    .AddPolicyHandler(Policy.TimeoutAsync<HttpResponseMessage>(1))
    ;
}