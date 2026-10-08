// Vai trò file: Điểm vào ứng dụng, tương tự main + cấu hình Spring Boot: đăng ký DI, middleware, endpoint và các worker nền.
using HeroChess.Api.Auth;
using HeroChess.Api.Data;
using HeroChess.Api.Data.Bootstrap;
using HeroChess.Api.Infrastructure;
using HeroChess.Api.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi;

// Top-level statements: compiler sinh Main; builder gom cấu hình, logging và DI trước khi tạo app.
var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
// Swagger Development có Bearer scheme để test protected API trực tiếp sau khi login.
builder.Services.AddSwaggerGen(options =>
{
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "Bearer",
        Description = "Dán accessToken nhận từ POST /api/v1/auth/login."
    });
    options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
    {
        [new OpenApiSecuritySchemeReference("Bearer", document, null)] = []
    });
});
builder.Services.AddAuthorization(AccountPolicies.Configure);
builder.Services.AddScoped<Microsoft.AspNetCore.Authorization.IAuthorizationHandler, AccountAuthorizationHandler>();
builder.Services.AddHttpContextAccessor();

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
// Scoped DbContext: mỗi request/scope có unit of work riêng; không chia sẻ context giữa thread.
builder.Services.AddDbContext<AppDbContext>(options => options.UseNpgsql(connectionString));
builder.Services.Configure<DatabaseBootstrapOptions>(builder.Configuration.GetSection("DatabaseBootstrap"));
builder.Services.Configure<OnboardingOptions>(builder.Configuration.GetSection("Onboarding"));
builder.Services.AddHostedService<DatabaseBootstrapHostedService>();

// Identity cung cấp register/login/refresh và bearer token; UserStore là adapter lưu trữ tùy chỉnh.
builder.Services.AddIdentityApiEndpoints<User>(options =>
{
    options.User.RequireUniqueEmail = true;
    options.SignIn.RequireConfirmedEmail = false;
    options.Password.RequiredLength = 8;
    options.Password.RequireNonAlphanumeric = false;
}).AddDefaultTokenProviders();
builder.Services.AddScoped<IUserStore<User>, UserStore>();
builder.Services.AddScoped<IUserClaimsPrincipalFactory<User>, UserClaimsPrincipalFactory>();
builder.Services.AddScoped<ICurrentUser, CurrentUser>();
builder.Services.AddScoped<LineupValidator>();
builder.Services.AddScoped<LineupService>();
builder.Services.Configure<MatchmakingOptions>(builder.Configuration.GetSection("Matchmaking"));
builder.Services.Configure<MatchRuntimeOptions>(builder.Configuration.GetSection("MatchRuntime"));
// Singleton sống cùng process. Các worker dùng IServiceScopeFactory để lấy service scoped cho từng công việc.
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<MatchLockRegistry>();
builder.Services.AddSingleton<CatalogVersionService>();
builder.Services.AddSingleton<MatchmakingService>();
builder.Services.AddSingleton<WsTicketService>();
builder.Services.AddSingleton<MatchConnectionHub>();
builder.Services.AddSingleton<BotTurnScheduler>();
builder.Services.AddSingleton<RewardPolicy>();
builder.Services.AddSingleton<RatingPolicy>();
builder.Services.AddScoped<MatchSelectionService>();
builder.Services.AddScoped<MatchSelectionLifetime>();
builder.Services.AddScoped<MatchReadService>();
builder.Services.AddSingleton<HeroChess.Rules.Skills.CommandSkillRegistry>(sp =>
    new HeroChess.Rules.Skills.CommandSkillRegistry(new HeroChess.Rules.Skills.ICommandSkillHandler[]
    {
        new HeroChess.Rules.Skills.VanCocTranGiangHandler(),
        new HeroChess.Rules.Skills.PhanKyDoatTheHandler(),
        new HeroChess.Rules.Skills.PhaTranDoatPhongHandler(),
        new HeroChess.Rules.Skills.BinhLamThuyHienHandler(),
        // Step 6: Thành and Rào Command Skills
        new HeroChess.Rules.Skills.ThanhHandler(),
        new HeroChess.Rules.Skills.RaoHandler(),
        // Step 6: Trần Hưng Đạo — Tượng Hero Skill
        new HeroChess.Rules.Skills.ThDTuongCocHandler(),
        // NOTE: Stolen effect cancellation is NOT a Command Skill.
        // It is an internal Phản Kỳ Đoạt Thế resolution mechanism handled by TurnLifecycle.
        // See: TurnLifecycle.Apply(..., resolveCreatorCancellation: true)
    }));
builder.Services.AddScoped<HeroChess.Rules.Skills.CommandSkillDispatcher>();
builder.Services.AddScoped<MatchCommandService>();
builder.Services.AddScoped<SettlementService>();
builder.Services.AddScoped<MatchHistoryService>();
builder.Services.AddScoped<ShopService>();
builder.Services.AddScoped<AdminService>();
// Worker startup phục hồi trận; các BackgroundService sau đó theo dõi bot, timeout và việc cần retry.
builder.Services.AddHostedService<MatchRecoveryHostedService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<BotTurnScheduler>());
builder.Services.AddHostedService<MatchTimeoutHostedService>();
builder.Services.AddSingleton<MatchMaintenanceHostedService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<MatchMaintenanceHostedService>());

// Data Protection key lưu ra đĩa để token không mất khả năng đọc chỉ vì process restart.
var keyDirectory = Path.Combine(builder.Environment.ContentRootPath, "keys");
Directory.CreateDirectory(keyDirectory);
builder.Services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(keyDirectory)).SetApplicationName("HeroChess.Api");

var app = builder.Build();
using (var validationScope = app.Services.CreateScope())
    _ = validationScope.ServiceProvider.GetRequiredService<AppDbContext>().Model;

// Thứ tự middleware có ý nghĩa: bắt lỗi bao ngoài, xác thực trước phân quyền; WS là endpoint riêng.
app.UseMiddleware<ApiExceptionMiddleware>();
if (app.Environment.IsDevelopment()) { app.UseSwagger(); app.UseSwaggerUI(); }
else app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(20) });
app.UseDefaultFiles();
app.UseStaticFiles();

// Liveness chỉ báo process phục vụ được; readiness phía dưới còn thử kết nối database.
app.MapGet("/health/live", () => Results.Ok(new { status = "live" }));
app.MapGet("/health/ready", async (AppDbContext db, CancellationToken cancellationToken) =>
{
    try
    {
        return await db.Database.CanConnectAsync(cancellationToken)
            ? Results.Ok(new { status = "ready", database = "connected" })
            : Results.Json(new { status = "not_ready", database = "unavailable" }, statusCode: 503);
    }
    catch
    {
        return Results.Json(new { status = "not_ready", database = "unavailable" }, statusCode: 503);
    }
});
// Gắn endpoint Identity, controller và socket vào routing; app.Run bắt đầu phục vụ.
app.MapGroup("/api/v1/auth").MapIdentityApi<User>();
app.MapControllers();
app.Map("/ws/v1", WebSocketEndpoint.HandleAsync);
app.Run();

// Expose Program cho WebApplicationFactory trong test; partial cho phép ghép với class Main do compiler sinh.
public partial class Program;
