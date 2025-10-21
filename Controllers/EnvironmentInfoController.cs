using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace StimulsoftReport.Controllers
{
    [ApiController]
    [Route("api/env-info")]
    public class EnvironmentInfoController : ControllerBase
    {
        private readonly IHostEnvironment _env;
        private readonly IConfiguration _config;

        public EnvironmentInfoController(IHostEnvironment env, IConfiguration config)
        {
            _env = env;
            _config = config;
        }

        [HttpGet]
        public IActionResult Get()
        {
            var envVars = Environment.GetEnvironmentVariables();
            var filteredEnvVars = new Dictionary<string, string>();

            var keysToInclude = new[]
            {
                "ASPNETCORE_ENVIRONMENT",
                "DOTNET_RUNNING_IN_CONTAINER",
                "PATH",
                "COMPUTERNAME",
                "USERNAME",
                "TEMP",
                "TMP"
            };

            foreach (var key in keysToInclude)
            {
                if (envVars.Contains(key))
                    filteredEnvVars[key] = envVars[key]?.ToString() ?? "";
            }

            var filteredConfig = _config.AsEnumerable()
                .Where(kv => kv.Key.StartsWith("ReportSettings:", StringComparison.OrdinalIgnoreCase)
                          || kv.Key.StartsWith("Logging:", StringComparison.OrdinalIgnoreCase)
                          || kv.Key.StartsWith("ConnectionStrings:", StringComparison.OrdinalIgnoreCase))
                .ToDictionary(kv => kv.Key, kv => kv.Value);

            var process = Process.GetCurrentProcess();
            double cpuUsagePercent = GetCpuUsagePercent(process);
            long memoryUsageMB = process.WorkingSet64 / (1024 * 1024);

            var drive = new System.IO.DriveInfo(System.IO.Path.GetPathRoot(_env.ContentRootPath) ?? "C:\\");
            long diskFreeSpaceMB = drive.AvailableFreeSpace / (1024 * 1024);

            var ipAddresses = NetworkInterface.GetAllNetworkInterfaces()
                .Where(ni => ni.OperationalStatus == OperationalStatus.Up)
                .SelectMany(ni => ni.GetIPProperties().UnicastAddresses)
                .Where(ip => ip.Address.AddressFamily == AddressFamily.InterNetwork)
                .Select(ip => ip.Address.ToString())
                .Distinct()
                .ToList();

            var listeningPorts = new List<int>();
            try
            {
                var ipGlobalProperties = IPGlobalProperties.GetIPGlobalProperties();
                var tcpListeners = ipGlobalProperties.GetActiveTcpListeners();
                listeningPorts = tcpListeners.Select(ep => ep.Port).ToList();
            }
            catch
            {
                // Ignorar errores
            }

            bool stimulsoftLicenseLoaded = System.IO.File.Exists(System.IO.Path.Combine(_env.ContentRootPath, "license.key"));

            var response = new
            {
                ContentRootPath = _env.ContentRootPath,
                EnvironmentName = _env.EnvironmentName,
                UserName = Environment.UserName,
                OSDescription = RuntimeInformation.OSDescription,
                FrameworkDescription = RuntimeInformation.FrameworkDescription,
                EnvironmentVariables = filteredEnvVars,
                Configuration = filteredConfig,
                SystemResources = new
                {
                    CpuUsagePercent = cpuUsagePercent,
                    MemoryUsageMB = memoryUsageMB,
                    DiskFreeSpaceMB = diskFreeSpaceMB
                },
                Licenses = new
                {
                    StimulsoftLicenseLoaded = stimulsoftLicenseLoaded
                },
                Network = new
                {
                    IpAddresses = ipAddresses,
                    ListeningPorts = listeningPorts
                }
            };

            return Ok(response);
        }

        private double GetCpuUsagePercent(Process process)
        {
            try
            {
                var startTime = DateTime.UtcNow;
                var startCpuUsage = process.TotalProcessorTime;
                System.Threading.Thread.Sleep(100);
                var endTime = DateTime.UtcNow;
                var endCpuUsage = process.TotalProcessorTime;

                var cpuUsedMs = (endCpuUsage - startCpuUsage).TotalMilliseconds;
                var totalMsPassed = (endTime - startTime).TotalMilliseconds;
                var cpuUsageTotal = cpuUsedMs / (Environment.ProcessorCount * totalMsPassed);

                return Math.Round(cpuUsageTotal * 100, 2);
            }
            catch
            {
                return -1;
            }
        }
    }
}