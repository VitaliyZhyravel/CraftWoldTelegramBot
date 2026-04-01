using Microsoft.EntityFrameworkCore;
using WebApplication1.Contracts;
using WebApplication1.Data;
using WebApplication1.Endpoints;
using WebApplication1.Options;
using WebApplication1.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddHttpClient();
builder.Services.Configure<RoninPoolOptions>(builder.Configuration.GetSection(RoninPoolOptions.SectionName));
builder.Services.Configure<TelegramBotOptions>(builder.Configuration.GetSection(TelegramBotOptions.SectionName));
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")));
builder.Services.AddSingleton<IRoninPoolPriceService, RoninPoolPriceService>();
builder.Services.AddSingleton<IAlertRuntimeCache, AlertRuntimeCache>();
builder.Services.AddScoped<ITelegramUpdateHandler, TelegramBotWorkflowService>();
builder.Services.AddHttpClient<ITelegramMessageClient, TelegramMessageClient>();
builder.Services.AddHttpClient<TelegramWebhookRegistrationService>();
builder.Services.AddScoped<ApplicationDbInitializer>();
builder.Services.AddHostedService<AlertRuntimeCacheWarmupService>();
builder.Services.AddHostedService<AlertMonitoringBackgroundService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<TelegramWebhookRegistrationService>());

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var dbInitializer = scope.ServiceProvider.GetRequiredService<ApplicationDbInitializer>();
    await dbInitializer.InitializeAsync();
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.MapTelegramWebhook();

app.MapGet(
        "/api/pool-price",
        async (string poolAddress, IRoninPoolPriceService priceService, CancellationToken cancellationToken) =>
        {
            var result = await priceService.GetPoolPriceAsync(poolAddress, cancellationToken);
            return Results.Ok(result);
        })
    .WithName("GetRoninPoolPrice");
app.MapGet("/check", () => "Service is running");

app.Run();
