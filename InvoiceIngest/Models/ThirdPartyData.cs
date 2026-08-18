using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AgentSyncConsole.Models
{
    public class ThirdPartyData
    {
        /// <summary>
        /// Added for the Hotelogix Transaction Sync conversion. Every other table
        /// migrated from the Catalyst datastore in this project uses a numeric
        /// "ROWID" (see Invoice.cs); the pre-existing lowercase "rowid" string
        /// property below is left untouched for backward compatibility with
        /// whatever already reads it.
        /// </summary>
        public int ROWID { get; set; }

        public string? rooms { get; set; }
        public string? invoice { get; set; }
        public string? guest { get; set; }
        public string? agent { get; set; }
        public string? corporates { get; set; }
        public string? transactions { get; set; }
        public string? payments { get; set; }
        public string? bookings { get; set; }
        public string? dnrs { get; set; }
        public string? businessSources { get; set; }
        public string? roomTypes { get; set; }
        public string? roomtaxmaster { get; set; }
        public string? payTypes { get; set; }
        public string? posPoint { get; set; }
        public string? posCategory { get; set; }
        public string? posProduct { get; set; }
        public string? posTax { get; set; }
        public string? banks { get; set; }
        public string? posInvoice { get; set; }
        public string? other { get; set; }
        public string? status { get; set; }
        public string? response { get; set; }
        public string? CreatedTime { get; set; }
        public string? rowid { get; set; }
    }
}
