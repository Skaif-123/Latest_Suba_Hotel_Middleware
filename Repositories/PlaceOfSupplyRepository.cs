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
    ///   zcql.executeZCQLQuery(`SELECT Code, Place_Of_Supply FROM Place_Of_Supply`)
    /// loaded once, before processing begins, into Dictionary&lt;string,string&gt;.
    /// </summary>
    public class PlaceOfSupplyRepository : IPlaceOfSupplyRepository
    {
        private readonly SqlConnectionFactory _connectionFactory;

        public PlaceOfSupplyRepository(SqlConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory;
        }

        public async Task<Dictionary<string, string>> LoadAllAsync()
        {
            const string sql = "SELECT Code, Place_Of_Supply FROM Place_Of_Supply";

            var map = new Dictionary<string, string>();

            using var conn = await _connectionFactory.CreateOpenConnectionAsync();
            var rows = await conn.QueryAsync<PlaceOfSupply>(sql);

            foreach (var row in rows)
            {
                if (!string.IsNullOrEmpty(row.Code) && !map.ContainsKey(row.Code))
                {
                    map[row.Code] = row.Place_Of_Supply ?? "";
                }
            }

            return map;
        }
    }
}
