using AI.Caller.Core;
using AI.Caller.Core.Extensions;
using AI.Caller.Core.Network;
using AI.Caller.Phone.BackgroundTask;
using AI.Caller.Phone.Entities;
using AI.Caller.Phone.Filters;
using AI.Caller.Phone.Hubs;
using AI.Caller.Phone.Models;
using AI.Caller.Phone.Services;
using FFmpeg.AutoGen;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using NLog.Extensions.Logging;

namespace AI.Caller.Phone {
    public class Program {
        public static void Main(string[] args) {
            var builder = WebApplication.CreateBuilder(args);

            builder.Services.AddLogging(options => {
                options.AddNLog();
            });

            builder.Services.AddControllersWithViews(options => {
                options.Filters.Add<SipAuthencationFilter>();
            }).AddNewtonsoftJson(options => {

            });

            ffmpeg.RootPath = builder.Configuration.GetValue<string>("FFmpegDir");

            builder.Services.AddDistributedMemoryCache();
            builder.Services.AddSession(options => {
                options.IdleTimeout = TimeSpan.FromMinutes(30);
                options.Cookie.HttpOnly = true;
                options.Cookie.IsEssential = true;
            });

            builder.Services.AddSignalR();

            builder.Services.Configure<WebRTCSettings>(builder.Configuration.GetSection("WebRTCSettings"));
            builder.Services.Configure<TTSSettings>(builder.Configuration.GetSection("TTSSettings"));
            builder.Services.Configure<AICustomerServiceSettings>(builder.Configuration.GetSection("AICustomerServiceSettings"));

            builder.Services.AddSingleton<ApplicationContext>(_ => {
                var ctx = new ApplicationContext();
                return ctx;
            });

            builder.Services.AddDbContext<AppDbContext>(options => options.UseSqlite("Data Source=app.db"));
            builder.Services.AddHostedService<SipBackgroundTask>();
            builder.Services.AddHostedService<AISipRegistrationService>();
            builder.Services.AddSingleton(sp => {
                return new SIPTransportManager(builder.Configuration.GetSection("SipSettings")["ContactHost"], sp.GetRequiredService<ILogger<SIPTransportManager>>());
            });
            builder.Services.AddSingleton<INetworkMonitoringService, NetworkMonitoringService>();
            builder.Services.AddSingleton<SIPClientPoolManager>();
            builder.Services.AddSingleton<ICallManager, CallManager>();
            builder.Services.AddSingleton<RecordingManager>();
            builder.Services.AddTransient<WebToWebScenario>();
            builder.Services.AddTransient<WebToMobileScenario>();
            builder.Services.AddTransient<MobileToWebScenario>();
            builder.Services.AddTransient<ServerToWebScenario>();
            builder.Services.AddTransient<WebToServerScenario>();
            builder.Services.AddTransient<ServerToMobileScenario>();
            builder.Services.AddTransient<MobileToServerScenario>();
            builder.Services.AddScoped<UserService>();
            builder.Services.AddScoped<ContactService>();
            builder.Services.AddScoped<SipService>();
            builder.Services.AddScoped<DataMigrationService>();
            builder.Services.AddSingleton<ISimpleRecordingService, AudioStreamRecordingService>();
            builder.Services.AddSingleton<HangupMonitoringService>();

            builder.Services.AddScoped<IFileStorageService, FileStorageService>();
            builder.Services.AddScoped<ITtsTemplateIntegrationService, TtsTemplateIntegrationService>();

            builder.Services.AddScoped<ICallRoutingService, CallRouting.Services.CallRoutingService>();

            builder.Services.AddScoped<ICallFlowOrchestrator, CallFlowOrchestrator>();
            builder.Services.AddScoped<IVariableResolverService, VariableResolverService>();
            builder.Services.AddScoped<ITtsPlayerService, TtsPlayerService>();

            builder.Services.AddAIAutoResponder();
            builder.Services.AddMediaProcessing();
            builder.Services.AddSingleton<AICustomerServiceManager>();
            builder.Services.AddAuthentication(options => {
                options.DefaultScheme = "CookieAuth";
                options.DefaultChallengeScheme = "CookieAuth";
            })
            .AddCookie("CookieAuth", options => {
                options.LoginPath = "/Account/Login";
            });

            builder.Services.AddAuthorization(options => {
                options.DefaultPolicy = new AuthorizationPolicyBuilder("CookieAuth")
                    .RequireAuthenticatedUser()
                    .Build();
            });

            var app = builder.Build();

            if (!app.Environment.IsDevelopment()) {
                app.UseExceptionHandler("/Error");
            }
            app.UseStaticFiles();
            app.UseRouting();
            app.UseSession();
            app.UseAuthentication();
            app.UseAuthorization();

            app.MapDefaultControllerRoute();
            app.MapHub<WebRtcHub>("/webrtc");

            SIPSorcery.LogFactory.Set(app.Services.GetService<ILoggerFactory>());

            using (var scope = app.Services.CreateScope()) {
                var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
#if DEBUG
                dbContext.Database.Migrate();
#endif
                EnsureDefaultUser(builder.Configuration, dbContext);

                var migrationService = scope.ServiceProvider.GetRequiredService<DataMigrationService>();
                try {
                    migrationService.MigrateUserSipDataToSipAccountAsync().Wait();
                    var isValid = migrationService.ValidateMigrationAsync().Result;
                    if (isValid) {
                        app.Logger.LogInformation("数据迁移完成并验证通过");
                    } else {
                        app.Logger.LogWarning("数据迁移验证失败");
                    }
                } catch (Exception ex) {
                    app.Logger.LogError(ex, "数据迁移失败");
                }
            }

            app.Run();
        }

        private static void EnsureDefaultUser(IConfiguration configuration, AppDbContext dbContext) {
            var defaultUserSection = configuration.GetSection("UserSettings:DefaultUser");
            var adminUser = dbContext.Users.FirstOrDefault(u => u.Username == "admin");
            if (adminUser == null) {
                dbContext.Users.Add(new User {
                    Username = defaultUserSection["Username"],
                    Password = defaultUserSection["Password"],
                    IsAdmin = true
                });

                dbContext.SaveChanges();
            } else if (!adminUser.IsAdmin) {
                adminUser.IsAdmin = true;
                dbContext.SaveChanges();
            }
        }
    }
}