using AgentSyncConsole.InvoiceIngest.DTOs;
using AgentSyncConsole.InvoiceIngest.Helpers;
using Microsoft.Extensions.Logging;

namespace AgentSyncConsole.InvoiceIngest.Utilities;

/// <summary>
/// Direct line-by-line port of the original runBatches() helper:
///
///   async function runBatches(rows, operation, label) {
///       let confirmed = 0;
///       const failed = [];
///       for (let i = 0; i < rows.length; i += BATCH_SIZE) {
///           const batch = rows.slice(i, i + BATCH_SIZE);
///           const batchNum = Math.floor(i / BATCH_SIZE) + 1;
///           try {
///               const res = await operation(batch);
///               confirmed += res ? res.length : 0;
///           } catch (batchErr) {
///               context.log(label + ' batch ' + batchNum + ' failed: ' +
///                   batchErr.toString() + ' — falling back to per-row');
///               for (const singleRow of batch) {
///                   try {
///                       await operation([singleRow]);
///                       confirmed++;
///                   } catch (rowErr) {
///                       context.log(label + ' row error: ' + rowErr.toString());
///                       failed.push({ row: singleRow, error: rowErr.toString() });
///                   }
///               }
///           }
///       }
///       return { confirmed, failed };
///   }
///
/// Slices rows into BATCH_SIZE chunks and calls operation() on
/// each. Each batch is independently try/caught so one bad batch
/// never stops the rest. Falls back to per-row calls when a batch
/// fails. Returns total confirmed row count plus every row that
/// failed even after per-row fallback.
/// </summary>
public static class BatchRunner
{
    /// <param name="rows">Full row set to write.</param>
    /// <param name="operation">
    /// Writes one batch and returns the number of rows the
    /// operation confirmed — mirrors `operation(batch)` returning
    /// `res` where `res.length` is the confirmed count.
    /// </param>
    /// <param name="label">Log label, e.g. "Invoice Insert".</param>
    /// <param name="batchSize">BATCH_SIZE.</param>
    public static async Task<BatchResult<TRow>> RunBatchesAsync<TRow>(
        IReadOnlyList<TRow> rows,
        Func<IReadOnlyList<TRow>, Task<int>> operation,
        string label,
        int batchSize,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        var confirmed = 0;
        var failed = new List<FailedRow<TRow>>();

        for (var i = 0; i < rows.Count; i += batchSize)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var batch = rows.Skip(i).Take(batchSize).ToList();
            var batchNum = (i / batchSize) + 1;

            try
            {
                var res = await operation(batch);

                if (res != batch.Count)
                {
                    throw new Exception(
                        $"{label}: Expected {batch.Count} rows to be affected, but only {res} rows were affected.");
                }

                confirmed += res;
            }
            catch (Exception batchErr)
            {
                logger.LogInformation(
                    "{Label} batch {BatchNum} failed: {Error} — falling back to per-row",
                    label, batchNum, batchErr.ToJsString());

                foreach (var singleRow in batch)
                {
                    try
                    {
                        var affected = await operation(new List<TRow> { singleRow });

                        if (affected != 1)
                        {
                            throw new Exception(
                                $"{label}: Expected 1 row to be affected, but {affected} rows were affected.");
                        }

                        confirmed++;
                    }
                    catch (Exception rowErr)
                    {
                        logger.LogInformation(
                            "{Label} row error: {Error}", label, rowErr.ToJsString());

                        failed.Add(new FailedRow<TRow>
                        {
                            Row = singleRow,
                            Error = rowErr.ToJsString()
                        });
                    }
                }
            }
        }

        return new BatchResult<TRow>
        {
            Confirmed = confirmed,
            Failed = failed
        };
    }
}