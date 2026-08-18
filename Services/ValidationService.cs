namespace AgentSyncConsole.Services
{
    /// <summary>
    /// Preserves the per-agent validation from the original code:
    ///   if (!customerID) { recordFailure(...Stage:"Validation"...); continue; }
    /// This was the only explicit validation gate before building the row -
    /// every other field is defensively defaulted (String(x || ""),
    /// parseInt(x) || 0), never rejected.
    /// </summary>
    public static class ValidationService
    {
        public static bool IsCustomerIdValid(string customerId)
        {
            return !string.IsNullOrEmpty(customerId);
        }
    }
}
