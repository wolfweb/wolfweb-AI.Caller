using AI.Caller.Core;
using AI.Caller.Phone.BackgroundTask;
using AI.Caller.Phone.Entities;
using AI.Caller.Phone.Filters;
using AI.Caller.Phone.Hubs;
using AI.Caller.Phone.Models;
using AI.Caller.Phone.Services;
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

            // Add services to the container.
            builder.Services.AddControllersWithViews(options => {
                options.Filters.Add<SipAuthencationFilter>();
            }).AddNewtonsoftJson(options => {

            });

            // Add session services
            builder.Services.AddDistributedMemoryCache();
            builder.Services.AddSession(options => {
                options.IdleTimeout = TimeSpan.FromMinutes(30);
                options.Cookie.HttpOnly = true;
                options.Cookie.IsEssential = true;
            });

            builder.Services.AddSignalR();

            builder.Services.AddSingleton<ApplicationContext>(x => {
                var ctx = new ApplicationContext();
                ctx.SipServer = builder.Configuration.GetSection("SipSettings")["SipServer"] ?? "192.168.8.113";
                return ctx;
            });

            builder.Services.AddDbContext<AppDbContext>(options => options.UseSqlite("Data Source=app.db"));
            builder.Services.AddHostedService<SipBackgroundTask>();
            builder.Services.AddSingleton<SIPTransportManager>();
            builder.Services.AddScoped<UserService>();
            builder.Services.AddScoped<ContactService>();
            builder.Services.AddScoped<SipService>();
            builder.Services.AddSingleton<HangupMonitoringService>();
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
            }

            app.Run();
        }

        private static void EnsureDefaultUser(IConfiguration configuration, AppDbContext dbContext) {
            var defaultUserSection = configuration.GetSection("UserSettings:DefaultUser");
            var adminUser = dbContext.Users.FirstOrDefault(u => u.Username == "admin");
            if (adminUser == null) {
                dbContext.Users.Add(new User {
                    Username = defaultUserSection["Username"],
                    Password = defaultUserSection["Password"]
                });

                dbContext.SaveChanges();
            }
        }
    }
}