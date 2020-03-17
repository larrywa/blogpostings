using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using SFCoreAppOne.Models;

namespace SFCoreAppOne.Controllers
{
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;

        public HomeController(ILoggerFactory logger)
        {
            _logger = logger.CreateLogger<HomeController>();
        }

        public IActionResult Index()
        {
            _logger.LogInformation("Info:Index called");
            _logger.LogWarning("Warn:Index called");
            _logger.LogError("Error:Index example log");

            return View();
        }

        public IActionResult Privacy()
        {
            _logger.LogInformation("Info:Privacy page called");
            _logger.LogWarning("Warn:Privacy called");
            _logger.LogError("Error:Privacy example log");

            return View();
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}
