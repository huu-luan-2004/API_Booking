using HotelBookingApi.Data;
using HotelBookingApi.Services;
using FirebaseAdmin;
using Google.Apis.Auth.OAuth2;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.AspNetCore.StaticFiles;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// Config
var config = builder.Configuration;

// Firebase Admin init
var saPath = config["FIREBASE_SERVICE_ACCOUNT_PATH"];
if (!string.IsNullOrWhiteSpace(saPath))
{
    FirebaseApp.Create(new AppOptions { Credential = GoogleCredential.FromFile(saPath) });
}

// Add services to the container.
builder.Services.AddControllers();

// Add database connection
builder.Services.AddSingleton<SqlConnectionFactory>();
builder.Services.AddScoped<NguoiDungRepository>();
builder.Services.AddScoped<CoSoLuuTruRepository>();
builder.Services.AddScoped<PhongRepository>();
builder.Services.AddScoped<DatPhongRepository>();
builder.Services.AddScoped<ThanhToanRepository>();
builder.Services.AddScoped<HuyDatPhongRepository>();
builder.Services.AddScoped<KhuyenMaiRepository>();

// Add Firebase and JWT services
builder.Services.AddSingleton<FirebaseService>();
builder.Services.AddSingleton<JwtService>();
builder.Services.AddSingleton<FirebaseStorageService>();
builder.Services.AddHttpClient<OpenStreetMapService>();
builder.Services.AddSingleton<VnPayService>();

// JWT authentication
var jwtSecret = config["JWT_SECRET"] ?? "your_jwt_secret_key";
var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret));

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
}).AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = false,
        ValidateAudience = false,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = key
    };
});

builder.Services.AddAuthorization();

// Add CORS
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

// Configure the HTTP request pipeline.
app.UseCors("AllowAll");
// Serve static files in wwwroot (e.g., /uploads/avatars/...) and add CORS headers so Flutter Web can load images
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        var headers = ctx.Context.Response.Headers;
        if (!headers.ContainsKey("Access-Control-Allow-Origin"))
        {
            headers.Append("Access-Control-Allow-Origin", "*");
        }
    }
});
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.MapGet("/", () => "API is working with auth!");

app.Run("http://0.0.0.0:5000");