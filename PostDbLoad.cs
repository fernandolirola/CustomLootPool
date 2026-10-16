using CustomLootPool.Services;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;

namespace CustomLootPool;

[Injectable(TypePriority = OnLoadOrder.PostLoad + 1)]
public class PostDbLoad(ConfigService configService, ItemCatalogService catalog, LootInjectionService injection) : IOnLoad
{
    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        configService.Load();
        catalog.Build();
        injection.Register();
        configService.ConfigChanged += injection.InvalidateCaches;
        return Task.CompletedTask;
    }
}
