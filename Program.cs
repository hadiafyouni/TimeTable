using Scalar.AspNetCore;
using Schedule;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi();

string connectionString =
    "Host=localhost;Database=schoolSchedule;Username=postgres;Password=postgre2005;";

builder.Services.AddScoped<CurriculumRepository>(_ =>
    new CurriculumRepository(connectionString));

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference(options =>
    {
        options.WithTitle("School Scheduler API");
        options.WithTheme(ScalarTheme.DeepSpace);
        options.WithDefaultHttpClient(ScalarTarget.CSharp, ScalarClient.HttpClient);
    });
}

app.UseHttpsRedirection();
app.MapControllers();
app.Run();