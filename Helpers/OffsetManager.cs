using System.Threading.Tasks;
using Dapper;
using Microsoft.Extensions.Logging;
using AgentSyncConsole.Interfaces;
using AgentSyncConsole.Utilites;

namespace AgentSyncConsole.Helpers
{
    /// <summary>
    /// Replaces the original Catalyst Cache offset persistence
    /// (getStoredOffset/setStoredOffset against segment.get/segment.put) with
    /// a dedicated SyncOffset SQL table (ModuleName, CurrentOffset, UpdatedTime).
    /// Behavior preserved: a failed read defaults to offset 0 (never throws);
    /// a save of 0 means "no more rows" (chain reset), a save of a positive
    /// offset means "resume from here".
    /// </summary>
    public class OffsetManager : IOffsetManager
    {
        private readonly SqlConnectionFactory _connectionFactory;
        private readonly string _moduleName;
        private readonly ILogger<OffsetManager> _logger;

        public OffsetManager(SqlConnectionFactory connectionFactory, ILogger<OffsetManager> logger)
        {
            _connectionFactory = connectionFactory;
            _moduleName = Constants.ModuleName;
            _logger = logger;
        }

        public async Task<int> LoadOffsetAsync()
        {
            const string sql = @"
                SELECT CurrentOffset
                FROM SyncOffset
                WHERE ModuleName = @ModuleName";

            try
            {
                using var conn = await _connectionFactory.CreateOpenConnectionAsync();
                var value = await conn.QueryFirstOrDefaultAsync<int?>(sql, new { ModuleName = _moduleName });
                return value ?? 0;
            }
            catch (System.Exception e)
            {
                _logger.LogInformation("--------------------------------------------------");
                _logger.LogInformation("GET STORED OFFSET FAILED");
                _logger.LogInformation("--------------------------------------------------");
                _logger.LogInformation("Current Offset=0 (defaulting)");
                _logger.LogInformation("Error=" + AppLogger.SafeStringifyError(e));
                return 0;
            }
        }

        public async Task SaveOffsetAsync(int nextOffset, int currentOffsetForLog)
        {
            _logger.LogInformation("--------------------------------------------------");
            _logger.LogInformation("SAVING OFFSET TO SyncOffset TABLE");
            _logger.LogInformation("--------------------------------------------------");
            _logger.LogInformation("Current Offset=" + currentOffsetForLog);
            _logger.LogInformation("Next Offset=" + nextOffset);

            const string sql = @"
                MERGE SyncOffset AS target
                USING (SELECT @ModuleName AS ModuleName) AS source
                ON target.ModuleName = source.ModuleName
                WHEN MATCHED THEN
                    UPDATE SET CurrentOffset = @CurrentOffset, UpdatedTime = GETUTCDATE()
                WHEN NOT MATCHED THEN
                    INSERT (ModuleName, CurrentOffset, UpdatedTime)
                    VALUES (@ModuleName, @CurrentOffset, GETUTCDATE());";

            try
            {
                using var conn = await _connectionFactory.CreateOpenConnectionAsync();
                await conn.ExecuteAsync(sql, new { ModuleName = _moduleName, CurrentOffset = nextOffset });
            }
            catch (System.Exception e)
            {
                _logger.LogInformation("--------------------------------------------------");
                _logger.LogInformation("FAILED TO SAVE OFFSET");
                _logger.LogInformation("--------------------------------------------------");
                _logger.LogInformation("Current Offset=" + currentOffsetForLog);
                _logger.LogInformation("Next Offset=" + nextOffset);
                _logger.LogInformation("Error=" + AppLogger.SafeStringifyError(e));
            }
        }
    }
}
