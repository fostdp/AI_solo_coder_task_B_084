
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using TextileMonitoring.API.Data;
using TextileMonitoring.API.Services;
using TextileMonitoring.API.SqlServer;
using TextileMonitoring.API.ZigBee;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "古代织绣品虫蛀与霉变协同监测系统 API",
        Version = "v1",
        Description = "织绣品保护实验室 - 虫蛀孔洞与霉菌协同监测平台"
    });
});

builder.Services.AddDbContext<ApplicationDbContext>(options =>
{
    var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
    options.UseSqlServer(connectionString, sqlOptions =>
    {
        sqlOptions.CommandTimeout(120);
        sqlOptions.EnableRetryOnFailure(
            maxRetryCount: 5,
            maxRetryDelay: TimeSpan.FromSeconds(10),
            errorNumbersToAdd: null);
    });
});

builder.Services.AddHttpClient();

builder.Services.AddScoped<IPredictionService, PredictionService>();
builder.Services.AddScoped<IAlertNotificationService, AlertNotificationService>();
builder.Services.AddScoped<IAlertService, AlertService>();
builder.Services.AddScoped<ISensorDataService, SensorDataService>();
builder.Services.AddScoped<ISqlServerBatchWriter, SqlServerBatchWriter>();

builder.Services.AddHostedService<ZigBeeUdpListener>();

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "织绣品监测 API V1");
    });
}

app.UseCors("AllowAll");

app.UseDefaultFiles();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthorization();

app.MapControllers();

app.MapFallbackToFile("index.html");

app.Logger.LogInformation("古代织绣品虫蛀与霉变协同监测系统启动成功！");
app.Logger.LogInformation("监听地址: http://localhost:5000");
app.Logger.LogInformation("Swagger文档: http://localhost:5000/swagger");
app.Logger.LogInformation("ZigBee监听器: 端口8684 (BackgroundService托管)");
app.Logger.LogInformation("ODE求解器: MathNet.Numerics + RK45自适应步长 (ode/solver.cs)");

app.Run("http://0.0.0.0:5000");
