using CreditSystem.Database;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddCors(opt =>
    opt.AddDefaultPolicy(policy =>
    {
        policy
            .WithOrigins("http://localhost:4200")
            .AllowAnyHeader()
            .AllowAnyMethod();
    }));

builder.Services.AddDbContext<CreditSystemContext>(opt =>
    opt.UseNpgsql(builder.Configuration.GetConnectionString("CreditSystem")));

var app = builder.Build();

app.UseCors();

app.MapControllers();

await app.RunAsync();
