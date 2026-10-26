using Microsoft.Extensions.Options;
using SemanticImageSearch.Core.Interfaces;
using SemanticImageSearch.Core.Options;
using SemanticImageSearch.Core.Services;

const string WebClientCorsPolicy = "WebClient";

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers(options => options.Filters.Add<ServiceUnavailableExceptionFilter>());
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

builder.Services.Configure<QdrantOptions>(builder.Configuration.GetSection(QdrantOptions.SectionName));
builder.Services.Configure<OllamaOptions>(builder.Configuration.GetSection(OllamaOptions.SectionName));

builder.Services.AddSingleton<IVectorDatabaseService>(sp =>
    new QdrantVectorService(sp.GetRequiredService<IOptions<QdrantOptions>>().Value));
builder.Services.AddHttpClient<ILLaVAEmbeddingService, OllamaAIService>((client, sp) =>
    new OllamaAIService(client, sp.GetRequiredService<IOptions<OllamaOptions>>().Value));
builder.Services.AddScoped<IImageSearchService, ImageSearchService>();

var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options =>
{
    options.AddPolicy(WebClientCorsPolicy, policy =>
        policy.WithOrigins(allowedOrigins)
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
