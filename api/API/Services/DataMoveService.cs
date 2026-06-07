using API.Schema.ActionsContext;
using API.Schema.ActionsContext.Actions;
using log4net;

namespace API.Services;

/// <summary>
/// Moves a file or folder on disk and records a DataMovedActionRecord — the logic formerly in
/// MoveFileOrFolderWorker. Bails (without touching the DB) if the source is missing or the destination
/// already exists, so a failed move never records a phantom action.
/// </summary>
public class DataMoveService
{
    private static readonly ILog Log = LogManager.GetLogger(typeof(DataMoveService));

    public async Task MoveAsync(ActionsContext actionsContext, string fromLocation, string toLocation, CancellationToken ct)
    {
        try
        {
            bool isDir = Directory.Exists(fromLocation);
            bool isFile = File.Exists(fromLocation);

            if (!isDir && !isFile)
            {
                Log.ErrorFormat("Source does not exist at {0}", fromLocation);
                return;
            }

            if (File.Exists(toLocation) || Directory.Exists(toLocation))
            {
                Log.ErrorFormat("Destination already exists at {0}", toLocation);
                return;
            }

            EnsureParentDirectoryExists(toLocation);

            if (isDir)
                Directory.Move(fromLocation, toLocation);
            else
                File.Move(fromLocation, toLocation);
        }
        catch (Exception e)
        {
            Log.Error(e);
            return; // Bail out on IO failure so we don't record a phantom move.
        }

        actionsContext.Actions.Add(new DataMovedActionRecord(fromLocation, toLocation));
        if (await actionsContext.Sync(ct, typeof(DataMoveService), nameof(MoveAsync)) is { success: false } syncError)
            Log.ErrorFormat("Failed to save database changes: {0}", syncError.exceptionMessage);
    }

    private static void EnsureParentDirectoryExists(string path)
    {
        var parentDir = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(parentDir))
            Directory.CreateDirectory(parentDir);
    }
}
