using SPTarkov.Server.Core.Helpers.InRaid;
using SPTarkov.Server.Core.Models.Common;

namespace CustomLootPool.Services;

internal static class SimulationRaid
{
    internal static (int Static, int Loose) Run(CounterTrackerHelper tracker, Dictionary<MongoId, int>? limits, Func<int> generateStatic, Func<int> generateLoose)
    {
        tracker.Clear();
        if (limits is not null) tracker.AddDataToTrack(new(limits));
        try { return (generateStatic(), generateLoose()); }
        finally { tracker.Clear(); }
    }
}
