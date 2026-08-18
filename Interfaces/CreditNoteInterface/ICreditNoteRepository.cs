using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AgentSyncConsole.Models;

namespace AgentSyncConsole.Interfaces.CreditNoteInterface
{
    public interface ICreditNoteRepository
    {
        Task<List<ThirdParty_CreditNote>> GetCreditNotesAsync(CancellationToken cancellationToken = default);
    }
}
