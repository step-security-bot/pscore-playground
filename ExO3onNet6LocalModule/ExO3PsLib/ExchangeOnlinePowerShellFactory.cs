﻿using Microsoft.Extensions.Logging;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Security.Cryptography.X509Certificates;
using System.Threading;

namespace ExO3PsLib
{
    public class ExchangeOnlinePowerShellFactory : IExchangeOnlinePowerShellFactory
    {
        private readonly ILogger<ExchangeOnlinePowerShellFactory> _logger;

        public ExchangeOnlinePowerShellFactory(ILogger<ExchangeOnlinePowerShellFactory> logger)
        {
            _logger = logger;
        }

        private InitialSessionState CreateInitialSessionState(string modulePath)
        {
            InitialSessionState iss = InitialSessionState.CreateDefault();
            iss.ExecutionPolicy = Microsoft.PowerShell.ExecutionPolicy.Unrestricted; // otherwise: ".. cannot be loaded because running scripts is disabled on this system"
            iss.ThrowOnRunspaceOpenError = true;
            iss.ImportPSModule(new string[] { modulePath }); // would be "ExchangeOnlineManagement" for installed PS module

            return iss;
        }

        public PowerShell Connect(string appId, string organization, X509Certificate2 certificate, string modulePath)
        {
            var iss = CreateInitialSessionState(modulePath);

            var exchangeRunspace = RunspaceFactory.CreateRunspace(iss);
            exchangeRunspace.Open();

            using (var pipeLine = exchangeRunspace.CreatePipeline())
            {
                var connectCmd = new Command("Connect-ExchangeOnline");
                connectCmd.Parameters.Add("AppId", appId);
                connectCmd.Parameters.Add("Organization", organization);
                connectCmd.Parameters.Add("Certificate", certificate);

                connectCmd.Parameters.Add("ShowBanner", false);
                connectCmd.Parameters.Add("SkipLoadingFormatData", true);
                connectCmd.Parameters.Add("ShowProgress", false);
                connectCmd.Parameters.Add("UseMultithreading", true);       // default anyways

                connectCmd.Parameters.Add("EnableErrorReporting", false);
                // connectCmd.Parameters.Add("LogDirectoryPath", "D:\\demos\\EXOTelemetry");
                // connectCmd.Parameters.Add("LogLevel", "All");

                pipeLine.Commands.Add(connectCmd);

                try
                {
                    var resultCollection = pipeLine.Invoke();
                    if (resultCollection.Count > 0)
                    {
                        _logger.LogInformation("Got result collection when I should not");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Connecting to Exchange Online failed");
                    throw;
                }

                if (pipeLine.Error != null && pipeLine.Error.Count > 0)
                {
                    throw new NotImplementedException(); // Error handing code below is not tested
#pragma warning disable CS0162 // Unreachable code detected
                    var errors = pipeLine.Error.ReadToEnd();
                    var errStringified = "!Errors! " + String.Join(" :: ", errors.Select(error => error.ToString()).ToList());
                    // TODO: Write to _logger
#pragma warning restore CS0162 // Unreachable code detected
                }
            }

            var ps = PowerShell.Create();
            ps.Runspace = exchangeRunspace;

            return ps;
        }

        public PowerShell ConnectViaPool(string appId, string organization, X509Certificate2 certificate, string modulePath)
        {
            string rootFolder = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);

            var iss = CreateInitialSessionState(modulePath);

            iss.Variables.Add(new SessionStateVariableEntry("exoAppId", appId, "no description"));
            iss.Variables.Add(new SessionStateVariableEntry("exoOrganization", organization, "no description"));
            iss.Variables.Add(new SessionStateVariableEntry("exoCertificate", certificate, "no description"));
            bool result = iss.StartupScripts.Add(System.IO.Path.Combine(rootFolder, "ConnectScript.ps1"));

            RunspacePool pool = RunspaceFactory.CreateRunspacePool(iss);

            pool.SetMinRunspaces(1);
            pool.SetMaxRunspaces(3);
            pool.Open(); // FAILS: System.Management.Automation.PSInvalidOperationException HResult = 0x80131509 Message = Running startup script threw an error: Cannot add type.Compilation errors occurred..

            var ps = PowerShell.Create();
            ps.RunspacePool = pool;

            return ps;
        }
    }
}
