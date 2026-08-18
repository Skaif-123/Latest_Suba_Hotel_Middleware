using System.Collections.Generic;
using System.Threading.Tasks;
using Dapper;
using AgentSyncConsole.Helpers;
using AgentSyncConsole.Interfaces;
using AgentSyncConsole.Models;

namespace AgentSyncConsole.Repositories
{
    /// <summary>
    /// Replaces:
    ///   zcql.executeZCQLQuery(`SELECT ROWID, agent FROM ThirdPartyData
    ///     WHERE agent IS NOT NULL ORDER BY ROWID LIMIT ${offset}, ${PAGE_SIZE}`)
    /// (and the equivalent "corporates" projection.)
    ///
    /// Both page queries now also skip rows already marked Processed for that
    /// track (Agent_Status / Corporate_Status), so a row whose agent(s) or
    /// corporate(s) already synced successfully is not re-fetched on the next run.
    /// </summary>
    public class ThirdPartyRepository : IThirdPartyRepository
    {
        private readonly SqlConnectionFactory _connectionFactory;

        public ThirdPartyRepository(SqlConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory;
        }

        public async Task<List<ThirdPartyDataRecord>> GetPageAsync(int offset, int pageSize)
        {
            const string sql = @"
                SELECT ROWID, agent, Status
                FROM ThirdPartyData
                WHERE agent IS NOT NULL
                  AND ISNULL(syncstatus,'') =''
                ORDER BY ROWID";

            //OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
            using var conn = await _connectionFactory.CreateOpenConnectionAsync();
            var rows = await conn.QueryAsync<ThirdPartyDataRecord>(sql, new { Offset = offset, PageSize = pageSize });
            return new List<ThirdPartyDataRecord>(rows);
        }

        public async Task<List<ThirdPartyDataRecord>> GetPageAsyncCorporate(int offset, int pageSize)
        {
            const string sql = @"
                SELECT ROWID, corporates, Status
                FROM ThirdPartyData
                WHERE corporates IS NOT NULL
                  AND ISNULL(syncstatus,'') =''
                ORDER BY ROWID";

            using var conn = await _connectionFactory.CreateOpenConnectionAsync();
            var rows = await conn.QueryAsync<ThirdPartyDataRecord>(sql, new { Offset = offset, PageSize = pageSize });
            return new List<ThirdPartyDataRecord>(rows);
        }

        public async Task UpdateAgentStatusAsync(string rowId, string status)
        {
            const string sql = @"
                UPDATE ThirdPartyData
                SET syncStatus = @Status,
                syncTime=SYSDATETIME()
                WHERE ROWID = @RowId";

            using var conn = await _connectionFactory.CreateOpenConnectionAsync();
            await conn.ExecuteAsync(sql, new { RowId = rowId, Status = status });
        }

        public async Task UpdateCorporateStatusAsync(string rowId, string status)
        {
            const string sql = @"
                UPDATE ThirdPartyData
                SET syncStatus = @Status,
                syncTime=SYSDATETIME()      
                WHERE ROWID = @RowId";

            using var conn = await _connectionFactory.CreateOpenConnectionAsync();
            await conn.ExecuteAsync(sql, new { RowId = rowId, Status = status });
        }
    }
}