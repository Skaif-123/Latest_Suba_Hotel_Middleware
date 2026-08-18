using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AgentSyncConsole.Interfaces.CreditNoteInterface;
using AgentSyncConsole.InvoiceIngest.Interfaces;
using AgentSyncConsole.Models;
using Dapper;
using Microsoft.Extensions.Logging;

namespace AgentSyncConsole.Repositories.CreditNoteRepo
{
    public class CreditRepository : ICreditNoteRepository
    {
        private readonly IDbConnectionFactory _connectionFactory;


        public CreditRepository(
           IDbConnectionFactory connectionFactory)

        {
            _connectionFactory = connectionFactory;

        }


        public async Task<List<ThirdParty_CreditNote>> GetCreditNotesAsync(
            CancellationToken cancellationToken = default)
        {
            using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);

            const string selectSql = @"
            SELECT
                ROWID,
                invoice AS Invoice
            FROM ThirdPartyData
            WHERE invoice IS NOT NULL
              AND LTRIM(RTRIM(invoice)) <> ''";

            var creditNotes = (await connection.QueryAsync<ThirdParty_CreditNote>(
                new CommandDefinition(
                    selectSql,
                    cancellationToken: cancellationToken)))
                .ToList();

            Console.WriteLine("TOTAL CREDIT NOTES FETCHED => {Count}", creditNotes.Count);

            const string updateSql = @"
            UPDATE ThirdPartyData
            SET syncStatus = @SyncStatus,
                syncTime = SYSDATETIME()
            WHERE invoice IS NOT NULL
              AND LTRIM(RTRIM(invoice)) <> ''";

            int rowsAffected = await connection.ExecuteAsync(
                new CommandDefinition(
                    updateSql,
                    new
                    {
                        SyncStatus = "fetched data from thirdparty for insertion CreditNoteDetail Table"
                    },
                    cancellationToken: cancellationToken));

            Console.WriteLine("UPDATED {Count} THIRD PARTY ROWS", rowsAffected);

            return creditNotes;
        }
    }
}
