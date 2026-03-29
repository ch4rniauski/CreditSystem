using CreditSystem.Database;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();

builder.Services.AddDbContext<CreditSystemContext>(opt =>
    opt.UseNpgsql(builder.Configuration.GetConnectionString("CreditSystem")));

var app = builder.Build();

app.MapControllers();

await app.RunAsync();
