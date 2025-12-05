using MaiAmTinhThuong.Data;
using MaiAmTinhThuong.Models;
using MaiAmTinhThuong.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PayOS;
using MatchType = MaiAmTinhThuong.Models.MatchType;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddHttpClient<GeminiService>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(10); // 10s timeout
});


// Add services to the container.
builder.Services.AddControllersWithViews();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(30);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

// Hỗ trợ cả SQL Server và PostgreSQL
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
if (!string.IsNullOrEmpty(connectionString) && 
    (connectionString.Contains("postgres", StringComparison.OrdinalIgnoreCase) || 
     connectionString.Contains("Host=", StringComparison.OrdinalIgnoreCase) ||
     connectionString.Contains("PostgreSQL", StringComparison.OrdinalIgnoreCase)))
{
    // Sử dụng PostgreSQL (cho Railway, Render, etc.)
    // Cần thêm package: dotnet add package Npgsql.EntityFrameworkCore.PostgreSQL
    builder.Services.AddDbContext<ApplicationDbContext>(options =>
        options.UseNpgsql(connectionString));
}
else
{
    // Sử dụng SQL Server (mặc định cho local)
    builder.Services.AddDbContext<ApplicationDbContext>(options =>
        options.UseSqlServer(connectionString ?? "Server=localhost;Database=HoTroNguoiCaoTuoiDB;Trusted_Connection=True;TrustServerCertificate=True;"));
}

builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
{
    options.SignIn.RequireConfirmedAccount = false;
})
.AddEntityFrameworkStores<ApplicationDbContext>()
.AddDefaultTokenProviders();

// Đăng ký PayOSClient
builder.Services.AddSingleton<PayOSClient>(serviceProvider =>
{
    var configuration = serviceProvider.GetRequiredService<IConfiguration>();
    var clientId = configuration["PayOS:ClientId"];
    var apiKey = configuration["PayOS:ApiKey"];
    var checksumKey = configuration["PayOS:ChecksumKey"];

    if (string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(checksumKey))
    {
        // Log warning nhưng không throw exception để app vẫn chạy được
        var logger = serviceProvider.GetRequiredService<ILogger<Program>>();
        logger.LogWarning("PayOS configuration is missing. Payment features will not work. Please set PayOS:ClientId, PayOS:ApiKey, and PayOS:ChecksumKey environment variables.");
        // Trả về PayOSClient với empty strings - sẽ fail khi sử dụng nhưng không crash app
        return new PayOSClient("", "", "");
    }

    return new PayOSClient(clientId, apiKey, checksumKey);
});


var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // Không dùng HSTS trên Railway vì Railway tự xử lý HTTPS
    // app.UseHsts();
}

// Tắt HTTPS redirection trên Railway (Railway tự xử lý HTTPS)
// app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseSession(); // Thêm session middleware

app.UseAuthentication(); // Thêm dòng này để login hoạt động
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

// Tự động chạy migration khi khởi động
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    try
    {
        // Kiểm tra database có thể kết nối được không
        if (db.Database.CanConnect())
        {
            // Chạy migration tự động
            db.Database.Migrate();
            logger.LogInformation("Database migration completed successfully.");
        }
        else
        {
            logger.LogWarning("Cannot connect to database. Please check connection string.");
        }
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "An error occurred while migrating the database. App will continue but database operations may fail.");
        // Không throw exception để app vẫn có thể start
    }
}

// Khởi tạo database và seed data (wrap trong try-catch để không crash app)
using (var scope = app.Services.CreateScope())
{
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    try
    {
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        
        // Chỉ chạy nếu database có thể kết nối
        if (db.Database.CanConnect())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();

            // Tạo role Admin nếu chưa có
            if (!await roleManager.RoleExistsAsync("Admin"))
                await roleManager.CreateAsync(new IdentityRole("Admin"));

            // Tạo user admin
            var adminEmail = "admin@localhost.com";
            var adminUser = await userManager.FindByEmailAsync(adminEmail);
            if (adminUser == null)
            {
                var user = new ApplicationUser
                {
                    UserName = adminEmail,
                    Email = adminEmail,
                    FullName = "Quản trị viên",
                    ProfilePicture = "default1-avatar.png",
                    Role = "Admin",
                    EmailConfirmed = true
                };
                var result = await userManager.CreateAsync(user, "Admin@123");
                if (result.Succeeded)
                {
                    await userManager.AddToRoleAsync(user, "Admin");
                }
            }

            // Seed BotRules
            if (!db.BotRules.Any())
            {
                db.BotRules.AddRange(new[]
                {
                    new BotRule { Trigger = "xin chào", MatchType = MatchType.Contains, Response = "Chào bạn! Mình có thể giúp gì cho bạn?", Priority = 100 },
                    new BotRule { Trigger = "đóng góp", MatchType = MatchType.Contains, Response = "Bạn có thể đóng góp tại /DongGop hoặc liên hệ số (+84) 902115231.", Priority = 90 },
                    new BotRule { Trigger = "giờ làm việc", MatchType = MatchType.Exact, Response = "Mái Ấm mở cửa: 8:00 - 17:00 (T2-T7).", Priority = 80 },
                    new BotRule { Trigger = "lien he", MatchType = MatchType.Regex, Response = "Bạn có thể gọi (+84)902115231 hoặc email MaiAmYeuThuong@gmail.com", Priority = 70 }
                });
                db.SaveChanges();
            }
            
            logger.LogInformation("Database initialization completed successfully.");
        }
        else
        {
            logger.LogWarning("Cannot connect to database. Skipping initialization. Please check connection string.");
        }
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "An error occurred during database initialization. App will continue but some features may not work.");
        // Không throw exception để app vẫn có thể start
    }
}


app.Run();
