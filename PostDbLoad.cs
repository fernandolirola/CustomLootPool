using CustomLootPool.Services;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;

namespace CustomLootPool;

[Injectable(TypePriority = OnLoadOrder.PostLoad + 1)]
public class EarlyLoad(LootInjectionService injection) : IOnLoad
{
    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        injection.SnapshotBaseMultipliers();
        return Task.CompletedTask;
    }
}

[Injectable(TypePriority = OnLoadOrder.PostLoad + 5000000)]
public class LateLoad(ConfigService configService, ItemCatalogService catalog, LootInjectionService injection, ExtraLootService extra) : IOnLoad
{
    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        configService.Load();
        catalog.Build();
        injection.Register();
        configService.ConfigChanged += injection.InvalidateCaches;
        extra.EnablePatches();
        _ = injection.EnsurePoolIndex();
        return Task.CompletedTask;
    }
}
