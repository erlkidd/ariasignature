using AriaSignature.Api;
using AriaSignature.Application;
using AriaSignature.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddApplication();
builder.Services.AddInfrastructure();
builder.Services.AddAriaApi();
builder.Services.AddHostedService<ApiDatabaseInitializationHostedService>();

var app = builder.Build();
app.UseAriaApi();

app.Run();

public partial class Program
{
}
