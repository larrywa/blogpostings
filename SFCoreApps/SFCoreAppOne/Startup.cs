using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace SFCoreAppOne
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddControllersWithViews();
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, 
            IWebHostEnvironment env,
            ILoggerFactory loggerFactory)
        {
            string fName1 = "Logs/Info-" + System.Environment.MachineName + "-";
            string fName2 = "ErrorLogs/Error-" + System.Environment.MachineName + "-";
            string fName3 = "WarnLogs/Warn-" + System.Environment.MachineName + "-";
            
            loggerFactory.AddFile(fName1 + "{Date}.txt",LogLevel.Information);
            loggerFactory.AddFile(fName2 + "{Date}.txt", LogLevel.Error);
            loggerFactory.AddFile(fName3 + "{Date}.txt", LogLevel.Warning);
            

            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }
            else
            {
                app.UseExceptionHandler("/Home/Error");
            }
            app.UseStaticFiles();

            app.UseRouting();

            app.UseAuthorization();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllerRoute(
                    name: "default",
                    pattern: "{controller=Home}/{action=Index}/{id?}");
            });
        }
    }
}
