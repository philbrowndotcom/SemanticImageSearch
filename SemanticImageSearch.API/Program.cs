using Qdrant.Client;

const string WebClientCorsPolicy = "WebClient";

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

builder.Services.AddSingleton(_ => new QdrantClient("localhost", 6334));
builder.Services.AddHttpClient(SearchController.OllamaClientName, client =>
{
    client.BaseAddress = new Uri("http://localhost:11434");
});

builder.Services.AddCors(options =>
{
    options.AddPolicy(WebClientCorsPolicy, policy =>
        policy.WithOrigins("http://localhost:54879", "https://localhost:54879")
              .AllowAnyHeader()
              .AllowAnyMethod());
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseCors(WebClientCorsPolicy);

app.UseAuthorization();

app.MapControllers();

app.Run();
