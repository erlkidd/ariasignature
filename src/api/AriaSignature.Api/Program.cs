using AriaSignature.Api;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddAriaApi();

var app = builder.Build();
app.UseAriaApi();

app.Run();
