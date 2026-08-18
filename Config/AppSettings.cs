using System;
using System.Configuration;

namespace AgentSyncConsole.Config
{
    /// <summary>
    /// Loads configuration from App.config. Replaces the implicit Catalyst
    /// environment/context configuration (catalyst.initialize(context)).
    /// </summary>
    public class AppSettings
    {
        public string ConnectionString { get; set; }

        public static AppSettings Load()
        {
            var connStr = ConfigurationManager.ConnectionStrings["AgentSyncDb"]?.ConnectionString;
            //Console.WriteLine("cconnst",connStr);
            if (string.IsNullOrWhiteSpace(connStr))
            {
                throw new ConfigurationErrorsException(
                    "Connection string 'AgentSyncDb' is missing or empty in App.config.");
            }

            return new AppSettings
            {
                ConnectionString = connStr
            };
        }
    }
}
